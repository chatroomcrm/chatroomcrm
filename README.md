# ChatFlow — WhatsApp CRM SaaS Codebase

This repository contains the complete, production-ready source code for **ChatFlow**, a multi-tenant WhatsApp CRM SaaS platform. 

It is split into two core workspaces:
- `/backend` — ASP.NET Core 8 Web API incorporating Entity Framework Core, PostgreSQL, and SignalR.
- `/frontend` — React 18, TypeScript, and TailwindCSS client interfaces.

---

## 🛠️ Tech Stack & Dependencies

### Backend
- **ASP.NET Core Web API (.NET 8)**
- **Entity Framework Core (EF Core)**
- **PostgreSQL Database Provider (Npgsql)**
- **SignalR (WebSockets Protocol)**
- **JWT (JSON Web Token) Security System**

### Frontend
- **React 18 + TypeScript + Vite**
- **TailwindCSS**
- **@microsoft/signalr client**
- **Lucide React Icons**

---

## 🗄️ Database Architecture & MS SQL Server Showcase

For developers with experience in **SQL**, ChatFlow utilizes a highly structured relational schema matching the multi-tenant architecture. Entity Framework Core automatically translates our C# entity profiles into highly efficient tables, keys, and indexes in Microsoft SQL Server.

### Entity Framework Core Table Migrations (SQL Server Schema)

When you run standard migrations, Entity Framework compiles the following relational structures:

```sql
-- 1. Tenants Table (Isolated branding and metadata)
CREATE TABLE Tenants (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Name NVARCHAR(200) NOT NULL,
    LogoUrl NVARCHAR(MAX),
    ThemeColor NVARCHAR(20) DEFAULT '#4facfe'
);

-- 2. Users Table (Tenant staff & administrators)
CREATE TABLE Users (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Name NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(50),
    Email NVARCHAR(255) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(MAX) NOT NULL,
    Role NVARCHAR(50) DEFAULT 'Agent',
    TenantId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Tenants(Id) ON DELETE CASCADE
);

-- 3. Contacts Table (Customer WhatsApp records)
CREATE TABLE Contacts (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Name NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(50) NOT NULL,
    TenantId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Tenants(Id) ON DELETE CASCADE
);

-- 4. Leads Table (Sales lifecycle stages)
CREATE TABLE Leads (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ContactId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Contacts(Id) ON DELETE CASCADE,
    Status NVARCHAR(50) DEFAULT 'New', -- New, Contacted, Qualified, Proposal, Won, Lost
    AssignedTo UNIQUEIDENTIFIER FOREIGN KEY REFERENCES Users(Id) ON DELETE NO ACTION,
    TenantId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Tenants(Id) ON DELETE NO ACTION
);

-- 5. Messages Table (Chat records mapping back to leads)
CREATE TABLE Messages (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    LeadId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Leads(Id) ON DELETE CASCADE,
    Content NVARCHAR(MAX) NOT NULL,
    Direction NVARCHAR(20) NOT NULL, -- Incoming, Outgoing
    ProviderMessageId NVARCHAR(100), -- Twilio ID
    Timestamp DATETIMEOFFSET DEFAULT SYSDATETIMEOFFSET(),
    TenantId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Tenants(Id) ON DELETE NO ACTION
);

-- 6. Tasks Table (Staff action reminders)
CREATE TABLE Tasks (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    LeadId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Leads(Id) ON DELETE CASCADE,
    DueDate DATETIMEOFFSET NOT NULL,
    Status NVARCHAR(50) DEFAULT 'Pending', -- Pending, Completed
    Notes NVARCHAR(MAX),
    TenantId UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES Tenants(Id) ON DELETE NO ACTION
);
```

### High-Performance Database Indexes
To handle massive throughput from active WhatsApp webhooks, we configure explicit composite indexes to minimize database lookup times:
```sql
-- Fast matching of customer phone numbers under active tenants
CREATE UNIQUE INDEX IX_Contacts_Tenant_Phone ON Contacts(TenantId, Phone);

-- Instant sorting and rendering of the Leads Board columns
CREATE INDEX IX_Leads_Tenant_Status ON Leads(TenantId, Status);

-- Chronological conversation loading for chat log rendering
CREATE INDEX IX_Messages_Lead_Timestamp ON Messages(LeadId, Timestamp);
```

---

## ⚡ Useful Analytics SQL Queries (T-SQL)
Here are some highly useful database administration queries you can run in your SQL Server management client (SSMS, Azure Data Studio, or DBeaver) to extract SaaS telemetry:

### 1. Lead Conversion Funnel (Conversion Ratios by Tenant)
```sql
SELECT 
    t.Name AS TenantName,
    COUNT(l.Id) AS TotalLeads,
    SUM(CASE WHEN l.Status = 'Won' THEN 1 ELSE 0 END) AS WonLeads,
    ROUND((CAST(SUM(CASE WHEN l.Status = 'Won' THEN 1 ELSE 0 END) AS NUMERIC) / NULLIF(COUNT(l.Id), 0)) * 100, 2) AS ConversionRatePercentage
FROM Tenants t
LEFT JOIN Leads l ON t.Id = l.TenantId
GROUP BY t.Id, t.Name;
```

### 2. Message Velocity & Activity (Hourly Load Statistics)
```sql
SELECT 
    DATEADD(hour, DATEDIFF(hour, 0, m.Timestamp), 0) AS ActivityHour,
    m.Direction,
    COUNT(m.Id) AS MessageCount
FROM Messages m
WHERE m.Timestamp >= DATEADD(day, -7, SYSDATETIMEOFFSET())
GROUP BY DATEADD(hour, DATEDIFF(hour, 0, m.Timestamp), 0), m.Direction
ORDER BY ActivityHour DESC;
```

### 3. Agent Performance (Assigned Leads & Resolution Tiers)
```sql
SELECT 
    u.Name AS AgentName,
    t.Name AS TenantName,
    COUNT(l.Id) AS AssignedLeadsCount,
    SUM(CASE WHEN l.Status = 'Won' THEN 1 ELSE 0 END) AS WonLeads,
    SUM(CASE WHEN l.Status = 'Lost' THEN 1 ELSE 0 END) AS LostLeads
FROM Users u
JOIN Tenants t ON u.TenantId = t.Id
LEFT JOIN Leads l ON u.Id = l.AssignedTo
GROUP BY u.Id, u.Name, t.Name;
```

---

## 🚀 Setup & Execution Instructions

### Running the Backend Web API
1. Navigate to `/backend` in your shell.
2. Update the connection parameters in `appsettings.json` to link to your live PostgreSQL server.
3. Open your terminal and execute:
   ```bash
   dotnet restore
   dotnet run
   ```
4. The system will automatically build the environment, compile tables, run basic seeds, and bind to `http://localhost:5000`.

### Running the React Frontend Client
1. Navigate to `/frontend` in your shell.
2. Install the package configurations:
   ```bash
   npm install
   ```
3. Boot the local Vite development pipeline:
   ```bash
   npm run dev
   ```
4. Click on the local link returned in your terminal (typically `http://localhost:5173`) to view the client console.
