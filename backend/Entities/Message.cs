using System;

namespace ChatFlowCrm.Entities
{
    public class Message
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid LeadId { get; set; }
        public Lead? Lead { get; set; }
        
        public string Content { get; set; } = string.Empty;
        public string Direction { get; set; } = "Incoming"; // Incoming, Outgoing
        public string ProviderMessageId { get; set; } = string.Empty; // Twilio Message SID
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public Guid TenantId { get; set; }
    }
}
