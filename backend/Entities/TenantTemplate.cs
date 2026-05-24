using System;

namespace ChatFlowCrm.Entities
{
    public class TenantTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public Tenant? Tenant { get; set; }
        
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "UTILITY"; // UTILITY, MARKETING, AUTHENTICATION
        public string Language { get; set; } = "en_US";
        public string Body { get; set; } = string.Empty;
        public string Status { get; set; } = "Approved"; // Approved, Pending, Rejected
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }
}
