using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ChatFlowCrm.Data;
using ChatFlowCrm.Entities;
using ChatFlowCrm.Services;
using ChatFlowCrm.SignalR;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Configuration (SQL Server)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=chatflow_db;Trusted_Connection=True;TrustServerCertificate=True;";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// 2. Dependency Injection
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>();
builder.Services.AddSingleton<IDbLoggerService, DbLoggerService>();

// 3. Register Controllers and Hubs
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Swagger API Generation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ChatFlow WhatsApp CRM API",
        Version = "v1",
        Description = "Multi-Tenant WhatsApp CRM SaaS Platform API with 3-tier Role-Based Access Control."
    });

    // Configure Swagger to use JWT Bearer Authentication
    var securityScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "JWT Authentication",
        Description = "Enter JWT Bearer token only (do NOT include the 'Bearer ' prefix, as it will be prefixed automatically or enter 'Bearer <token>' manually)",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Id = "Bearer",
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme
        }
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

// 4. JWT Authentication Setup
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SUPER_SECRET_CHATFLOW_SECURITY_KEY_2026";
var key = Encoding.ASCII.GetBytes(jwtKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ChatFlowCrm",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ChatFlowClients",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    // Supporting JWTs inside SignalR Websocket connections
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chathub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// 5. CORS policy configurations
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true) // Crucial for SignalR local dev
              .AllowCredentials();
    });
});

var app = builder.Build();

// Enable Swagger OpenAPI UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ChatFlow CRM API v1");
    c.RoutePrefix = "swagger"; // accessible at http://localhost:5000/swagger
});

// Enable Database Logging exception handling middleware first to capture any exception downstream
app.UseMiddleware<ChatFlowCrm.Middleware.ExceptionMiddleware>();

app.UseCors("AllowAll");

// Enable static file serving from wwwroot to host the frontend seamlessly
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chathub");

// Simple API status check with automatic static index.html fallback
app.MapGet("/", async (HttpContext context) =>
{
    var indexFile = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
    if (File.Exists(indexFile))
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexFile);
    }
    else
    {
        await context.Response.WriteAsJsonAsync(new { name = "ChatFlow WhatsApp CRM API", status = "Healthy", version = "1.0.0" });
    }
});

// Automatically seed a default Tenant and User on startup if empty (for direct convenience!)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        // EF Core database migration or creation
        context.Database.EnsureCreated();

        // Self-healing: Ensure Email column exists in Contacts table for reporting
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Contacts') AND name = 'Email') BEGIN ALTER TABLE Contacts ADD Email NVARCHAR(MAX) NOT NULL DEFAULT ''; END");

        // Self-healing: Ensure Timestamp column exists in Leads table for reporting
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Leads') AND name = 'Timestamp') BEGIN ALTER TABLE Leads ADD Timestamp DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(); END");

        // Self-healing: Ensure WhatsApp columns exist in Tenants table for true multi-tenancy
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'WhatsAppNumber') BEGIN ALTER TABLE Tenants ADD WhatsAppNumber NVARCHAR(MAX) NOT NULL DEFAULT ''; END");
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'MetaAccessToken') BEGIN ALTER TABLE Tenants ADD MetaAccessToken NVARCHAR(MAX) NOT NULL DEFAULT ''; END");
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'MetaPhoneNumberId') BEGIN ALTER TABLE Tenants ADD MetaPhoneNumberId NVARCHAR(MAX) NOT NULL DEFAULT ''; END");

        // Self-healing: Seed active production credentials for Durga Enterprises if empty (Tenant GUID: 73349044-4ef4-405d-bd9f-6e6e06344860)
        context.Database.ExecuteSqlRaw(@"
            UPDATE Tenants 
            SET WhatsAppNumber = '8143712528', 
                MetaAccessToken = 'EAAOAfLtTZCVQBRoVxFN0yaAxuoNXenRPecUXHG3DN5tFH61sZB1wnTIpGDmhS1D61ZCUPkFXFcWKNZB4emxu48vfI9ZCJ8b7nj48RZBZCwl0UkldAlVU81E4sZC8ZB3qUIm6PDmWKOZAzUYUGeNCr0iHqEumB4ZBTqgWMzfJpZA5bhei89YfPIL7eBTvTnSiUQOeOgDm5nURrKnjOw1BUX1xVV7yWV1aoJ6WWcN1POeekXIMqr08aRYB2QMGRt7tRCnfB5rxODI3zLiQguxn3uL3CgaAQgZDZD', 
                MetaPhoneNumberId = '1184346914753507' 
            WHERE Id = '73349044-4ef4-405d-bd9f-6e6e06344860' 
              AND (MetaAccessToken = '' OR MetaAccessToken IS NULL)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error seeding DB: {ex.Message}");
    }
}

app.Run();
