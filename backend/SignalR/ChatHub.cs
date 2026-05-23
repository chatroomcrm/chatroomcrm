using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace ChatFlowCrm.SignalR
{
    public class ChatHub : Hub
    {
        // Join a group dedicated to a particular tenant (for real-time dashboard events)
        public async Task JoinTenantGroup(string tenantId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Tenant_{tenantId}");
        }

        // Join a specific active lead chat thread
        public async Task JoinLeadChat(string leadId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Lead_{leadId}");
        }

        // Broadcast Typing Indicator to other members of the group
        public async Task SendTypingIndicator(string leadId, string agentName, bool isTyping)
        {
            await Clients.OthersInGroup($"Lead_{leadId}").SendAsync("UserTyping", leadId, agentName, isTyping);
        }

        // Leave a lead chat group
        public async Task LeaveLeadChat(string leadId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Lead_{leadId}");
        }
    }
}
