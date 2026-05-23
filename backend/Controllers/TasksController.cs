using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;

namespace ChatFlowCrm.Controllers
{
    [ApiController]
    [Route("api/tasks")]
    public class TasksController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TasksController(AppDbContext context)
        {
            _context = context;
        }

        private Guid ResolveTenantId()
        {
            var claim = User.FindFirst("TenantId")?.Value;
            if (Guid.TryParse(claim, out var tenantId))
            {
                return tenantId;
            }
            if (Request.Headers.TryGetValue("X-Tenant-Id", out var headerTenant) && 
                Guid.TryParse(headerTenant.ToString(), out var headerTenantId))
            {
                return headerTenantId;
            }
            var fallbackTenant = _context.Tenants.FirstOrDefault();
            return fallbackTenant?.Id ?? Guid.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> GetTasks([FromQuery] string? status)
        {
            var tenantId = ResolveTenantId();

            var query = _context.Tasks
                .Include(t => t.Lead)
                .ThenInclude(l => l!.Contact)
                .Where(t => t.TenantId == tenantId);

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(t => t.Status == status);
            }

            var tasksList = await query
                .OrderBy(t => t.DueDate)
                .Select(t => new
                {
                    t.Id,
                    t.LeadId,
                    contactName = t.Lead != null && t.Lead.Contact != null ? t.Lead.Contact.Name : "N/A",
                    t.DueDate,
                    t.Status,
                    t.Notes
                })
                .ToListAsync();

            return Ok(tasksList);
        }

        public class CreateTaskRequest
        {
            public Guid LeadId { get; set; }
            public DateTime DueDate { get; set; }
            public string Notes { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
        {
            var tenantId = ResolveTenantId();
            var leadExists = await _context.Leads.AnyAsync(l => l.Id == request.LeadId && l.TenantId == tenantId);
            if (!leadExists)
            {
                return BadRequest("Lead not found or inaccessible.");
            }

            var taskItem = new TaskItem
            {
                LeadId = request.LeadId,
                DueDate = request.DueDate,
                Notes = request.Notes,
                Status = "Pending",
                TenantId = tenantId
            };

            _context.Tasks.Add(taskItem);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, taskId = taskItem.Id });
        }

        [HttpPut("{taskId}/status")]
        public async Task<IActionResult> UpdateTaskStatus(Guid taskId, [FromBody] string status)
        {
            var tenantId = ResolveTenantId();
            var taskItem = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.TenantId == tenantId);
            if (taskItem == null)
            {
                return NotFound("Task not found.");
            }

            taskItem.Status = status;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, taskId = taskItem.Id, status = taskItem.Status });
        }
    }
}
