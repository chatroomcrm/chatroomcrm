using System;

namespace ChatFlowCrm.Entities
{
    public class TaskItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid LeadId { get; set; }
        public Lead? Lead { get; set; }
        
        public DateTime DueDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Completed
        public string Notes { get; set; } = string.Empty;
        
        public Guid TenantId { get; set; }
    }
}
