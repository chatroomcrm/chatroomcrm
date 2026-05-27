using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChatFlowCrm.Data;

namespace ChatFlowCrm.Services
{
    public interface IMetaWhatsAppService
    {
        Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, Guid? tenantId);
    }

    public class MetaWhatsAppService : IMetaWhatsAppService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MetaWhatsAppService> _logger;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;
        private readonly IDbLoggerService _dbLogger;

        public MetaWhatsAppService(HttpClient httpClient, ILogger<MetaWhatsAppService> logger, IConfiguration configuration, AppDbContext context, IDbLoggerService dbLogger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _context = context;
            _dbLogger = dbLogger;
        }

        public async Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, Guid? tenantId)
        {
            try
            {
                string? metaToken = _configuration["Meta:AccessToken"];
                string? metaPhoneId = null;

                if (tenantId.HasValue)
                {
                    var tenant = await _context.Tenants.FindAsync(tenantId.Value);
                    if (tenant != null && !string.IsNullOrEmpty(tenant.MetaPhoneNumberId))
                    {
                        metaPhoneId = tenant.MetaPhoneNumberId;
                        _logger.LogInformation("Loaded dynamic Tenant phone ID from DB for outbound Meta Cloud API. TenantId: {TenantId}", tenantId.Value);
                    }
                }

                if (string.IsNullOrEmpty(metaPhoneId))
                {
                    metaPhoneId = _configuration["Meta:PhoneNumberId"];
                }

                if (string.IsNullOrEmpty(metaToken) || string.IsNullOrEmpty(metaPhoneId) || metaPhoneId == "PLACEHOLDER_PHONE_NUMBER_ID")
                {
                    _logger.LogWarning("Meta Cloud API configurations are missing. Skipping Meta sender.");
                    return false;
                }

                // Standardize phone number using the dynamic country-code-aware formatter
                var defaultCountryCode = _configuration["Messaging:DefaultCountryCode"] ?? "+91";
                var formattedToPhone = PhoneFormatter.Format(toPhone, defaultCountryCode);

                _logger.LogInformation("Using Native Meta Cloud API to send WhatsApp message to {Phone}", formattedToPhone);
                var requestUrl = $"https://graph.facebook.com/v18.0/{metaPhoneId}/messages";
                var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", metaToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = formattedToPhone.Replace("+", "").Trim(),
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
                
                try
                {
                    var apiException = new HttpRequestException($"Meta Cloud API returned HTTP {(int)response.StatusCode} ({response.StatusCode}). Response Body: {errContent}");
                    await _dbLogger.LogErrorAsync(
                        $"Failed to send Meta outbound WhatsApp message to {toPhone} (HTTP {response.StatusCode}). Failed API response recorded.", 
                        apiException, 
                        "MetaWhatsAppService.SendWhatsAppMessage", 
                        tenantId
                    );
                }
                catch {}
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in MetaWhatsAppService sending to {Phone}", toPhone);
                try
                {
                    await _dbLogger.LogErrorAsync($"Fatal exception in MetaWhatsAppService outbound dispatcher to {toPhone}: {ex.Message}", ex, "MetaWhatsAppService.SendWhatsAppMessage", tenantId);
                }
                catch {}
                return false;
            }
        }
    }
}
