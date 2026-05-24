using Microsoft.EntityFrameworkCore;
using ChatFlowCrm.Entities;

namespace ChatFlowCrm.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Contact> Contacts => Set<Contact>();
        public DbSet<Lead> Leads => Set<Lead>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<TaskItem> Tasks => Set<TaskItem>();
        public DbSet<LogEntry> LogEntries => Set<LogEntry>();
        public DbSet<TenantTemplate> TenantTemplates => Set<TenantTemplate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Tenant configurations
            modelBuilder.Entity<Tenant>()
                .HasKey(t => t.Id);

            // User configurations
            modelBuilder.Entity<User>()
                .HasKey(u => u.Id);
            modelBuilder.Entity<User>()
                .HasOne(u => u.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(u => u.TenantId)
                .IsRequired(false) // TenantId is optional for global SuperAdmin
                .OnDelete(DeleteBehavior.Cascade);

            // Contact configurations
            modelBuilder.Entity<Contact>()
                .HasKey(c => c.Id);
            modelBuilder.Entity<Contact>()
                .HasOne(c => c.Tenant)
                .WithMany(t => t.Contacts)
                .HasForeignKey(c => c.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Lead configurations
            modelBuilder.Entity<Lead>()
                .HasKey(l => l.Id);
            modelBuilder.Entity<Lead>()
                .HasOne(l => l.Contact)
                .WithMany(c => c.Leads)
                .HasForeignKey(l => l.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Lead>()
                .HasOne(l => l.Tenant)
                .WithMany(t => t.Leads)
                .HasForeignKey(l => l.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Message configurations
            modelBuilder.Entity<Message>()
                .HasKey(m => m.Id);
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Lead)
                .WithMany(l => l.Messages)
                .HasForeignKey(m => m.LeadId)
                .OnDelete(DeleteBehavior.Cascade);

            // TaskItem configurations
            modelBuilder.Entity<TaskItem>()
                .HasKey(t => t.Id);
            modelBuilder.Entity<TaskItem>()
                .HasOne(t => t.Lead)
                .WithMany(l => l.Tasks)
                .HasForeignKey(t => t.LeadId)
                .OnDelete(DeleteBehavior.Cascade);

            // TenantTemplate configurations
            modelBuilder.Entity<TenantTemplate>()
                .HasKey(t => t.Id);
            modelBuilder.Entity<TenantTemplate>()
                .HasOne(t => t.Tenant)
                .WithMany()
                .HasForeignKey(t => t.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes for database performance (crucial for SQL experience!)
            modelBuilder.Entity<Contact>()
                .HasIndex(c => new { c.TenantId, c.Phone })
                .IsUnique();

            modelBuilder.Entity<Lead>()
                .HasIndex(l => new { l.TenantId, l.Status });

            modelBuilder.Entity<Message>()
                .HasIndex(m => new { m.LeadId, m.Timestamp });

            modelBuilder.Entity<LogEntry>()
                .HasKey(l => l.Id);
            modelBuilder.Entity<LogEntry>()
                .HasIndex(l => new { l.Timestamp, l.LogLevel });
        }
    }
}
