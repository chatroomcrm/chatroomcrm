using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChatFlowCrm.Data;

namespace ChatFlowCrm.Services
{
    public class WhatsAppService : IWhatsAppService
    {
        private readonly IMetaWhatsAppService _metaService;
        private readonly ITwilioWhatsAppService _twilioService;
        private readonly ILogger<WhatsAppService> _logger;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;

        public WhatsAppService(
            IMetaWhatsAppService metaService, 
            ITwilioWhatsAppService twilioService, 
            ILogger<WhatsAppService> logger, 
            IConfiguration configuration,
            AppDbContext context)
        {
            _metaService = metaService;
            _twilioService = twilioService;
            _logger = logger;
            _configuration = configuration;
            _context = context;
        }

        public async Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, string providerConfig)
        {
            Guid? resolvedTenantId = null;
            if (Guid.TryParse(providerConfig, out var tenantId))
            {
                resolvedTenantId = tenantId;
            }

            try
            {
                string preferredProvider = _configuration["Messaging:PreferredProvider"] ?? "Meta";

                // Dynamically resolve provider from database Tenant configuration
                if (resolvedTenantId.HasValue)
                {
                    var tenant = await _context.Tenants.FindAsync(resolvedTenantId.Value);
                    if (tenant != null && !string.IsNullOrEmpty(tenant.MessagingProvider))
                    {
                        preferredProvider = tenant.MessagingProvider;
                        _logger.LogInformation("Orchestrator resolved active Tenant messaging provider from DB: {Provider}", preferredProvider);
                    }
                }

                bool useTwilio = preferredProvider.Equals("Twilio", StringComparison.OrdinalIgnoreCase);

                if (useTwilio)
                {
                    _logger.LogInformation("Orchestrating outbound WhatsApp message: Dispatching to Twilio first...");
                    bool twilioSuccess = await _twilioService.SendWhatsAppMessageAsync(toPhone, content, resolvedTenantId);
                    if (twilioSuccess) return true;

                    _logger.LogWarning("Twilio provider did not complete successfully. Falling back to Meta Cloud API...");
                    return await _metaService.SendWhatsAppMessageAsync(toPhone, content, resolvedTenantId);
                }
                else
                {
                    _logger.LogInformation("Orchestrating outbound WhatsApp message: Dispatching to Meta first...");
                    bool metaSuccess = await _metaService.SendWhatsAppMessageAsync(toPhone, content, resolvedTenantId);
                    if (metaSuccess) return true;

                    _logger.LogWarning("Meta provider did not complete successfully. Falling back to Twilio API...");
                    return await _twilioService.SendWhatsAppMessageAsync(toPhone, content, resolvedTenantId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception inside WhatsAppService orchestrator flow for {Phone}", toPhone);
                return false;
            }
        }
    }
}
