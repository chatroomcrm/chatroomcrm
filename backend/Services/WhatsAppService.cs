using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChatFlowCrm.Services
{
    public class WhatsAppService : IWhatsAppService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<WhatsAppService> _logger;
        private readonly IConfiguration _configuration;

        public WhatsAppService(HttpClient httpClient, ILogger<WhatsAppService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, string providerConfig)
        {
            try
            {
                // 1. Try Native Meta Cloud API Outbound Messaging first
                var metaToken = _configuration["Meta:AccessToken"];
                var metaPhoneId = _configuration["Meta:PhoneNumberId"];

                if (!string.IsNullOrEmpty(metaToken) && !string.IsNullOrEmpty(metaPhoneId) && metaPhoneId != "PLACEHOLDER_PHONE_NUMBER_ID")
                {
                    _logger.LogInformation("Using Native Meta Cloud API to send WhatsApp message to {Phone}", toPhone);
                    
                    var requestUrl = $"https://graph.facebook.com/v18.0/{metaPhoneId}/messages";
                    var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", metaToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var payload = new
                    {
                        messaging_product = "whatsapp",
                        to = toPhone.Replace("+", "").Trim(), // Meta expects digits only, no + prefix
                        type = "text",
                        text = new { body = content }
                    };

                    var jsonString = System.Text.Json.JsonSerializer.Serialize(payload);
                    request.Content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Successfully sent WhatsApp message via Meta to {Phone}", toPhone);
                        return true;
                    }

                    var errContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to send Meta WhatsApp message. Status: {Status}, Details: {Details}", response.StatusCode, errContent);
                    return false;
                }

                // 2. Fallback to Twilio Outbound Messaging
                var accountSid = _configuration["Twilio:AccountSid"];
                var authToken = _configuration["Twilio:AuthToken"];
                var fromWhatsAppNumber = _configuration["Twilio:FromWhatsAppNumber"] ?? "whatsapp:+14155238886"; // Twilio Sandbox Number

                if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken))
                {
                    _logger.LogWarning("Outbound messaging configurations (Meta or Twilio) are missing. Simulating outbound WhatsApp message successfully.");
                    return true; // Simulate in development
                }

                var requestUrlTwilio = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
                var requestTwilio = new HttpRequestMessage(HttpMethod.Post, requestUrlTwilio);
                
                var authBytes = Encoding.UTF8.GetBytes($"{accountSid}:{authToken}");
                requestTwilio.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                var postData = new Dictionary<string, string>
                {
                    { "To", $"whatsapp:{toPhone}" },
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
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while sending outbound WhatsApp message to {Phone}", toPhone);
                return false;
            }
        }
    }
}
