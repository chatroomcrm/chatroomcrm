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
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        public TemplatesController(AppDbContext context, HttpClient httpClient, IDbLoggerService logger, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _context = context;
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
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
        public async Task<IActionResult> GetTemplates(
            [FromQuery] Guid? tenantId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var finalTenantId = ResolveTenantId(tenantId);
            var query = _context.TenantTemplates
                .Where(t => t.TenantId == finalTenantId);

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(t => 
                    t.Name.ToLower().Contains(s) || 
                    t.Body.ToLower().Contains(s) || 
                    t.Category.ToLower().Contains(s)
                );
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var totalCount = await query.CountAsync();
            Response.Headers["X-Pagination-Total-Count"] = totalCount.ToString();

            var templates = await query
                .OrderByDescending(t => t.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(templates);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadTemplatesCsv(IFormFile file, [FromQuery] Guid? tenantId, [FromQuery] string language = "en")
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

            // Standardize language code from client input (default is en)
            if (string.IsNullOrWhiteSpace(language))
            {
                language = "en";
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
                    if (parts.Count < 3)
                    {
                        failedRows.Add($"Row {rowNum}: Missing columns. Expected Name, Category, Body.");
                        continue;
                    }

                    string name = parts[0].Trim().ToLower().Replace(" ", "_");
                    string category = parts[1].Trim().ToUpper();
                    string body = parts[2].Trim();

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

                    // 1. Programmatically upload to Meta Cloud API first if Meta is the preferred provider and credentials are active
                    string status = "Approved"; // Local simulation status fallback

                    var preferredProvider = _configuration["Messaging:PreferredProvider"] ?? "Meta";
                    bool isTwilio = preferredProvider.Equals("Twilio", StringComparison.OrdinalIgnoreCase);

                    if (isTwilio)
                    {
                        status = "Approved"; // Automatically approved locally for Twilio direct messaging
                        await _logger.LogInfoAsync($"Twilio is active preferred provider. Template '{name}' saved locally and marked as Approved.", "TemplatesController.Upload", finalTenantId);
                    }
                    else
                    {
                        var providerToken = !string.IsNullOrEmpty(tenant.ProviderApiKey) ? tenant.ProviderApiKey : _configuration["Meta:AccessToken"];
                        var wabaId = tenant.ProviderAccountId;

                        if (!string.IsNullOrEmpty(providerToken) && 
                            !string.IsNullOrEmpty(wabaId) && 
                            wabaId != "PLACEHOLDER")
                        {
                            try
                            {
                                var metaStatus = await CreateTemplateOnMetaAsync(
                                    finalTenantId,
                                    wabaId, 
                                    providerToken, 
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

        [HttpPost("sync")]
        public async Task<IActionResult> SyncTemplatesFromMeta([FromQuery] Guid? tenantId)
        {
            var finalTenantId = ResolveTenantId(tenantId);
            var tenant = await _context.Tenants.FindAsync(finalTenantId);
            if (tenant == null)
            {
                return BadRequest("Tenant not found.");
            }

            var providerToken = !string.IsNullOrEmpty(tenant.ProviderApiKey) ? tenant.ProviderApiKey : _configuration["Meta:AccessToken"];
            var wabaId = tenant.ProviderAccountId;

            if (string.IsNullOrEmpty(providerToken) || string.IsNullOrEmpty(wabaId) || wabaId == "PLACEHOLDER")
            {
                return BadRequest("Meta Business credentials (WABA ID or Access Token) are not configured for this tenant.");
            }

            try
            {
                var url = $"https://graph.facebook.com/v20.0/{wabaId}/message_templates?limit=100";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    await _logger.LogErrorAsync($"Meta templates fetch failed (HTTP {response.StatusCode}): {err}", null, "TemplatesController.Sync", finalTenantId);
                    return StatusCode((int)response.StatusCode, $"Failed to fetch templates from Meta API: {err}");
                }

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (!doc.RootElement.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                {
                    return Ok(new { success = true, syncedCount = 0, message = "No templates found on Meta." });
                }

                int syncedCount = 0;
                var now = DateTimeOffset.UtcNow;

                foreach (var item in dataArray.EnumerateArray())
                {
                    string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string category = item.TryGetProperty("category", out var c) ? c.GetString() ?? "UTILITY" : "UTILITY";
                    string language = item.TryGetProperty("language", out var l) ? l.GetString() ?? "en_US" : "en_US";
                    string status = item.TryGetProperty("status", out var s) ? s.GetString() ?? "APPROVED" : "APPROVED";
                    
                    // Extract body text from components
                    string body = "";
                    if (item.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            if (component.TryGetProperty("type", out var typeProp) && 
                                (typeProp.GetString() ?? "").Equals("BODY", StringComparison.OrdinalIgnoreCase))
                            {
                                body = component.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(body))
                    {
                        continue;
                    }

                    // Map status format to match CRM values
                    if (status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)) status = "Approved";
                    else if (status.Equals("PENDING", StringComparison.OrdinalIgnoreCase)) status = "Pending";
                    else if (status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase)) status = "Rejected";

                    // Check for existing record
                    var existing = await _context.TenantTemplates
                        .FirstOrDefaultAsync(t => t.TenantId == finalTenantId && t.Name == name);

                    if (existing != null)
                    {
                        existing.Category = category;
                        existing.Language = language;
                        existing.Body = body;
                        existing.Status = status;
                        existing.Timestamp = now;
                        _context.TenantTemplates.Update(existing);
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
                            Timestamp = now
                        };
                        _context.TenantTemplates.Add(newTemplate);
                    }
                    syncedCount++;
                }

                await _context.SaveChangesAsync();
                await _logger.LogInfoAsync($"Successfully synced {syncedCount} templates from Meta WhatsApp API.", "TemplatesController.Sync", finalTenantId);

                return Ok(new { success = true, syncedCount, message = $"Successfully synced {syncedCount} templates from Meta WhatsApp API." });
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"Error syncing templates from Meta API: {ex.Message}", ex, "TemplatesController.Sync", finalTenantId);
                return StatusCode(500, $"An error occurred during template synchronization: {ex.Message}");
            }
        }

        private async Task<string?> CreateTemplateOnMetaAsync(Guid tenantId, string wabaId, string accessToken, string name, string category, string language, string body)
        {
            var url = $"https://graph.facebook.com/v20.0/{wabaId}/message_templates";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                name = name,
                category = category,
                language = new
                {
                    code = language
                },
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

            try
            {
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
                try
                {
                    var apiException = new HttpRequestException($"Meta Cloud API returned HTTP {(int)response.StatusCode} ({response.StatusCode}). Response Body: {err}");
                    await _logger.LogErrorAsync(
                        $"Meta template creation failed for '{name}' (HTTP {response.StatusCode}). Failed API response recorded.", 
                        apiException, 
                        "TemplatesController.CreateTemplateOnMetaAsync", 
                        tenantId
                    );
                }
                catch {}
                return null; // Indicates API failure
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[META TEMPLATE CREATION EXCEPTION] Details: {ex}");
                try
                {
                    await _logger.LogErrorAsync(
                        $"Exception while executing Meta template creation API for '{name}': {ex.Message}", 
                        ex, 
                        "TemplatesController.CreateTemplateOnMetaAsync", 
                        tenantId
                    );
                }
                catch {}
                throw;
            }
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
