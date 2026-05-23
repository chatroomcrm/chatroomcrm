-- ===================================================================================
-- ChatFlow WhatsApp CRM — Roles and Swagger Schema & Data Migration Script
-- Target Database: Microsoft SQL Server LocalDB (chatflow_db)
-- Authentication: Windows Authentication
--
-- Running instructions:
-- 1. Open Azure Data Studio or SQL Server Management Studio.
-- 2. Connect to your server (localdb)\MSSQLLocalDB
-- 3. Open this file and click "Run" (F5) to apply updates.
-- ===================================================================================

USE chatflow_db;
GO

PRINT '--- Starting Database Schema Alteration for Multi-Role Support ---';

-- 1. Safely add 'IsBlocked' to Tenants table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'IsBlocked')
BEGIN
    ALTER TABLE Tenants ADD IsBlocked BIT NOT NULL DEFAULT 0;
    PRINT 'Added column [IsBlocked] to Tenants table.';
END
ELSE
BEGIN
    PRINT 'Column [IsBlocked] already exists in Tenants table.';
END
GO

-- 1a. Safely add 'Name' to Tenants table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'Name')
BEGIN
    ALTER TABLE Tenants ADD Name NVARCHAR(MAX) NOT NULL DEFAULT '';
    PRINT 'Added column [Name] to Tenants table.';
END
ELSE
BEGIN
    PRINT 'Column [Name] already exists in Tenants table.';
END
GO

-- 1b. Safely add 'LogoUrl' to Tenants table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'LogoUrl')
BEGIN
    ALTER TABLE Tenants ADD LogoUrl NVARCHAR(MAX) NOT NULL DEFAULT '';
    PRINT 'Added column [LogoUrl] to Tenants table.';
END
ELSE
BEGIN
    PRINT 'Column [LogoUrl] already exists in Tenants table.';
END
GO

-- 1c. Safely add 'ThemeColor' to Tenants table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Tenants') AND name = 'ThemeColor')
BEGIN
    ALTER TABLE Tenants ADD ThemeColor NVARCHAR(MAX) NOT NULL DEFAULT '#10b981|#0284c7';
    PRINT 'Added column [ThemeColor] to Tenants table.';
END
ELSE
BEGIN
    PRINT 'Column [ThemeColor] already exists in Tenants table.';
END
GO

-- 2. Safely add 'IsBlocked' to Users table if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'IsBlocked')
BEGIN
    ALTER TABLE Users ADD IsBlocked BIT NOT NULL DEFAULT 0;
    PRINT 'Added column [IsBlocked] to Users table.';
END
ELSE
BEGIN
    PRINT 'Column [IsBlocked] already exists in Users table.';
END
GO

-- 3. Dynamically drop existing foreign key constraints on Users.TenantId to allow column type alteration
DECLARE @ConstraintName NVARCHAR(256);

SELECT @ConstraintName = fk.name
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN sys.columns c ON fkc.parent_object_id = c.object_id AND fkc.parent_column_id = c.column_id
WHERE fk.parent_object_id = OBJECT_ID('Users') 
  AND fk.referenced_object_id = OBJECT_ID('Tenants')
  AND c.name = 'TenantId';

IF @ConstraintName IS NOT NULL
BEGIN
    EXEC('ALTER TABLE Users DROP CONSTRAINT [' + @ConstraintName + ']');
    PRINT 'Dropped foreign key constraint: [' + @ConstraintName + '] to allow column type alteration.';
END
ELSE
BEGIN
    PRINT 'No foreign key constraint detected on Users.TenantId. Proceeding...';
END
GO

-- 4. Alter TenantId column in Users table to be nullable (optional relationship for global Super Admin)
ALTER TABLE Users ALTER COLUMN TenantId UNIQUEIDENTIFIER NULL;
PRINT 'Altered column Users.TenantId to be NULLable (UNIQUEIDENTIFIER NULL).';
GO

