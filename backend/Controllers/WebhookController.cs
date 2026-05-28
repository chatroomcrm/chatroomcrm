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
            var form = Request.Form;
            Guid finalTenantId = Guid.Empty;

            await _logger.LogInfoAsync($"[Twilio Webhook Received] ContentType={Request.ContentType}, Payload={form}", "WebhookController.Receive", finalTenantId == Guid.Empty ? null : finalTenantId);
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

                // 1.5. Log the raw incoming request body or form values securely into LogEntries
                string rawPayload = string.Empty;
                try
                {
                    if (Request.HasFormContentType)
                    {
                        var formPairs = Request.Form.Select(k => $"{k.Key}={k.Value}");
                        rawPayload = "Form Data: " + string.Join("&", formPairs);
                    }
                    else
                    {
                        Request.EnableBuffering();
                        using (var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true))
                        {
                            rawPayload = "Raw Body: " + await reader.ReadToEndAsync();
                            Request.Body.Position = 0;
                        }
                    }
                    await _logger.LogInfoAsync($"[Twilio Webhook Received] ContentType={Request.ContentType}, Payload={rawPayload}", "WebhookController.Receive", finalTenantId == Guid.Empty ? null : finalTenantId);
                }
                catch (Exception logEx)
                {
                    await _logger.LogWarningAsync($"Failed to log raw webhook payload: {logEx.Message}", "WebhookController.Receive", finalTenantId == Guid.Empty ? null : finalTenantId);
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

                return Content("<Response></Response>", "text/xml");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync($"Fatal error in Receive webhook: {ex.Message}", ex, "WebhookController.Receive", finalTenantId == Guid.Empty ? null : finalTenantId);
                return StatusCode(500, "Error processing Twilio webhook.");
            }
        }

        [HttpPost("twilio-errors")]
        public async Task<IActionResult> ReceiveTwilioError()
        {
            string accountSid = "";
            string debuggerSid = "";
            string timestamp = "";
            string level = "Error";
            string rawPayload = "";

            try
            {
                if (Request.HasFormContentType)
                {
                    var form = Request.Form;
                    accountSid = form["AccountSid"].ToString();
                    debuggerSid = form["Sid"].ToString();
                    timestamp = form["Timestamp"].ToString();
                    level = form["Level"].ToString();
                    rawPayload = form["Payload"].ToString();
                }
                else
                {
                    // Parse raw JSON body if posted as application/json
                    Request.EnableBuffering();
                    using (var reader = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 1024, leaveOpen: true))
                    {
                        var bodyStr = await reader.ReadToEndAsync();
                        using (var jsonDoc = JsonDocument.Parse(bodyStr))
                        {
                            var root = jsonDoc.RootElement;
                            if (root.TryGetProperty("AccountSid", out var accEl)) accountSid = accEl.GetString() ?? "";
                            if (root.TryGetProperty("Sid", out var sidEl)) debuggerSid = sidEl.GetString() ?? "";
                            if (root.TryGetProperty("Timestamp", out var timeEl)) timestamp = timeEl.GetString() ?? "";
                            if (root.TryGetProperty("Level", out var levelEl)) level = levelEl.GetString() ?? "Error";
                            if (root.TryGetProperty("Payload", out var payEl)) rawPayload = payEl.ToString();
                        }
                    }
                }

                // Format log details
                string logMessage = $"[Twilio Debugger {level}] EventSid: {debuggerSid}, AccountSid: {accountSid}, Timestamp: {timestamp}, Details: {rawPayload}";

                if (level.Equals("Error", StringComparison.OrdinalIgnoreCase))
                {
                    await _logger.LogErrorAsync(
                        logMessage, 
                        new Exception($"Twilio Debugger Event. Details: {rawPayload}"), 
                        "TwilioDebugger.Webhook"
                    );
                }
                else
                {
                    await _logger.LogWarningAsync(logMessage, "TwilioDebugger.Webhook");
                }

                return Ok();
            }
            catch (Exception ex)
            {
                try
                {
                    await _logger.LogErrorAsync($"Fatal error in twilio-errors webhook: {ex.Message}", ex, "TwilioDebugger.Webhook");
                }
                catch {}
                
                // Return HTTP 200 OK so Twilio doesn't continuously retry failing error logging requests
                return Ok();
            }
        }
    }
}
