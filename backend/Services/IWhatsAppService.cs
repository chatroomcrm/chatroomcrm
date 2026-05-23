using System.Threading.Tasks;

namespace ChatFlowCrm.Services
{
    public interface IWhatsAppService
    {
        Task<bool> SendWhatsAppMessageAsync(string toPhone, string content, string providerConfig);
    }
}
