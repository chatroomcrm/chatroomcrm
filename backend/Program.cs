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
builder.Services.AddHttpClient<IMetaWhatsAppService, MetaWhatsAppService>();
builder.Services.AddHttpClient<ITwilioWhatsAppService, TwilioWhatsAppService>();
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
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

// Enable static file serving from wwwroot if needed, but disable serving default files
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chathub");

// Simple JSON API health check status endpoint
app.MapGet("/", async (HttpContext context) =>
{
    await context.Response.WriteAsJsonAsync(new { name = "ChatFlow WhatsApp CRM API", status = "Healthy", version = "1.0.0" });
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

        // Self-healing: Ensure Unified WhatsApp columns exist in Tenants table for true multi-tenancy
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'WhatsAppNumber') BEGIN ALTER TABLE Tenants ADD WhatsAppNumber NVARCHAR(MAX) NOT NULL DEFAULT ''; END");
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'ServiceType') BEGIN ALTER TABLE Tenants ADD ServiceType NVARCHAR(MAX) NULL; END");
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'ProviderAccountId') BEGIN ALTER TABLE Tenants ADD ProviderAccountId NVARCHAR(MAX) NULL; END");
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'ProviderApiKey') BEGIN ALTER TABLE Tenants ADD ProviderApiKey NVARCHAR(MAX) NULL; END");
        context.Database.ExecuteSqlRaw("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'ProviderSenderId') BEGIN ALTER TABLE Tenants ADD ProviderSenderId NVARCHAR(MAX) NULL; END");

        // Self-healing: Backward-compatible migration from legacy columns
        context.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'MessagingProvider')
            BEGIN
                EXEC('UPDATE Tenants SET ServiceType = MessagingProvider WHERE (ServiceType IS NULL OR ServiceType = '''') AND MessagingProvider IS NOT NULL AND MessagingProvider <> '''';');
            END");
        context.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'MetaAccessToken')
            BEGIN
                EXEC('UPDATE Tenants SET ProviderApiKey = MetaAccessToken, ServiceType = ''Meta'' WHERE (ProviderApiKey IS NULL OR ProviderApiKey = '''') AND MetaAccessToken IS NOT NULL AND MetaAccessToken <> '''';');
            END");
        context.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'MetaPhoneNumberId')
            BEGIN
                EXEC('UPDATE Tenants SET ProviderSenderId = MetaPhoneNumberId WHERE (ProviderSenderId IS NULL OR ProviderSenderId = '''') AND MetaPhoneNumberId IS NOT NULL AND MetaPhoneNumberId <> '''';');
            END");
        context.Database.ExecuteSqlRaw(@"
            IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'MetaBusinessAccountId')
            BEGIN
                EXEC('UPDATE Tenants SET ProviderAccountId = MetaBusinessAccountId WHERE (ProviderAccountId IS NULL OR ProviderAccountId = '''') AND MetaBusinessAccountId IS NOT NULL AND MetaBusinessAccountId <> '''';');
            END");

        // Self-healing: Seed active production credentials for Durga Enterprises if empty (Tenant GUID: 73349044-4ef4-405d-bd9f-6e6e06344860)
        context.Database.ExecuteSqlRaw(@"
            UPDATE Tenants 
            SET WhatsAppNumber = '8143712528', 
                ServiceType = 'Meta',
                ProviderApiKey = 'EAAOAfLtTZCVQBRrLBQPHZAewl5gqXkqxT7ktxK7N8sIZCM1ZASN8zZBQatZBqhUmtwMsEA8m5ZCOsjBKktQY4hLQiOErd5N2zkXJVcDmCBXNF81P4NYm7ZBfr2nb3JDN7exkZBsN8rPI4BE04TSpmA3nBvNhbEBbV0tY3ZAtkJxZCSVhtDXDRWteaIS0HchcGnDOVrwAhxwDwUb4az2GUFiQwG9ZBvixDxqm62tiDfvFdZAQZBU4QX0ZB0krliWZC7ZAxI4MWRAk1rQT7GQW2PGTdicyANoai', 
                ProviderSenderId = '1184346914753507',
                ProviderAccountId = '1720827125758007'
            WHERE Id = '73349044-4ef4-405d-bd9f-6e6e06344860'");

        // Self-healing: Ensure TenantTemplates table exists
        context.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantTemplates')
            BEGIN
                CREATE TABLE TenantTemplates (
                    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                    TenantId UNIQUEIDENTIFIER NOT NULL,
                    Name NVARCHAR(256) NOT NULL,
                    Category NVARCHAR(64) NOT NULL,
                    Language NVARCHAR(16) NOT NULL,
                    Body NVARCHAR(MAX) NOT NULL,
                    Status NVARCHAR(64) NOT NULL DEFAULT 'Approved',
                    Timestamp DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                    CONSTRAINT FK_TenantTemplates_Tenants FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE
                );
            END");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error seeding DB: {ex.Message}");
    }
}

app.Run();
