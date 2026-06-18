using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;
using ChatFlowCrm.Services;
using ChatFlowCrm.SignalR;

namespace ChatFlowCrm.Controllers
{
    [ApiController]
    [Route("api/messages")]
    [Authorize]
    public class MessagesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWhatsAppService _whatsAppService;
        private readonly IHubContext<ChatHub> _hubContext;

        public MessagesController(AppDbContext context, IWhatsAppService whatsAppService, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _whatsAppService = whatsAppService;
            _hubContext = hubContext;
        }

        private Guid ResolveTenantId()
        {
            var claim = User.FindFirst("TenantId")?.Value;
            if (Guid.TryParse(claim, out var tenantId))
            {
                return tenantId;
            }
            if (Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenant) && 
                Guid.TryParse(headerTenant.ToString(), out var headerTenantId))
            {
                return headerTenantId;
            }
            var fallbackTenant = _context.Tenants.FirstOrDefault();
            return fallbackTenant?.Id ?? Guid.Empty;
        }

        [HttpGet("{leadId}")]
        public async Task<IActionResult> GetMessageHistory(Guid leadId)
        {
            var tenantId = ResolveTenantId();
            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == tenantId);
            if (lead == null)
            {
                return NotFound(new { message = "Lead not found or inaccessible." });
            }

            // Enforce Agent boundaries: Agent cannot read chat history of a lead assigned to another agent
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

            if (userRole == UserRoles.Agent && userId != null && lead.AssignedTo != null && lead.AssignedTo != userId)
            {
                return Forbid();
            }

            var messages = await _context.Messages
                .Where(m => m.LeadId == leadId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new
                {
                    m.Id,
                    m.Content,
                    m.Direction,
                    m.ProviderMessageId,
                    m.Timestamp
                })
                .ToListAsync();

            return Ok(messages);
        }

        public class SendMessageRequest
        {
            public Guid LeadId { get; set; }
            public string Content { get; set; } = string.Empty;
            public string? TemplateName { get; set; }
            public List<string>? TemplateParameters { get; set; }
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var tenantId = ResolveTenantId();
            
            // Find lead and associated contact phone
            var lead = await _context.Leads
                .Include(l => l.Contact)
                .FirstOrDefaultAsync(l => l.Id == request.LeadId && l.TenantId == tenantId);

            if (lead == null || lead.Contact == null)
            {
                return BadRequest("Lead or associated contact not found.");
            }

            // Enforce Agent boundaries: Agent cannot send messages to a lead assigned to another agent
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdClaim, out var parsedId) ? parsedId : null;

            if (userRole == UserRoles.Agent && userId != null && lead.AssignedTo != null && lead.AssignedTo != userId)
            {
                return Forbid();
            }

            bool sent = false;
            string messageBody = request.Content;

            // Check if template sending is requested
            if (!string.IsNullOrEmpty(request.TemplateName))
            {
                var template = await _context.TenantTemplates
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == request.TemplateName);

                if (template == null)
                {
                    return BadRequest($"Template '{request.TemplateName}' not found.");
                }

                // Resolve parameter place-holders in template body
                messageBody = template.Body;
                var paramList = request.TemplateParameters ?? new List<string>();
                for (int i = 0; i < paramList.Count; i++)
                {
                    messageBody = messageBody.Replace($"{{{{{i + 1}}}}}", paramList[i]);
                }

                // Call template dispatcher on WhatsApp service
                sent = await _whatsAppService.SendWhatsAppTemplateAsync(
                    lead.Contact.Phone,
                    template.Name,
                    template.Language,
                    paramList,
                    messageBody,
                    tenantId.ToString()
                );
            }
            else
            {
                // Call standard free-text dispatch service
                sent = await _whatsAppService.SendWhatsAppMessageAsync(
                    lead.Contact.Phone, 
                    request.Content, 
                    tenantId.ToString()
                );
            }

            if (!sent)
            {
                return StatusCode(500, "Failed to deliver WhatsApp message via provider.");
            }

            // 2. Save Outgoing Message in DB
            var message = new Message
            {
                LeadId = lead.Id,
                Content = messageBody,
                Direction = "Outgoing",
                ProviderMessageId = Guid.NewGuid().ToString(), // Simulated output ID if sandbox
                Timestamp = DateTime.UtcNow,
                TenantId = tenantId
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // 3. Broadcast Outgoing Message via SignalR
            var payload = new
            {
                messageId = message.Id,
                leadId = lead.Id,
                contactName = lead.Contact.Name,
                content = message.Content,
                direction = message.Direction,
                timestamp = message.Timestamp
            };

            // Broadcast to the tenant's global dashboard
            await _hubContext.Clients.Group($"Tenant_{tenantId}").SendAsync("ReceiveMessage", payload);

            // Broadcast to the specific active conversation stream
            await _hubContext.Clients.Group($"Lead_{lead.Id}").SendAsync("ReceiveMessage", payload);

            return Ok(new { success = true, messageId = message.Id });
        }
    }
}
