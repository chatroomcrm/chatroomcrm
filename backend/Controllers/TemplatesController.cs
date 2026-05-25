using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;
using ChatFlowCrm.Services;

namespace ChatFlowCrm.Controllers
{
    [ApiController]
    [Route("api/templates")]
    [Authorize]
    public class TemplatesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IDbLoggerService _logger;

        public TemplatesController(AppDbContext context, HttpClient httpClient, IDbLoggerService logger)
        {
            _context = context;
            _httpClient = httpClient;
            _logger = logger;
        }

        private Guid ResolveTenantId(Guid? queryTenantId = null)
        {
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (userRole == UserRoles.SuperAdmin && queryTenantId.HasValue)
            {
                return queryTenantId.Value;
            }

            var claim = User.FindFirst("TenantId")?.Value;
            if (Guid.TryParse(claim, out var tenantId))
            {
                return tenantId;
            }
            var fallbackTenant = _context.Tenants.FirstOrDefault();
            return fallbackTenant?.Id ?? Guid.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> GetTemplates([FromQuery] Guid? tenantId)
        {
            var finalTenantId = ResolveTenantId(tenantId);
            var templates = await _context.TenantTemplates
                .Where(t => t.TenantId == finalTenantId)
                .OrderByDescending(t => t.Timestamp)
                .ToListAsync();

            return Ok(templates);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadTemplatesCsv(IFormFile file, [FromQuery] Guid? tenantId)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("Please upload a valid CSV template file.");
            }

            var finalTenantId = ResolveTenantId(tenantId);
            var tenant = await _context.Tenants.FindAsync(finalTenantId);
            if (tenant == null)
            {
                return BadRequest("Tenant not found.");
            }

            var importedTemplates = new List<TenantTemplate>();
            var failedRows = new List<string>();

            try
            {
                using var reader = new StreamReader(file.OpenReadStream());
                string? headerLine = await reader.ReadLineAsync(); // Read headers

                int rowNum = 1;
                while (!reader.EndOfStream)
                {
                    rowNum++;
                    string? line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Parse CSV line cleanly, accounting for basic quotes
                    var parts = ParseCsvLine(line);
                    if (parts.Count < 4)
                    {
                        failedRows.Add($"Row {rowNum}: Missing columns. Expected Name, Category, Language, Body.");
                        continue;
                    }

                    string name = parts[0].Trim().ToLower().Replace(" ", "_");
                    string category = parts[1].Trim().ToUpper();
                    string language = parts[2].Trim();
                    string body = parts[3].Trim();

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(body))
                    {
                        failedRows.Add($"Row {rowNum}: Name and Body are required.");
                        continue;
                    }

                    // Standardize category matching Meta values
                    if (category != "UTILITY" && category != "MARKETING" && category != "AUTHENTICATION")
                    {
                        category = "UTILITY"; // Fallback to standard
                    }

                    // 1. Programmatically upload to Meta Cloud API first if credentials are active
                    string status = "Approved"; // Local simulation status fallback

                    if (!string.IsNullOrEmpty(tenant.MetaAccessToken) && 
                        !string.IsNullOrEmpty(tenant.MetaBusinessAccountId) && 
                        tenant.MetaBusinessAccountId != "PLACEHOLDER")
                    {
                        try
                        {
                            var metaStatus = await CreateTemplateOnMetaAsync(
                                tenant.MetaBusinessAccountId, 
                                tenant.MetaAccessToken, 
                                name, 
                                category, 
                                language, 
                                body
                            );
                            if (metaStatus != null)
                            {
                                status = metaStatus; // Use actual status returned by Meta (Approved, Pending, or Rejected)
                                await _logger.LogInfoAsync($"Successfully registered message template '{name}' on Meta WhatsApp API. Status: {status}", "TemplatesController.Upload", finalTenantId);
                            }
                            else
                            {
                                status = "Rejected"; // Meta API explicitly rejected the structure
                                await _logger.LogWarningAsync($"Meta API rejected template creation for '{name}'. Saved locally as Rejected.", "TemplatesController.Upload", finalTenantId);
                            }
                        }
                        catch (Exception ex)
                        {
                            status = "Pending";
                            await _logger.LogErrorAsync($"Error programmatically creating template '{name}' on Meta. Saved locally as Pending.", ex, "TemplatesController.Upload", finalTenantId);
                        }
                    }
                    else
                    {
                        status = "Simulated"; // Visual indicator showing credentials are not configured
                        await _logger.LogInfoAsync($"Meta Business credentials missing for Tenant. Template '{name}' saved locally for sandbox offline simulation.", "TemplatesController.Upload", finalTenantId);
                    }

                    // 2. Check for duplicate templates to prevent duplicates in local DB
                    var existing = await _context.TenantTemplates
                        .FirstOrDefaultAsync(t => t.TenantId == finalTenantId && t.Name == name);

                    if (existing != null)
                    {
                        existing.Category = category;
                        existing.Language = language;
                        existing.Body = body;
                        existing.Status = status;
                        existing.Timestamp = DateTimeOffset.UtcNow;
                        _context.TenantTemplates.Update(existing);
                        importedTemplates.Add(existing);
                    }
                    else
                    {
                        var newTemplate = new TenantTemplate
                        {
                            TenantId = finalTenantId,
                            Name = name,
                            Category = category,
                            Language = language,
                            Body = body,
                            Status = status,
                            Timestamp = DateTimeOffset.UtcNow
                        };
                        _context.TenantTemplates.Add(newTemplate);
                        importedTemplates.Add(newTemplate);
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"Fatal error parsing template CSV upload: {ex.Message}", ex, "TemplatesController.Upload", finalTenantId);
                return StatusCode(500, $"An error occurred parsing CSV file: {ex.Message}");
            }

            return Ok(new
            {
                success = true,
                message = $"Successfully processed CSV templates file. Imported/updated {importedTemplates.Count} templates.",
                importedCount = importedTemplates.Count,
                templates = importedTemplates.Select(t => new { t.Id, t.Name, t.Category, t.Language, t.Body, t.Status }),
                failedRows = failedRows
            });
        }

        private async Task<string?> CreateTemplateOnMetaAsync(string wabaId, string accessToken, string name, string category, string language, string body)
        {
            var url = $"https://graph.facebook.com/v20.0/{wabaId}/message_templates";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                name = name,
                category = category,
                language = language,
                components = new[]
                {
                    new
                    {
                        type = "BODY",
                        text = body
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("status", out var statusProp))
                    {
                        string metaStatus = statusProp.GetString() ?? "APPROVED";
                        // Standardize string casing to match frontend expected values
                        if (metaStatus.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)) return "Approved";
                        if (metaStatus.Equals("PENDING", StringComparison.OrdinalIgnoreCase)) return "Pending";
                        if (metaStatus.Equals("REJECTED", StringComparison.OrdinalIgnoreCase)) return "Rejected";
                        return metaStatus;
                    }
                }
                catch {}
                return "Approved";
            }

            var err = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[META TEMPLATE CREATION ERROR] Code: {response.StatusCode}. Details: {err}");
            return null; // Indicates API failure
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }
    }
}
