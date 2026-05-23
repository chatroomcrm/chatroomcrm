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

        public SuperAdminController(AppDbContext context, IDbLoggerService logger)
        {
            _context = context;
            _logger = logger;
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
        public async Task<IActionResult> GetTenants()
        {
            var tenants = await _context.Tenants
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
                .ToListAsync();

            return Ok(tenants);
        }

        public class CreateTenantRequest
        {
            public string Name { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string ThemeColor { get; set; } = string.Empty;
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

            var newTenant = new Tenant
            {
                Name = request.Name.Trim(),
                LogoUrl = logoUrl,
                ThemeColor = themeColor,
                IsBlocked = false
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
        public async Task<IActionResult> GetUsers()
        {
            var users = await _context.Users
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
        public async Task<IActionResult> GetSystemLogs([FromQuery] int limit = 100, [FromQuery] string? logLevel = null)
        {
            var query = _context.LogEntries.AsQueryable();

            if (!string.IsNullOrEmpty(logLevel))
            {
                query = query.Where(l => l.LogLevel == logLevel);
            }

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Take(limit)
                .ToListAsync();

            return Ok(logs);
        }
    }
}
