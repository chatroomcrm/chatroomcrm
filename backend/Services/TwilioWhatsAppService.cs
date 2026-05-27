using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChatFlowCrm.Data;

namespace ChatFlowCrm.Services
{
    public interface ITwilioWhatsAppService
    {
        Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, Guid? tenantId);
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

                _logger.LogInformation("Using Twilio Messaging API to send WhatsApp message to {Phone}", formattedToPhone);
                var requestUrlTwilio = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
                var requestTwilio = new HttpRequestMessage(HttpMethod.Post, requestUrlTwilio);
                
                var authBytes = Encoding.UTF8.GetBytes($"{accountSid}:{authToken}");
                requestTwilio.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                var postData = new Dictionary<string, string>
                {
                    { "To", $"whatsapp:{formattedToPhone}" },
                    { "From", fromWhatsAppNumber },
                    { "Body", content }
                };

                requestTwilio.Content = new FormUrlEncodedContent(postData);

                var responseTwilio = await _httpClient.SendAsync(requestTwilio);
                if (responseTwilio.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully sent WhatsApp message via Twilio to {Phone}", toPhone);
                    return true;
                }

                var errorContent = await responseTwilio.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send Twilio WhatsApp message. Status: {Status}, Details: {Details}", responseTwilio.StatusCode, errorContent);
                
                try
                {
                    var apiException = new HttpRequestException($"Twilio Outbound API returned HTTP {(int)responseTwilio.StatusCode} ({responseTwilio.StatusCode}). Response Body: {errorContent}");
                    await _dbLogger.LogErrorAsync(
                        $"Failed to send Twilio outbound WhatsApp message to {toPhone} (HTTP {responseTwilio.StatusCode}). Failed API response recorded.", 
                        apiException, 
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
    }
}
