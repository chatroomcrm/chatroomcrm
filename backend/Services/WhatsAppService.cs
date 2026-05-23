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
                var accountSid = _configuration["Twilio:AccountSid"];
                var authToken = _configuration["Twilio:AuthToken"];
                var fromWhatsAppNumber = _configuration["Twilio:FromWhatsAppNumber"] ?? "whatsapp:+14155238886"; // Twilio Sandbox Number

                if (string.IsNullOrEmpty(accountSid) || string.IsNullOrEmpty(authToken))
                {
                    _logger.LogWarning("Twilio configurations are missing. Simulating outbound WhatsApp message successfully.");
                    return true; // Simulate in development
                }

                var requestUrl = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";

                var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                
                // Format basic authentication credentials
                var authBytes = Encoding.UTF8.GetBytes($"{accountSid}:{authToken}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                // Twilio Form UrlEncoded parameters
                var postData = new Dictionary<string, string>
                {
                    { "To", $"whatsapp:{toPhone}" },
                    { "From", fromWhatsAppNumber },
                    { "Body", content }
                };

                request.Content = new FormUrlEncodedContent(postData);

                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully sent WhatsApp message to {Phone}", toPhone);
                    return true;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send Twilio WhatsApp message. Status: {Status}, Details: {Details}", response.StatusCode, errorContent);
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
