using System;
using System.Text.Json.Serialization;

namespace ChatFlowCrm.Entities
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        
        [JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;
        
        public bool IsBlocked { get; set; } = false;
        
        public string Role { get; set; } = UserRoles.Agent; // SuperAdmin, TenantAdmin, Agent
        
        public Guid? TenantId { get; set; }
        
        [JsonIgnore]
        public Tenant? Tenant { get; set; }
    }
}
