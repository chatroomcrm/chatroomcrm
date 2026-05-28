using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;
using ChatFlowCrm.SignalR;

namespace ChatFlowCrm.Controllers
{
    [ApiController]
    [Route("api/webhook")]
    public class WebhookController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly Services.IDbLoggerService _logger;

        public WebhookController(AppDbContext context, IHubContext<ChatHub> hubContext, Services.IDbLoggerService logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpPost("whatsapp")]
        public async Task<IActionResult> Receive()
        {
            Guid finalTenantId = Guid.Empty;
            try
            {
                // 1. Resolve Tenant ID manually from query parameters to keep the C# method signature parameterless
                var queryTenantId = Request.Query["tenantId"].ToString();
                if (Guid.TryParse(queryTenantId, out var parsedTenantId))
                {
                    finalTenantId = parsedTenantId;
                }
                else
                {
                    // Fallback to the first tenant in DB for sandbox demonstration
                    var firstTenant = await _context.Tenants.FirstOrDefaultAsync();
                    if (firstTenant == null)
                    {
                        // Create default sandbox tenant if db is empty
                        var sandboxTenant = new Tenant { Name = "Sandbox Tenant", ThemeColor = "#00f2fe" };
                        _context.Tenants.Add(sandboxTenant);
                        await _context.SaveChangesAsync();
                        finalTenantId = sandboxTenant.Id;
                    }
                    else
                    {
                        finalTenantId = firstTenant.Id;
                    }
                }

                // 2. Parse Twilio webhook form fields
                var from = Request.Form["From"].ToString(); // Format: whatsapp:+1234567890
                var body = Request.Form["Body"].ToString();
                var messageSid = Request.Form["MessageSid"].ToString();
                var profileName = Request.Form["ProfileName"].ToString(); // Customer's WhatsApp Name

                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(body))
                {
                    await _logger.LogWarningAsync("Received invalid or empty Twilio webhook payload.", "WebhookController.whatsapp", finalTenantId);
                    return BadRequest("Invalid Twilio payload. Missing From or Body.");
                }

                await _logger.LogInfoAsync($"Received WhatsApp webhook message: From={from}, MessageSid={messageSid}", "WebhookController.whatsapp", finalTenantId);

                // Standardize phone number (strip 'whatsapp:' prefix)
                var phoneNumber = from.Replace("whatsapp:", "").Trim();
                var customerName = string.IsNullOrEmpty(profileName) ? $"WhatsApp Contact ({phoneNumber})" : profileName;

                // 3. Find or Create Contact
                var contact = await _context.Contacts
                    .FirstOrDefaultAsync(c => c.TenantId == finalTenantId && c.Phone == phoneNumber);

                if (contact == null)
                {
                    contact = new Contact
                    {
                        Name = customerName,
                        Phone = phoneNumber,
                        TenantId = finalTenantId
                    };
                    _context.Contacts.Add(contact);
                    await _context.SaveChangesAsync(); // Fetch Id
                }

                // 4. Find or Create Lead (Active leads: Status NOT in Won or Lost)
                var lead = await _context.Leads
                    .Include(l => l.Contact)
                    .FirstOrDefaultAsync(l => l.TenantId == finalTenantId && 
                                              l.ContactId == contact.Id && 
                                              l.Status != "Won" && 
                                              l.Status != "Lost");

                bool isNewLead = false;
                if (lead == null)
                {
                    lead = new Lead
                    {
                        ContactId = contact.Id,
                        Status = "New",
                        TenantId = finalTenantId
                    };
                    _context.Leads.Add(lead);
                    await _context.SaveChangesAsync(); // Fetch Id
                    isNewLead = true;
                    
                    // Reload relation for dashboard detail
                    lead.Contact = contact;
                }

                // 5. Create & Save Message
                var message = new Message
                {
                    LeadId = lead.Id,
                    Content = body,
                    Direction = "Incoming",
                    ProviderMessageId = messageSid,
                    Timestamp = DateTime.UtcNow,
                    TenantId = finalTenantId
                };
                _context.Messages.Add(message);
                await _context.SaveChangesAsync();

                // 6. SignalR WebSockets Real-Time Broadcast
                var payload = new
                {
                    messageId = message.Id,
                    leadId = lead.Id,
                    contactId = contact.Id,
                    contactName = contact.Name,
                    phone = contact.Phone,
                    content = message.Content,
                    direction = message.Direction,
                    timestamp = message.Timestamp,
                    isNewLead = isNewLead
                };

                // Broadcast to the tenant's global dashboard
                await _hubContext.Clients.Group($"Tenant_{finalTenantId}").SendAsync("ReceiveMessage", payload);

                // Broadcast to the specific active conversation stream
                await _hubContext.Clients.Group($"Lead_{lead.Id}").SendAsync("ReceiveMessage", payload);

                if (isNewLead)
                {
                    // Let dashboard know a new card needs to render in the Kanban board
                    await _hubContext.Clients.Group($"Tenant_{finalTenantId}").SendAsync("ReceiveNewLead", new
                    {
                        leadId = lead.Id,
                        contactName = contact.Name,
                        phone = contact.Phone,
                        status = lead.Status,
                        timestamp = message.Timestamp
                    });
                }

                return Ok(new { success = true, leadId = lead.Id, messageId = message.Id });
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"Fatal error in ReceiveWhatsAppMessage webhook: {ex.Message}", ex, "WebhookController.whatsapp", finalTenantId == Guid.Empty ? null : finalTenantId);
                return StatusCode(500, "Error processing Twilio webhook.");
            }
        }
    }
}
