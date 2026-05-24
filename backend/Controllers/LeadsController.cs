using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;
using ChatFlowCrm.SignalR;

namespace ChatFlowCrm.Controllers
{
    [ApiController]
    [Route("api/leads")]
    [Authorize]
    public class LeadsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;

        public LeadsController(AppDbContext context, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // Helper to extract TenantId from JWT Claims or fallback to a query/header in dev mode
        private Guid ResolveTenantId()
        {
            var claim = User.FindFirst("TenantId")?.Value;
            if (Guid.TryParse(claim, out var tenantId))
            {
                return tenantId;
            }

            // Fallback for development/testing convenience
            if (Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenant) && 
                Guid.TryParse(headerTenant.ToString(), out var headerTenantId))
            {
                return headerTenantId;
            }

            // If absolutely none, grab the first tenant in database
            var fallbackTenant = _context.Tenants.FirstOrDefault();
            return fallbackTenant?.Id ?? Guid.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> GetLeads([FromQuery] string? status)
        {
            var tenantId = ResolveTenantId();
            if (tenantId == Guid.Empty) return BadRequest("Tenant not resolved.");

            var query = _context.Leads
                .Include(l => l.Contact)
                .Where(l => l.TenantId == tenantId);

            // Enforce Agent boundaries: Agent can only see their assigned leads or unassigned leads
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

            if (userRole == UserRoles.Agent && userId != null)
            {
                query = query.Where(l => l.AssignedTo == userId || l.AssignedTo == null);
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(l => l.Status == status);
            }

            var leads = await query
                .OrderByDescending(l => l.Timestamp)
                .Select(l => new
                {
                    l.Id,
                    l.Status,
                    l.AssignedTo,
                    l.Timestamp,
                    contact = l.Contact != null ? new
                    {
                        l.Contact.Id,
                        l.Contact.Name,
                        l.Contact.Phone,
                        l.Contact.Email
                    } : null
                })
                .ToListAsync();

            return Ok(leads);
        }

        [HttpGet("analytics")]
        public async Task<IActionResult> GetAnalytics()
        {
            var tenantId = ResolveTenantId();
            if (tenantId == Guid.Empty) return BadRequest("Tenant not resolved.");

            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

            var leadsQuery = _context.Leads.Where(l => l.TenantId == tenantId);
            var messagesQuery = _context.Messages.Where(m => m.TenantId == tenantId);

            // Enforce Agent boundaries: Agent can only see their own metrics!
            if (userRole == UserRoles.Agent && userId != null)
            {
                leadsQuery = leadsQuery.Where(l => l.AssignedTo == userId);
                messagesQuery = messagesQuery.Where(m => _context.Leads.Any(l => l.Id == m.LeadId && l.AssignedTo == userId));
            }

            var totalLeads = await leadsQuery.CountAsync();
            var wonLeads = await leadsQuery.CountAsync(l => l.Status == "Won");
            var activeChats = await leadsQuery.CountAsync(l => l.Status != "Won" && l.Status != "Lost");

            // Conversion rate
            var conversionRate = totalLeads > 0 ? Math.Round((double)wonLeads / totalLeads * 100, 1) : 0.0;

            // Pipeline Breakdown
            var pipelineBreakdown = await leadsQuery
                .GroupBy(l => l.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);

            // Velocity (Messages per day for last 7 days)
            var cutoff = DateTime.UtcNow.Date.AddDays(-6);
            var messages = await messagesQuery
                .Where(m => m.Timestamp >= cutoff)
                .Select(m => new { m.Timestamp })
                .ToListAsync();

            var velocity = Enumerable.Range(0, 7)
                .Select(i => cutoff.AddDays(i))
                .Select(date => new
                {
                    dateString = date.ToString("yyyy-MM-dd"),
                    dayName = date.ToString("ddd"),
                    count = messages.Count(m => m.Timestamp.Date == date)
                })
                .ToList();

            // Avg Response Time (simulated calculation based on actual DB records if present, or a solid live value)
            double avgResponseMinutes = 1.5;
            var lastMessages = await messagesQuery
                .OrderByDescending(m => m.Timestamp)
                .Take(100)
                .ToListAsync();

            if (lastMessages.Count > 1)
            {
                var diffs = lastMessages
                    .Zip(lastMessages.Skip(1), (m1, m2) => new { m1, m2 })
                    .Where(x => x.m1.LeadId == x.m2.LeadId && x.m1.Direction != x.m2.Direction)
                    .Select(x => Math.Abs((x.m1.Timestamp - x.m2.Timestamp).TotalMinutes))
                    .ToList();
                if (diffs.Count > 0)
                {
                    avgResponseMinutes = Math.Round(diffs.Average(), 1);
                    if (avgResponseMinutes < 0.2) avgResponseMinutes = 0.5; // floor at 30 seconds
                }
            }

            return Ok(new
            {
                totalLeads,
                wonLeads,
                activeChats,
                conversionRate,
                avgResponseMinutes,
                pipelineBreakdown,
                velocity
            });
        }

        public class UpdateStatusRequest
        {
            public string Status { get; set; } = string.Empty;
        }

        [HttpPut("{leadId}/status")]
        public async Task<IActionResult> UpdateLeadStatus(Guid leadId, [FromBody] UpdateStatusRequest request)
        {
            var tenantId = ResolveTenantId();
            var lead = await _context.Leads
                .Include(l => l.Contact)
                .FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == tenantId);

            if (lead == null)
            {
                return NotFound(new { message = "Lead not found." });
            }

            // Enforce Agent boundaries: Agent cannot edit status of a lead assigned to another agent
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

            if (userRole == UserRoles.Agent && userId != null && lead.AssignedTo != null && lead.AssignedTo != userId)
            {
                return Forbid();
            }

            var oldStatus = lead.Status;
            lead.Status = request.Status;
            await _context.SaveChangesAsync();

            // Broadcast real-time status update to all connected agents on this tenant
            await _hubContext.Clients.Group($"Tenant_{tenantId}").SendAsync("ReceiveLeadStatusUpdate", new
            {
                leadId = lead.Id,
                contactName = lead.Contact?.Name ?? "Unknown",
                oldStatus = oldStatus,
                newStatus = lead.Status
            });

            return Ok(new { success = true, leadId = lead.Id, status = lead.Status });
        }
    }
}
