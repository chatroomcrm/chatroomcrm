using System;
using System.Collections.Generic;

namespace ChatFlowCrm.Entities
{
    public class Contact
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty; // Customer Email
        
        public Guid TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public ICollection<Lead> Leads { get; set; } = new List<Lead>();
    }
}
