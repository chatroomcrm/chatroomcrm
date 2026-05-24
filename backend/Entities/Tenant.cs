using System;
using System.Collections.Generic;

namespace ChatFlowCrm.Entities
{
    public class Tenant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string ThemeColor { get; set; } = "#00f2fe|#4facfe"; // Default brand gradient
        public bool IsBlocked { get; set; } = false;

        public string WhatsAppNumber { get; set; } = string.Empty;
        public string MetaAccessToken { get; set; } = string.Empty;
        public string MetaPhoneNumberId { get; set; } = string.Empty;

        // Navigation properties
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
        public ICollection<Lead> Leads { get; set; } = new List<Lead>();
    }
}
