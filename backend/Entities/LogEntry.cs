using System;

namespace ChatFlowCrm.Entities
{
    public class LogEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string LogLevel { get; set; } = "Info"; // Info, Warning, Error, Exception
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public string? Source { get; set; }
        public Guid? TenantId { get; set; }
    }
}
