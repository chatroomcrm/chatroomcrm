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
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> ReceiveWhatsAppMessage([FromQuery] Guid? tenantId)
        {
            // 1. Resolve Tenant ID
            Guid finalTenantId;
            if (tenantId.HasValue)
            {
                finalTenantId = tenantId.Value;
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

        [HttpGet("meta")]
        public IActionResult VerifyMetaWebhook(
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.challenge")] string? challenge,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken)
        {
            const string defaultVerifyToken = "ChatRoomMetaToken2026";
            
            if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(verifyToken))
            {
                return BadRequest("Missing hub.mode or hub.verify_token.");
            }

            if (mode == "subscribe" && verifyToken == defaultVerifyToken)
            {
                return Content(challenge ?? "", "text/plain");
            }

            return Forbid("Verification token mismatch.");
        }

        [HttpPost("meta")]
        public async Task<IActionResult> ReceiveMetaMessage(
            [FromBody] JsonElement payload,
            [FromQuery] Guid? tenantId)
        {
            // Resolve Tenant ID
            Guid finalTenantId;
            if (tenantId.HasValue)
            {
                finalTenantId = tenantId.Value;
            }
            else
            {
                var firstTenant = await _context.Tenants.FirstOrDefaultAsync();
                if (firstTenant == null)
                {
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

            try
            {
                // Validate if it is a WhatsApp entry
                if (payload.TryGetProperty("object", out var objProp) && objProp.GetString() == "whatsapp_business_account")
                {
                    if (payload.TryGetProperty("entry", out var entryProp) && entryProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in entryProp.EnumerateArray())
                        {
                            if (entry.TryGetProperty("changes", out var changesProp) && changesProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var change in changesProp.EnumerateArray())
                                {
                                    if (change.TryGetProperty("value", out var valueProp))
                                    {
                                        // We only care about changes representing messages
                                        if (valueProp.TryGetProperty("messages", out var messagesProp) && messagesProp.ValueKind == JsonValueKind.Array)
                                        {
                                            var contactsList = new List<JsonElement>();
                                            if (valueProp.TryGetProperty("contacts", out var cProp) && cProp.ValueKind == JsonValueKind.Array)
                                            {
                                                foreach (var contactObj in cProp.EnumerateArray())
                                                {
                                                    contactsList.Add(contactObj);
                                                }
                                            }

                                            foreach (var msg in messagesProp.EnumerateArray())
                                            {
                                                // Extract required parameters
                                                string? from = msg.TryGetProperty("from", out var fromEl) ? fromEl.GetString() : null;
                                                string? messageId = msg.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                                                
                                                string? body = null;
                                                if (msg.TryGetProperty("text", out var textEl) && textEl.TryGetProperty("body", out var bodyEl))
                                                {
                                                    body = bodyEl.GetString();
                                                }
                                                else if (msg.TryGetProperty("type", out var typeEl))
                                                {
                                                    string type = typeEl.GetString() ?? "unknown";
                                                    body = $"[Received {type} message]";
                                                }

                                                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(messageId))
                                                {
                                                    continue;
                                                }

                                                if (string.IsNullOrEmpty(body))
                                                {
                                                    body = "[Empty message]";
                                                }

                                                // Extract contact profile name
                                                string? profileName = null;
                                                foreach (var contactObj in contactsList)
                                                {
                                                    if (contactObj.TryGetProperty("wa_id", out var waIdEl) && waIdEl.GetString() == from)
                                                    {
                                                        if (contactObj.TryGetProperty("profile", out var profileEl) && profileEl.TryGetProperty("name", out var nameEl))
                                                        {
                                                            profileName = nameEl.GetString();
                                                            break;
                                                        }
                                                    }
                                                }

                                                // Standardize phone number
                                                var phoneNumber = from.Trim();
                                                var customerName = string.IsNullOrEmpty(profileName) ? $"WhatsApp Contact ({phoneNumber})" : profileName;

                                                await _logger.LogInfoAsync($"Received Meta WhatsApp message: From={phoneNumber}, MsgId={messageId}", "WebhookController.meta", finalTenantId);

                                                // Find or Create Contact
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

                                                // Find or Create Lead (Active leads: Status NOT in Won or Lost)
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

                                                // Create & Save Message
                                                var message = new Message
                                                {
                                                    LeadId = lead.Id,
                                                    Content = body,
                                                    Direction = "Incoming",
                                                    ProviderMessageId = messageId,
                                                    Timestamp = DateTime.UtcNow,
                                                    TenantId = finalTenantId
                                                };
                                                _context.Messages.Add(message);
                                                await _context.SaveChangesAsync();

                                                // SignalR WebSockets Real-Time Broadcast
                                                var payloadDto = new
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
                                                await _hubContext.Clients.Group($"Tenant_{finalTenantId}").SendAsync("ReceiveMessage", payloadDto);

                                                // Broadcast to the specific active conversation stream
                                                await _hubContext.Clients.Group($"Lead_{lead.Id}").SendAsync("ReceiveMessage", payloadDto);

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
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"Error parsing Meta webhook payload: {ex.Message}", ex, "WebhookController.meta", finalTenantId);
                return StatusCode(500, "Error parsing payload.");
            }

            return Ok(new { success = true });
        }
    }
}
