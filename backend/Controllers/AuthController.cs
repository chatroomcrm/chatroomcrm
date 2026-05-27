using System;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;

namespace ChatFlowCrm.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly Services.IDbLoggerService _logger;

        public AuthController(AppDbContext context, IConfiguration configuration, Services.IDbLoggerService logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public class LoginRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class RegisterRequest
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string TenantName { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public Guid? TenantId { get; set; } = null;
        }

        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
            {
                return Unauthorized(new { message = "Invalid authentication claims." });
            }

            var caller = await _context.Users.FindAsync(callerId);
            if (caller == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            if (caller.Role != UserRoles.SuperAdmin && caller.Role != UserRoles.TenantAdmin)
            {
                return StatusCode(403, new { message = "Access denied. Only Super Admin and Tenant Admin can register users." });
            }

            var exists = await _context.Users.AnyAsync(u => u.Email == request.Email);
            if (exists)
            {
                return BadRequest(new { message = "Email already registered." });
            }

            string targetRole;
            Guid? targetTenantId = null;

            if (caller.Role == UserRoles.TenantAdmin)
            {
                targetRole = string.IsNullOrEmpty(request.Role) ? UserRoles.Agent : request.Role;
                if (targetRole != UserRoles.Agent)
                {
                    return BadRequest(new { message = "Tenant Admin is allowed to create only Agent users." });
                }
                targetTenantId = caller.TenantId;
            }
            else // SuperAdmin
            {
                targetRole = string.IsNullOrEmpty(request.Role) ? UserRoles.TenantAdmin : request.Role;
                if (targetRole != UserRoles.SuperAdmin && targetRole != UserRoles.TenantAdmin && targetRole != UserRoles.Agent)
                {
                    return BadRequest(new { message = "Invalid role specified." });
                }

                if (targetRole != UserRoles.SuperAdmin)
                {
                    if (request.TenantId.HasValue)
                    {
                        var tenantExists = await _context.Tenants.AnyAsync(t => t.Id == request.TenantId.Value);
                        if (!tenantExists)
                        {
                            return BadRequest(new { message = "Specified Tenant does not exist." });
                        }
                        targetTenantId = request.TenantId.Value;
                    }
                    else if (!string.IsNullOrEmpty(request.TenantName))
                    {
                        var newTenant = new Tenant { Name = request.TenantName };
                        _context.Tenants.Add(newTenant);
                        await _context.SaveChangesAsync();
                        targetTenantId = newTenant.Id;
                    }
                    else
                    {
                        return BadRequest(new { message = "Tenant ID or Tenant Name is required for TenantAdmin or Agent roles." });
                    }
                }
            }

            // Create new User
            var user = new User
            {
                Name = request.Name,
                Email = request.Email,
                Phone = request.Phone,
                PasswordHash = HashPassword(request.Password),
                Role = targetRole,
                TenantId = targetTenantId
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Fetch tenant name for response representation
            string responseTenantName = "Global Platform";
            if (targetTenantId.HasValue)
            {
                var tenant = await _context.Tenants.FindAsync(targetTenantId.Value);
                responseTenantName = tenant?.Name ?? "My Organization";
            }

            await _logger.LogInfoAsync($"New user registered by {caller.Role} ({caller.Email}): Email={user.Email}, Role={user.Role}", "AuthController.Register", targetTenantId);

            var token = GenerateJwtToken(user);

            return Ok(new
            {
                token,
                user = new
                {
                    user.Id,
                    user.Name,
                    user.Email,
                    user.Role,
                    user.TenantId,
                    TenantName = responseTenantName
                }
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _context.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == request.Email);

            if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
            {
                await _logger.LogWarningAsync($"Failed login attempt: Email={request.Email}", "AuthController.Login");
                return Unauthorized(new { message = "Invalid email or password." });
            }

            if (user.IsBlocked)
            {
                await _logger.LogWarningAsync($"Suspended user login attempt: Email={user.Email}", "AuthController.Login", user.TenantId);
                return StatusCode(403, new { message = "Your user account has been suspended by the platform administrator." });
            }

            if (user.Tenant != null && user.Tenant.IsBlocked)
            {
                await _logger.LogWarningAsync($"Login attempt for suspended organization: Email={user.Email}, Tenant={user.Tenant.Name}", "AuthController.Login", user.TenantId);
                return StatusCode(403, new { message = "Your organization has been suspended by the platform administrator." });
            }

            await _logger.LogInfoAsync($"User logged in successfully: Email={user.Email}", "AuthController.Login", user.TenantId);

            var token = GenerateJwtToken(user);

            return Ok(new
            {
                token,
                user = new
                {
                    user.Id,
                    user.Name,
                    user.Email,
                    user.Role,
                    user.TenantId,
                    TenantName = user.Tenant?.Name ?? (user.Role == UserRoles.SuperAdmin ? "Global Platform" : "My Organization")
                }
            });
        }

        [Authorize(Roles = "SuperAdmin,TenantAdmin")]
        [HttpGet("team")]
        public async Task<IActionResult> GetTeam()
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
            {
                return Unauthorized(new { message = "Invalid authentication claims." });
            }

            var caller = await _context.Users.FindAsync(callerId);
            if (caller == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            if (caller.Role == UserRoles.SuperAdmin)
            {
                var allUsers = await _context.Users
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
                return Ok(allUsers);
            }
            else // TenantAdmin
            {
                var tenantUsers = await _context.Users
                    .Where(u => u.TenantId == caller.TenantId)
                    .Select(u => new
                    {
                        u.Id,
                        u.Name,
                        u.Email,
                        u.Phone,
                        u.Role,
                        u.IsBlocked,
                        u.TenantId,
                        TenantName = u.Tenant != null ? u.Tenant.Name : "My Organization"
                    })
                    .OrderBy(u => u.Email)
                    .ToListAsync();
                return Ok(tenantUsers);
            }
        }

        public class UpdateBrandingRequest
        {
            public string Name { get; set; } = string.Empty;
            public string LogoUrl { get; set; } = string.Empty;
            public string ThemeColor { get; set; } = string.Empty;
        }

        [Authorize]
        [HttpGet("branding")]
        public async Task<IActionResult> GetBranding()
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
            {
                return Unauthorized(new { message = "Invalid authentication claims." });
            }

            var caller = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == callerId);
            if (caller == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            if (caller.Tenant == null)
            {
                return Ok(new
                {
                    name = caller.Role == UserRoles.SuperAdmin ? "Global Platform" : "My Organization",
                    logoUrl = caller.Role == UserRoles.SuperAdmin ? "GP" : "MO",
                    themeColor = "#00f2fe|#4facfe"
                });
            }

            return Ok(new
            {
                name = caller.Tenant.Name,
                logoUrl = caller.Tenant.LogoUrl,
                themeColor = caller.Tenant.ThemeColor
            });
        }

        [Authorize(Roles = "SuperAdmin,TenantAdmin")]
        [HttpPut("branding")]
        public async Task<IActionResult> UpdateBranding([FromBody] UpdateBrandingRequest request)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
            {
                return Unauthorized(new { message = "Invalid authentication claims." });
            }

            var caller = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == callerId);
            if (caller == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            if (caller.Tenant == null)
            {
                return BadRequest(new { message = "Only users assigned to a tenant organization can customize branding settings." });
            }

            if (string.IsNullOrEmpty(request.Name))
            {
                return BadRequest(new { message = "Custom Brand Header Name is required." });
            }

            caller.Tenant.Name = request.Name;
            caller.Tenant.LogoUrl = request.LogoUrl;
            caller.Tenant.ThemeColor = request.ThemeColor;

            _context.Tenants.Update(caller.Tenant);
            await _context.SaveChangesAsync();

            await _logger.LogInfoAsync($"White-Label Brand Customization updated by {caller.Role} ({caller.Email}): Name={caller.Tenant.Name}, ThemeColor={caller.Tenant.ThemeColor}", "AuthController.UpdateBranding", caller.TenantId);

            return Ok(new
            {
                message = "Branding settings saved successfully.",
                name = caller.Tenant.Name,
                logoUrl = caller.Tenant.LogoUrl,
                themeColor = caller.Tenant.ThemeColor
            });
        }

        public class UpdateMessagingRequest
        {
            public string? MessagingProvider { get; set; }
            public string WhatsAppNumber { get; set; } = string.Empty;
            public string? ProviderAccountId { get; set; }
            public string? ProviderApiKey { get; set; }
            public string? ProviderSenderId { get; set; }
        }

        [Authorize(Roles = "TenantAdmin")]
        [HttpGet("messaging")]
        public async Task<IActionResult> GetMessaging()
        {
            var callerIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
            {
                return Unauthorized(new { message = "Invalid authentication claims." });
            }

            var caller = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == callerId);
            if (caller == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            if (caller.Tenant == null)
            {
                return BadRequest(new { message = "Only users assigned to a tenant organization can read messaging settings." });
            }

            return Ok(new
            {
                messagingProvider = caller.Tenant.MessagingProvider,
                whatsAppNumber = caller.Tenant.WhatsAppNumber,
                providerAccountId = caller.Tenant.ProviderAccountId,
                providerApiKey = caller.Tenant.ProviderApiKey,
                providerSenderId = caller.Tenant.ProviderSenderId
            });
        }

        [Authorize(Roles = "TenantAdmin")]
        [HttpPut("messaging")]
        public async Task<IActionResult> UpdateMessaging([FromBody] UpdateMessagingRequest request)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
            {
                return Unauthorized(new { message = "Invalid authentication claims." });
            }

            var caller = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == callerId);
            if (caller == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            if (caller.Tenant == null)
            {
                return BadRequest(new { message = "Only users assigned to a tenant organization can configure messaging settings." });
            }

            var formattedWhatsApp = request.WhatsAppNumber.Trim();
            if (!string.IsNullOrEmpty(formattedWhatsApp))
            {
                formattedWhatsApp = formattedWhatsApp.Replace("whatsapp:", "").Replace(" ", "").Trim();
            }

            caller.Tenant.MessagingProvider = request.MessagingProvider;
            caller.Tenant.WhatsAppNumber = formattedWhatsApp;
            caller.Tenant.ProviderAccountId = string.IsNullOrEmpty(request.ProviderAccountId) ? null : request.ProviderAccountId.Trim();
            caller.Tenant.ProviderApiKey = string.IsNullOrEmpty(request.ProviderApiKey) ? null : request.ProviderApiKey.Trim();
            caller.Tenant.ProviderSenderId = string.IsNullOrEmpty(request.ProviderSenderId) ? null : request.ProviderSenderId.Trim();

            _context.Tenants.Update(caller.Tenant);
            await _context.SaveChangesAsync();

            await _logger.LogInfoAsync($"Tenant messaging settings updated by {caller.Role} ({caller.Email}): Provider={caller.Tenant.MessagingProvider}, WhatsApp={caller.Tenant.WhatsAppNumber}", "AuthController.UpdateMessaging", caller.TenantId);

            return Ok(new
            {
                message = "Messaging settings saved successfully.",
                messagingProvider = caller.Tenant.MessagingProvider,
                whatsAppNumber = caller.Tenant.WhatsAppNumber,
                providerAccountId = caller.Tenant.ProviderAccountId,
                providerApiKey = caller.Tenant.ProviderApiKey,
                providerSenderId = caller.Tenant.ProviderSenderId
            });
        }

        public class UpdateUserRequest
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public string? Password { get; set; }
        }

        [Authorize(Roles = "TenantAdmin")]
        [HttpPut("users/{userId}")]
        public async Task<IActionResult> UpdateUser(Guid userId, [FromBody] UpdateUserRequest request)
        {
            var callerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(callerIdStr) || !Guid.TryParse(callerIdStr, out var callerId))
            {
                return Unauthorized(new { message = "Invalid authentication claims." });
            }

            var caller = await _context.Users.FindAsync(callerId);
            if (caller == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            var userToEdit = await _context.Users.FindAsync(userId);
            if (userToEdit == null)
            {
                return NotFound(new { message = "User account not found." });
            }

            // Enforce Tenant boundaries:
            // 1. TenantAdmin can only edit users within their own organization
            // 2. TenantAdmin cannot edit a SuperAdmin user
            if (caller.Role == UserRoles.TenantAdmin)
            {
                if (userToEdit.TenantId != caller.TenantId)
                {
                    return Forbid();
                }
                if (userToEdit.Role == UserRoles.SuperAdmin)
                {
                    return Forbid();
                }
            }

            // Check if email already belongs to another user
            var emailExists = await _context.Users.AnyAsync(u => u.Email == request.Email && u.Id != userId);
            if (emailExists)
            {
                return BadRequest(new { message = "This email address is already registered to another user account." });
            }

            userToEdit.Name = request.Name;
            userToEdit.Email = request.Email;
            userToEdit.Phone = request.Phone;

            if (!string.IsNullOrEmpty(request.Password))
            {
                userToEdit.PasswordHash = HashPassword(request.Password);
            }

            _context.Users.Update(userToEdit);
            await _context.SaveChangesAsync();

            await _logger.LogInfoAsync($"User account updated by {caller.Role} ({caller.Email}): Email={userToEdit.Email}, Name={userToEdit.Name}", "AuthController.UpdateUser", userToEdit.TenantId);

            return Ok(new
            {
                success = true,
                message = "User details updated successfully.",
                user = new
                {
                    userToEdit.Id,
                    userToEdit.Name,
                    userToEdit.Email,
                    userToEdit.Phone,
                    userToEdit.Role,
                    userToEdit.IsBlocked,
                    userToEdit.TenantId
                }
            });
        }

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtKey = _configuration["Jwt:Key"] ?? "SUPER_SECRET_CHATFLOW_SECURITY_KEY_2026";
            var key = Encoding.ASCII.GetBytes(jwtKey);

            // Fetch tenant dynamically to load active unified configurations into the cryptographically signed session
            Tenant? tenant = null;
            if (user.TenantId.HasValue)
            {
                tenant = _context.Tenants.Find(user.TenantId.Value);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("TenantId", user.TenantId?.ToString() ?? ""),
                new Claim("MessagingProvider", tenant?.MessagingProvider ?? ""),
                new Claim("TenantWhatsApp", tenant?.WhatsAppNumber ?? ""),
                new Claim("ProviderAccountId", tenant?.ProviderAccountId ?? ""),
                new Claim("ProviderApiKey", tenant?.ProviderApiKey ?? ""),
                new Claim("ProviderSenderId", tenant?.ProviderSenderId ?? "")
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _configuration["Jwt:Issuer"] ?? "ChatFlowCrm",
                Audience = _configuration["Jwt:Audience"] ?? "ChatFlowClients"
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private string HashPassword(string password)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        private bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }
    }
}
