using System;
using System.Collections.Generic;

namespace ChatFlowCrm.Entities
{
    public class Lead
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ContactId { get; set; }
        public Contact? Contact { get; set; }
        
        public string Status { get; set; } = "New"; // New, Contacted, Qualified, Proposal, Won, Lost
        public Guid? AssignedTo { get; set; }
        
        public Guid TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}
