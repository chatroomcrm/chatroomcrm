using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;
using ChatFlowCrm.Services;

namespace ChatFlowCrm.Controllers
{
    [ApiController]
    [Route("api/superadmin")]
    [Authorize(Roles = UserRoles.SuperAdmin)]
    public class SuperAdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IDbLoggerService _logger;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        public SuperAdminController(
            AppDbContext context, 
            IDbLoggerService logger, 
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("analytics")]
        public async Task<IActionResult> GetGlobalAnalytics()
        {
            var totalTenants = await _context.Tenants.CountAsync();
            var totalUsers = await _context.Users.CountAsync();
            var totalLeads = await _context.Leads.CountAsync();
            var totalMessages = await _context.Messages.CountAsync();
            var totalLogs = await _context.LogEntries.CountAsync();

            var recentLogs = await _context.LogEntries
                .OrderByDescending(l => l.Timestamp)
                .Take(10)
                .ToListAsync();

            var tenantBreakdown = await _context.Tenants
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    LeadsCount = t.Leads.Count,
                    UsersCount = t.Users.Count,
                    IsBlocked = t.IsBlocked
                })
                .ToListAsync();

            return Ok(new
            {
                totalTenants,
                totalUsers,
                totalLeads,
                totalMessages,
                totalLogs,
                recentLogs,
                tenantBreakdown
            });
        }

        [HttpGet("tenants")]
        public async Task<IActionResult> GetTenants(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var query = _context.Tenants.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(t => t.Name.ToLower().Contains(s));
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var totalCount = await query.CountAsync();
            Response.Headers["X-Pagination-Total-Count"] = totalCount.ToString();

            var tenants = await query
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.LogoUrl,
                    t.ThemeColor,
                    t.IsBlocked,
                    UsersCount = t.Users.Count,
                    LeadsCount = t.Leads.Count
                })
                .OrderBy(t => t.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(tenants);
        }

        public class CreateTenantRequest
        {
            public string Name { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string ThemeColor { get; set; } = string.Empty;

            public string? ServiceType { get; set; } // "Twilio", "Meta", or "None"
            public string WhatsAppNumber { get; set; } = string.Empty;
            public string? ProviderAccountId { get; set; }
            public string? ProviderApiKey { get; set; }
            public string? ProviderSenderId { get; set; }
        }

        [HttpPost("tenants")]
        public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Organization Name is required." });
            }

            var exists = await _context.Tenants.AnyAsync(t => t.Name.ToLower() == request.Name.Trim().ToLower());
            if (exists)
            {
                return BadRequest(new { message = "An organization with this name already exists." });
            }

            string logoUrl = request.LogoUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(logoUrl))
            {
                var words = request.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                logoUrl = words.Length > 1 
                    ? (words[0][0].ToString() + words[1][0].ToString()).ToUpper() 
                    : (request.Name.Length > 1 ? request.Name.Trim().Substring(0, 2).ToUpper() : request.Name.Trim().ToUpper());
            }

            string themeColor = request.ThemeColor?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(themeColor))
            {
                themeColor = "#00f2fe|#4facfe";
            }

            // Real-time API credentials verification
            if (!string.IsNullOrEmpty(request.ServiceType) && !request.ServiceType.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                var isValid = await VerifyProviderCredentialsAsync(
                    request.ServiceType, 
                    request.ProviderAccountId, 
                    request.ProviderApiKey, 
                    request.ProviderSenderId
                );
                if (!isValid)
                {
                    return BadRequest(new { message = $"Verification failed for the selected {request.ServiceType} credentials. Please check your Account SID/ID and Token/Key." });
                }
            }

            var formattedWhatsApp = request.WhatsAppNumber?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(formattedWhatsApp))
            {
                formattedWhatsApp = formattedWhatsApp.Replace("whatsapp:", "").Replace(" ", "").Trim();
            }

            var newTenant = new Tenant
            {
                Name = request.Name.Trim(),
                LogoUrl = logoUrl,
                ThemeColor = themeColor,
                IsBlocked = false,
                ServiceType = request.ServiceType,
                WhatsAppNumber = formattedWhatsApp,
                ProviderAccountId = string.IsNullOrEmpty(request.ProviderAccountId) ? null : request.ProviderAccountId.Trim(),
                ProviderApiKey = string.IsNullOrEmpty(request.ProviderApiKey) ? null : request.ProviderApiKey.Trim(),
                ProviderSenderId = string.IsNullOrEmpty(request.ProviderSenderId) ? null : request.ProviderSenderId.Trim()
            };

            _context.Tenants.Add(newTenant);
            await _context.SaveChangesAsync();

            await _logger.LogInfoAsync($"SuperAdmin created a new organization: TenantName={newTenant.Name}", "SuperAdminController.CreateTenant");

            return Ok(new
            {
                success = true,
                message = $"Organization '{newTenant.Name}' has been added successfully.",
                tenant = new
                {
                    newTenant.Id,
                    newTenant.Name,
                    newTenant.LogoUrl,
                    newTenant.ThemeColor,
                    newTenant.IsBlocked,
                    UsersCount = 0,
                    LeadsCount = 0
                }
            });
        }

        private async Task<bool> VerifyProviderCredentialsAsync(string serviceType, string? accountId, string? apiKey, string? senderId)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                if (serviceType.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(apiKey)) return false;
                    
                    var requestUrl = $"https://api.twilio.com/2010-04-01/Accounts/{accountId}.json";
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, requestUrl);
                    var authBytes = System.Text.Encoding.UTF8.GetBytes($"{accountId}:{apiKey}");
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
                    
                    var response = await client.SendAsync(request);
                    return response.IsSuccessStatusCode;
                }
                else if (serviceType.Equals("Meta", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(apiKey)) return false;
                    
                    var requestUrl = $"https://graph.facebook.com/v20.0/{accountId}";
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, requestUrl);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    
                    var response = await client.SendAsync(request);
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Provider Verification Exception] {ex.Message}");
            }
            return false;
        }

        [HttpPost("tenants/{tenantId}/toggle-block")]
        public async Task<IActionResult> ToggleTenantBlock(Guid tenantId)
        {
            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant == null)
            {
                return NotFound(new { message = "Tenant not found." });
            }

            tenant.IsBlocked = !tenant.IsBlocked;
            await _context.SaveChangesAsync();

            string status = tenant.IsBlocked ? "suspended" : "activated";
            await _logger.LogWarningAsync($"SuperAdmin toggled tenant status: Tenant={tenant.Name}, Status={status}", "SuperAdminController.ToggleTenantBlock");

            return Ok(new { success = true, isBlocked = tenant.IsBlocked, message = $"Organization {tenant.Name} has been {status} successfully." });
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u => 
                    u.Name.ToLower().Contains(s) || 
                    u.Email.ToLower().Contains(s) || 
                    (u.Phone != null && u.Phone.ToLower().Contains(s)) || 
                    u.Role.ToLower().Contains(s)
                );
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var totalCount = await query.CountAsync();
            Response.Headers["X-Pagination-Total-Count"] = totalCount.ToString();

            var users = await query
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.Email,
                    u.Phone,
                    u.Role,
                    u.IsBlocked,
                    u.TenantId,
                    TenantName = u.Tenant != null ? u.Tenant.Name : "Global Platform"
                })
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(users);
        }

        [HttpPost("users/{userId}/toggle-block")]
        public async Task<IActionResult> ToggleUserBlock(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (user.Role == UserRoles.SuperAdmin)
            {
                return BadRequest(new { message = "You cannot suspend another Super Admin user." });
            }

            user.IsBlocked = !user.IsBlocked;
            await _context.SaveChangesAsync();

            string status = user.IsBlocked ? "suspended" : "activated";
            await _logger.LogWarningAsync($"SuperAdmin toggled user status: Email={user.Email}, Status={status}", "SuperAdminController.ToggleUserBlock");

            return Ok(new { success = true, isBlocked = user.IsBlocked, message = $"User {user.Email} has been {status} successfully." });
        }

        [HttpGet("logs")]
        public async Task<IActionResult> GetSystemLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? logLevel = null,
            [FromQuery] string? search = null)
        {
            var query = _context.LogEntries.AsQueryable();

            if (!string.IsNullOrEmpty(logLevel))
            {
                query = query.Where(l => l.LogLevel == logLevel);
            }

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(l => 
                    l.Message.ToLower().Contains(s) || 
                    (l.Exception != null && l.Exception.ToLower().Contains(s)) || 
                    (l.Source != null && l.Source.ToLower().Contains(s))
                );
            }

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var totalCount = await query.CountAsync();
            Response.Headers["X-Pagination-Total-Count"] = totalCount.ToString();

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(logs);
        }

        [HttpPost("purge-all-data")]
        [AllowAnonymous]
        public async Task<IActionResult> PurgeAllData([FromQuery] string secret)
        {
            var configuredSecret = _configuration["Security:PurgeSecret"];
            if (string.IsNullOrEmpty(configuredSecret) || secret != configuredSecret)
            {
                return Forbid("Invalid secret.");
            }

            try
            {
                // Force deletion in order of dependencies
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Messages");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Tasks");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Leads");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Contacts");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM TenantTemplates");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries");

                return Ok(new { success = true, message = "All database tables (Messages, Tasks, Leads, Contacts, Templates, Logs) cleared successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
