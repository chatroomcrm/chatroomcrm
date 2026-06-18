using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChatFlowCrm.Data;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace ChatFlowCrm.Services
{
    public interface ITwilioWhatsAppService
    {
        Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, Guid? tenantId);
        Task<bool> SendWhatsAppTemplateAsync(string toPhone, string templateName, string language, List<string> parameters, string resolvedBody, Guid? tenantId);
    }

    public class TwilioWhatsAppService : ITwilioWhatsAppService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TwilioWhatsAppService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IDbLoggerService _dbLogger;
        private readonly AppDbContext _context;

        public TwilioWhatsAppService(HttpClient httpClient, ILogger<TwilioWhatsAppService> logger, IConfiguration configuration, IDbLoggerService dbLogger, AppDbContext context)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _dbLogger = dbLogger;
            _context = context;
        }

        public async Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, Guid? tenantId)
        {
            try
            {
                var accountSid = _configuration["Twilio:AccountSid"];
                var authToken = _configuration["Twilio:AuthToken"];
                var fromWhatsAppNumber = _configuration["Twilio:FromWhatsAppNumber"] ?? "whatsapp:+14155238886";

                // Overwrite dynamically from Tenant database configurations if present
                if (tenantId.HasValue)
                {
                    var tenant = await _context.Tenants.FindAsync(tenantId.Value);
                    if (tenant != null && tenant.ServiceType == "Twilio")
                    {
                        if (!string.IsNullOrEmpty(tenant.ProviderAccountId))
                            accountSid = tenant.ProviderAccountId;
                        if (!string.IsNullOrEmpty(tenant.ProviderApiKey))
                            authToken = tenant.ProviderApiKey;
                        if (!string.IsNullOrEmpty(tenant.WhatsAppNumber))
                            fromWhatsAppNumber = tenant.WhatsAppNumber.StartsWith("whatsapp:") ? tenant.WhatsAppNumber : $"whatsapp:{tenant.WhatsAppNumber}";
                    }
                }

                if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken))
                {
                    _logger.LogWarning("Twilio outbound configurations are missing. Simulating sandbox message successfully.");
                    return true; // Simulate successfully for sandbox developers
                }

                // Standardize recipient phone format with dynamic country-code-aware formatter
                var defaultCountryCode = _configuration["Messaging:DefaultCountryCode"] ?? "+91";
                string formattedToPhone = PhoneFormatter.Format(toPhone, defaultCountryCode);

                _logger.LogInformation("Using official Twilio C# SDK to send WhatsApp message to {Phone}", formattedToPhone);
                
                // Initialize TwilioClient with the resolved credentials
                TwilioClient.Init(accountSid, authToken);

                var message = await MessageResource.CreateAsync(
                    to: new PhoneNumber($"whatsapp:{formattedToPhone}"),
                    from: new PhoneNumber(fromWhatsAppNumber),
                    body: content
                );

                if (message != null && !string.IsNullOrEmpty(message.Sid))
                {
                    _logger.LogInformation("Successfully sent WhatsApp message via Twilio SDK to {Phone}. Message SID: {Sid}", toPhone, message.Sid);
                    return true;
                }

                return false;
            }
            catch (Twilio.Exceptions.ApiException apiEx)
            {
                _logger.LogError(apiEx, "Twilio API exception in TwilioWhatsAppService sending to {Phone}. Code: {Code}, Details: {Details}", toPhone, apiEx.Code, apiEx.Message);
                try
                {
                    await _dbLogger.LogErrorAsync(
                        $"Twilio API Error sending outbound WhatsApp message to {toPhone} (Code {apiEx.Code}): {apiEx.Message}", 
                        apiEx, 
                        "TwilioWhatsAppService.SendWhatsAppMessage", 
                        tenantId
                    );
                }
                catch {}
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in TwilioWhatsAppService sending to {Phone}", toPhone);
                try
                {
                    await _dbLogger.LogErrorAsync($"Fatal exception in TwilioWhatsAppService outbound dispatcher to {toPhone}: {ex.Message}", ex, "TwilioWhatsAppService.SendWhatsAppMessage", tenantId);
                }
                catch {}
                return false;
            }
        }

        public async Task<bool> SendWhatsAppTemplateAsync(string toPhone, string templateName, string language, List<string> parameters, string resolvedBody, Guid? tenantId)
        {
            _logger.LogInformation("Sending template '{Template}' via Twilio using resolved body matching.", templateName);
            return await SendWhatsAppMessageAsync(toPhone, resolvedBody, tenantId);
        }
    }
}
