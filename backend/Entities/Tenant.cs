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

        // Unified Dynamic Messaging Architecture
        public string? ServiceType { get; set; } // "Twilio", "Meta", or NULL
        public string WhatsAppNumber { get; set; } = string.Empty; // Shared display number (e.g. "+918143712528")
        
        public string? ProviderAccountId { get; set; } // Twilio AccountSid OR Meta WABA ID
        public string? ProviderApiKey { get; set; }    // Twilio AuthToken OR Meta AccessToken
        public string? ProviderSenderId { get; set; }  // Meta PhoneNumberId (NULL for Twilio)

        // Navigation properties
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
        public ICollection<Lead> Leads { get; set; } = new List<Lead>();
    }
}
