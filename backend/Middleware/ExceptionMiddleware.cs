using System;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;

namespace ChatFlowCrm.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;

        public ExceptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var logId = Guid.NewGuid();
            var timestamp = DateTimeOffset.UtcNow;
            
            // Extract request routing source details for debugging context
            var requestPath = context.Request.Path;
            var requestMethod = context.Request.Method;
            var source = $"{requestMethod} {requestPath}";

            // Attempt to resolve the TenantId from JWT claims or request headers if available
            Guid? tenantId = null;
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var tenantClaim = context.User.FindFirst("TenantId")?.Value 
                    ?? context.User.FindFirst(ClaimTypes.GroupSid)?.Value; // fallback
                if (Guid.TryParse(tenantClaim, out var parsedTenantId))
                {
                    tenantId = parsedTenantId;
                }
            }
            else if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var headerTenantId))
            {
                if (Guid.TryParse(headerTenantId, out var parsedTenantId))
                {
                    tenantId = parsedTenantId;
                }
            }

            try
            {
                // Unhandled exceptions are logged to the database
                // Since this middleware is a singleton, resolve AppDbContext dynamically from request services
                using var scope = context.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var logEntry = new LogEntry
                {
                    Id = logId,
                    Timestamp = timestamp,
                    LogLevel = "Exception",
                    Message = exception.Message,
                    Exception = exception.ToString(), // includes full stack trace and inner exceptions
                    Source = source,
                    TenantId = tenantId
                };

                dbContext.LogEntries.Add(logEntry);
                await dbContext.SaveChangesAsync();

                // Also output to console
                Console.WriteLine($"[UNHANDLED EXCEPTION LOGGER - ID: {logId}] Error: {exception.Message}");
            }
            catch (Exception dbEx)
            {
                // Fail-safe fallback if the database server is down or logging itself throws an error
                Console.WriteLine($"[FATAL DATABASE LOGGER FAILIURE] Exception could not be saved to DB: {dbEx.Message}");
                Console.WriteLine($"[ORIGINAL UNHANDLED EXCEPTION] ID: {logId}. Message: {exception.Message}. Stack: {exception.StackTrace}");
            }

            // Return clean JSON response to client instead of raw developer trace logs
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var errorResponse = new
            {
                StatusCode = context.Response.StatusCode,
                Error = "Internal Server Error",
                Message = "An unexpected system exception occurred. The details have been securely logged.",
                LogId = logId,
                Timestamp = timestamp
            };

            var jsonResult = JsonSerializer.Serialize(errorResponse);
            await context.Response.WriteAsync(jsonResult);
        }
    }
}
