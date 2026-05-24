using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;

namespace ChatFlowCrm.Services
{
    public interface IDbLoggerService
    {
        Task LogInfoAsync(string message, string? source = null, Guid? tenantId = null);
        Task LogWarningAsync(string message, string? source = null, Guid? tenantId = null);
        Task LogErrorAsync(string message, Exception? ex = null, string? source = null, Guid? tenantId = null);
    }

    public class DbLoggerService : IDbLoggerService
    {
        private readonly IServiceProvider _serviceProvider;

        public DbLoggerService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        private async Task SaveLogAsync(string logLevel, string message, Exception? ex, string? source, Guid? tenantId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var entry = new LogEntry
                {
                    LogLevel = logLevel,
                    Message = message,
                    Exception = ex?.ToString(),
                    Source = source,
                    TenantId = tenantId
                };

                dbContext.LogEntries.Add(entry);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                // Fallback to standard console logger if database logging fails to prevent endless loops
                Console.WriteLine($"[FATAL DATABASE LOGGER ERROR] Could not save log entry to database: {dbEx.Message}");
                Console.WriteLine($"[ORIGINAL LOG - {logLevel}] Message: {message}. Source: {source}. Ex: {ex}");
            }
        }

        public Task LogInfoAsync(string message, string? source = null, Guid? tenantId = null)
        {
            Console.WriteLine($"[INFO] {source ?? "System"}: {message}");
            return SaveLogAsync("Info", message, null, source, tenantId);
        }

        public Task LogWarningAsync(string message, string? source = null, Guid? tenantId = null)
            => SaveLogAsync("Warning", message, null, source, tenantId);

        public Task LogErrorAsync(string message, Exception? ex = null, string? source = null, Guid? tenantId = null)
            => SaveLogAsync("Error", message, ex, source, tenantId);
    }
}
