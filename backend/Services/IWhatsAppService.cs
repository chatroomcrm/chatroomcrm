using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChatFlowCrm.Services
{
    public interface IWhatsAppService
    {
        Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, string providerConfig);
        Task<bool> SendWhatsAppTemplateAsync(string toPhone, string templateName, string language, List<string> parameters, string resolvedBody, string providerConfig);
    }
}