-- 5. Re-create the foreign key constraint pointing to Tenants(Id) with ON DELETE CASCADE
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Users_Tenants_TenantId' AND parent_object_id = OBJECT_ID('Users'))
BEGIN
    ALTER TABLE Users ADD CONSTRAINT FK_Users_Tenants_TenantId 
    FOREIGN KEY (TenantId) REFERENCES Tenants(Id) 
    ON DELETE CASCADE;
    PRINT 'Created foreign key constraint FK_Users_Tenants_TenantId with ON DELETE CASCADE.';
END
ELSE
BEGIN
    PRINT 'Foreign key constraint FK_Users_Tenants_TenantId already exists.';
END
GO

-- 6. Update John Admin's seeded role to TenantAdmin (from Admin)
UPDATE Users SET Role = 'TenantAdmin' WHERE Email = 'admin@acme.com' AND Role = 'Admin';
PRINT 'Updated John Admin role from "Admin" to "TenantAdmin" (Business Owner).';
GO

-- 7. Seed Platform Super Admin User (superadmin@chatflow.com / password123) if not exists
-- The password hash for "password123" is "75K3eLr+dx6JJFuJ7LwIpEpOFmwGZZkRiB84PURz6U8="
IF NOT EXISTS (SELECT * FROM Users WHERE Email = 'superadmin@chatflow.com')
BEGIN
    INSERT INTO Users (Id, Name, Email, Phone, PasswordHash, Role, TenantId, IsBlocked)
    VALUES (NEWID(), 'Platform Administrator', 'superadmin@chatflow.com', '+10000000', '75K3eLr+dx6JJFuJ7LwIpEpOFmwGZZkRiB84PURz6U8=', 'SuperAdmin', NULL, 0);
    PRINT 'Seeded Platform Administrator user (superadmin@chatflow.com / password123).';
END
ELSE
BEGIN
    PRINT 'Platform Administrator user already seeded.';
END
GO

-- 8. Seed Acme Tenant Agent Sarah Agent (agent@acme.com / password123) if not exists
-- First, find the Acme Tenant ID
DECLARE @AcmeTenantId UNIQUEIDENTIFIER;
SELECT TOP 1 @AcmeTenantId = Id FROM Tenants WHERE Name LIKE '%Acme%' OR Name LIKE '%Demo%';

IF @AcmeTenantId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT * FROM Users WHERE Email = 'agent@acme.com')
    BEGIN
        INSERT INTO Users (Id, Name, Email, Phone, PasswordHash, Role, TenantId, IsBlocked)
        VALUES (NEWID(), 'Sarah Agent', 'agent@acme.com', '+15550188', '75K3eLr+dx6JJFuJ7LwIpEpOFmwGZZkRiB84PURz6U8=', 'Agent', @AcmeTenantId, 0);
        PRINT 'Seeded Tenant Agent user Sarah Agent (agent@acme.com / password123).';
    END
    ELSE
    BEGIN
        PRINT 'Tenant Agent user already seeded.';
    END
END
ELSE
BEGIN
    PRINT 'WARNING: Demo Tenant not found. Could not seed Sarah Agent. Please sign up a Tenant first.';
END
GO

-- 9. Seed/Update standard branding settings for the demo tenant at the database level
DECLARE @DemoTenantId UNIQUEIDENTIFIER;
SELECT TOP 1 @DemoTenantId = Id FROM Tenants WHERE Name LIKE '%Acme%' OR Name LIKE '%Demo%';

IF @DemoTenantId IS NOT NULL
BEGIN
    UPDATE Tenants 
    SET Name = 'ChatRoom CRM', 
        LogoUrl = 'CR', 
        ThemeColor = '#10b981|#0284c7' 
    WHERE Id = @DemoTenantId;
    PRINT 'Updated demo tenant branding settings at the database level (ChatRoom CRM / CR / #10b981|#0284c7).';
END
GO

PRINT '--- Database Schema Alteration and Data Seeding Completed Successfully ---';
GO
