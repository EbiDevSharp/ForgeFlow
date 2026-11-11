$ErrorActionPreference = 'Stop'
$conn = 'Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'

$sql = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'auth.RoleDefinition', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_SecurityEvent_Code' AND parent_object_id = OBJECT_ID(N'auth.SecurityEvent'))
        ALTER TABLE [auth].[SecurityEvent] DROP CONSTRAINT [CK_SecurityEvent_Code];

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Role_Scope' AND parent_object_id = OBJECT_ID(N'auth.ScopedRoleAssignment'))
        ALTER TABLE [auth].[ScopedRoleAssignment] DROP CONSTRAINT [CK_Role_Scope];

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ScopedRoleAssignment_UserId_RoleId' AND object_id = OBJECT_ID(N'auth.ScopedRoleAssignment'))
        DROP INDEX [IX_ScopedRoleAssignment_UserId_RoleId] ON [auth].[ScopedRoleAssignment];

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId' AND object_id = OBJECT_ID(N'auth.ScopedRoleAssignment'))
        DROP INDEX [IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId] ON [auth].[ScopedRoleAssignment];

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId' AND object_id = OBJECT_ID(N'auth.ScopedRoleAssignment'))
        DROP INDEX [IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId] ON [auth].[ScopedRoleAssignment];

    ALTER TABLE [auth].[ScopedRoleAssignment] ALTER COLUMN [RoleId] bigint NOT NULL;

    CREATE UNIQUE INDEX [IX_ScopedRoleAssignment_UserId_RoleId]
        ON [auth].[ScopedRoleAssignment] ([UserId], [RoleId]) WHERE [ScopeKind] = 0;

    CREATE UNIQUE INDEX [IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId]
        ON [auth].[ScopedRoleAssignment] ([UserId], [RoleId], [OrganizationId]) WHERE [ScopeKind] = 1;

    CREATE UNIQUE INDEX [IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId]
        ON [auth].[ScopedRoleAssignment] ([UserId], [RoleId], [OrganizationId], [SiteId]) WHERE [ScopeKind] = 2;

    CREATE TABLE [auth].[RoleDefinition] (
        [Id] bigint NOT NULL IDENTITY(100, 1),
        [Name] nvarchar(100) NOT NULL,
        [NormalizedName] nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [IsSystem] bit NOT NULL,
        [ScopeKind] tinyint NOT NULL,
        [OrganizationId] bigint NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_RoleDefinition] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RoleDefinition_Invariant] CHECK (([IsSystem]=1 AND [OrganizationId] IS NULL AND [Id] IN (1,2,3) AND [IsActive]=1) OR ([IsSystem]=0 AND [OrganizationId] IS NOT NULL AND [Id] >= 100 AND [ScopeKind] IN (1,2))),
        CONSTRAINT [CK_RoleDefinition_Metadata] CHECK ((([IsSystem]=1 AND [CreatedByActorId]=0) OR ([IsSystem]=0 AND [CreatedByActorId]>0)) AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))),
        CONSTRAINT [FK_RoleDefinition_Organization_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );

    CREATE TABLE [auth].[UserPermissionGrant] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [UserId] bigint NOT NULL,
        [ScopeKind] tinyint NOT NULL,
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NULL,
        [PermissionCode] varchar(64) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_UserPermissionGrant] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserPermissionGrant_Metadata] CHECK ([CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))),
        CONSTRAINT [CK_UserPermissionGrant_Scope] CHECK (LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode]) AND (([ScopeKind]=1 AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [SiteId] IS NOT NULL))),
        CONSTRAINT [FK_UserPermissionGrant_OrganizationMembership_UserId_OrganizationId] FOREIGN KEY ([UserId], [OrganizationId]) REFERENCES [auth].[OrganizationMembership] ([UserId], [OrganizationId]),
        CONSTRAINT [FK_UserPermissionGrant_SiteMembership_UserId_OrganizationId_SiteId] FOREIGN KEY ([UserId], [OrganizationId], [SiteId]) REFERENCES [auth].[SiteMembership] ([UserId], [OrganizationId], [SiteId]),
        CONSTRAINT [FK_UserPermissionGrant_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );

    CREATE TABLE [auth].[RolePermission] (
        [RoleId] bigint NOT NULL,
        [PermissionCode] varchar(64) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_RolePermission] PRIMARY KEY ([RoleId], [PermissionCode]),
        CONSTRAINT [CK_RolePermission_Code] CHECK (LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode])),
        CONSTRAINT [CK_RolePermission_Metadata] CHECK ([CreatedByActorId] >= 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))),
        CONSTRAINT [FK_RolePermission_RoleDefinition_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [auth].[RoleDefinition] ([Id])
    );

    SET IDENTITY_INSERT [auth].[RoleDefinition] ON;
    INSERT INTO [auth].[RoleDefinition] ([Id], [CreatedAtUtc], [CreatedByActorId], [IsActive], [IsSystem], [Name], [NormalizedName], [OrganizationId], [ScopeKind], [UpdatedAtUtc], [UpdatedByActorId])
    VALUES
        (1, '2026-11-10T00:00:00.0000000Z', 0, 1, 1, N'OrganizationAdministrator', N'ORGANIZATIONADMINISTRATOR', NULL, 1, NULL, NULL),
        (2, '2026-11-10T00:00:00.0000000Z', 0, 1, 1, N'SiteOperator', N'SITEOPERATOR', NULL, 2, NULL, NULL),
        (3, '2026-11-10T00:00:00.0000000Z', 0, 1, 1, N'DeploymentProvisioner', N'DEPLOYMENTPROVISIONER', NULL, 0, NULL, NULL);
    SET IDENTITY_INSERT [auth].[RoleDefinition] OFF;

    INSERT INTO [auth].[RolePermission] ([PermissionCode], [RoleId], [CreatedAtUtc], [CreatedByActorId], [IsActive], [UpdatedAtUtc], [UpdatedByActorId])
    VALUES
        ('Memberships.Manage', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Organizations.Read', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Organizations.SetActive', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Organizations.Update', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Permissions.Assign', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Roles.AssignFixed', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Roles.Manage', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Roles.Read', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Sites.Create', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Sites.Read', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Sites.SetActive', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Sites.Update', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.Create', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.Read', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.ResetPassword', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.SetActive', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.Unlock', 1, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Sites.Read', 2, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Sites.SetActive', 2, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Sites.Update', 2, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Organizations.Create', 3, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.AttachOrganization', 3, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.ResetPassword', 3, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.SetActive', 3, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL),
        ('Users.Unlock', 3, '2026-11-10T00:00:00.0000000Z', 0, 1, NULL, NULL);

    ALTER TABLE [auth].[SecurityEvent] ADD CONSTRAINT [CK_SecurityEvent_Code]
        CHECK (DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged'));

    CREATE INDEX [IX_ScopedRoleAssignment_RoleId] ON [auth].[ScopedRoleAssignment] ([RoleId]);

    ALTER TABLE [auth].[ScopedRoleAssignment] ADD CONSTRAINT [CK_Role_Scope]
        CHECK (([RoleId]=3 AND [ScopeKind]=0 AND [OrganizationId] IS NULL AND [SiteId] IS NULL) OR ([RoleId]=1 AND [ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([RoleId]=2 AND [ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL) OR ([RoleId]>=100 AND (([ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL))));

    CREATE INDEX [IX_RoleDefinition_OrganizationId_IsActive_ScopeKind] ON [auth].[RoleDefinition] ([OrganizationId], [IsActive], [ScopeKind]);

    CREATE UNIQUE INDEX [UX_RoleDefinition_Org_Name] ON [auth].[RoleDefinition] ([OrganizationId], [NormalizedName]) WHERE [IsSystem] = 0;

    CREATE INDEX [IX_RolePermission_PermissionCode_IsActive_RoleId] ON [auth].[RolePermission] ([PermissionCode], [IsActive], [RoleId]);

    CREATE INDEX [IX_UserPermissionGrant_UserId_IsActive_ScopeKind_OrganizationId_SiteId_PermissionCode] ON [auth].[UserPermissionGrant] ([UserId], [IsActive], [ScopeKind], [OrganizationId], [SiteId], [PermissionCode]);

    CREATE UNIQUE INDEX [IX_UserPermissionGrant_UserId_OrganizationId_PermissionCode] ON [auth].[UserPermissionGrant] ([UserId], [OrganizationId], [PermissionCode]) WHERE [ScopeKind] = 1;

    CREATE UNIQUE INDEX [IX_UserPermissionGrant_UserId_OrganizationId_SiteId_PermissionCode] ON [auth].[UserPermissionGrant] ([UserId], [OrganizationId], [SiteId], [PermissionCode]) WHERE [ScopeKind] = 2;

    ALTER TABLE [auth].[ScopedRoleAssignment] ADD CONSTRAINT [FK_ScopedRoleAssignment_RoleDefinition_RoleId]
        FOREIGN KEY ([RoleId]) REFERENCES [auth].[RoleDefinition] ([Id]);

    IF DATABASE_PRINCIPAL_ID('ForgeFlowAuthRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT ON OBJECT::[auth].[RoleDefinition] TO [ForgeFlowAuthRuntime];
        GRANT SELECT ON OBJECT::[auth].[RolePermission] TO [ForgeFlowAuthRuntime];
        GRANT SELECT ON OBJECT::[auth].[UserPermissionGrant] TO [ForgeFlowAuthRuntime];
        DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[RoleDefinition] TO [ForgeFlowAuthRuntime];
        DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[RolePermission] TO [ForgeFlowAuthRuntime];
        DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserPermissionGrant] TO [ForgeFlowAuthRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261110134703_RolesAndGranularPermissions')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110134703_RolesAndGranularPermissions', N'10.0.5');
END;

-- Ensure the primary admin user (lowest active UserId with OrganizationAdministrator) has full permissions (OrganizationAdministrator + DeploymentProvisioner)
DECLARE @adminUserId bigint = (
    SELECT TOP (1) [UserId]
    FROM [auth].[ScopedRoleAssignment]
    WHERE [RoleId] = 1 AND [IsActive] = 1
    ORDER BY [UserId]
);

IF @adminUserId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [auth].[ScopedRoleAssignment] WHERE [UserId] = @adminUserId AND [ScopeKind] = 0 AND [RoleId] = 3)
    BEGIN
        INSERT INTO [auth].[ScopedRoleAssignment] ([UserId], [RoleId], [ScopeKind], [OrganizationId], [SiteId], [IsActive], [CreatedAtUtc], [CreatedByActorId], [UpdatedAtUtc], [UpdatedByActorId])
        VALUES (@adminUserId, 3, 0, NULL, NULL, 1, SYSUTCDATETIME(), @adminUserId, NULL, NULL);
    END
    ELSE
    BEGIN
        UPDATE [auth].[ScopedRoleAssignment]
        SET [IsActive] = 1, [UpdatedAtUtc] = SYSUTCDATETIME(), [UpdatedByActorId] = @adminUserId
        WHERE [UserId] = @adminUserId AND [ScopeKind] = 0 AND [RoleId] = 3 AND [IsActive] = 0;
    END;
END;

IF OBJECT_ID(N'organization.Unit', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrganizationChange_Event' AND parent_object_id = OBJECT_ID(N'organization.OrganizationChange'))
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    CREATE TABLE [organization].[Unit] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Symbol] nvarchar(32) NOT NULL,
        [Dimension] tinyint NOT NULL,
        [PrecisionScale] tinyint NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Unit] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Unit_Organization_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Unit_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Unit_Dimension] CHECK ([Dimension] BETWEEN 1 AND 6),
        CONSTRAINT [CK_Unit_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Unit_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [CK_Unit_PrecisionScale] CHECK ([PrecisionScale] BETWEEN 0 AND 6),
        CONSTRAINT [CK_Unit_Symbol] CHECK (DATALENGTH([Symbol]) BETWEEN 2 AND 64 AND DATALENGTH([Symbol]) = DATALENGTH(LTRIM(RTRIM([Symbol]))) AND DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0),
        CONSTRAINT [FK_Unit_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );

    CREATE TABLE [organization].[UnitConversion] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [FromUnitId] bigint NOT NULL,
        [ToUnitId] bigint NOT NULL,
        [Factor] decimal(18,6) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_UnitConversion] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UnitConversion_DifferentUnits] CHECK ([FromUnitId] <> [ToUnitId]),
        CONSTRAINT [CK_UnitConversion_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_UnitConversion_PositiveFactor] CHECK ([Factor] > 0),
        CONSTRAINT [FK_UnitConversion_FromUnit] FOREIGN KEY ([OrganizationId], [FromUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_UnitConversion_ToUnit] FOREIGN KEY ([OrganizationId], [ToUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id])
    );

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event]
        CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated'))));

    CREATE INDEX [IX_Unit_Organization_IsActive_Code] ON [organization].[Unit] ([OrganizationId], [IsActive], [Code], [Id]);
    CREATE UNIQUE INDEX [UX_Unit_Organization_Code] ON [organization].[Unit] ([OrganizationId], [Code]);
    CREATE INDEX [IX_UnitConversion_OrganizationId_ToUnitId] ON [organization].[UnitConversion] ([OrganizationId], [ToUnitId]);
    CREATE UNIQUE INDEX [UX_UnitConversion_Organization_From_To] ON [organization].[UnitConversion] ([OrganizationId], [FromUnitId], [ToUnitId]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Unit] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[UnitConversion] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Unit] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[UnitConversion] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261110164056_UnitsAndConversions')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110164056_UnitsAndConversions', N'10.0.5');
END;

IF OBJECT_ID(N'organization.Item', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrganizationChange_Event' AND parent_object_id = OBJECT_ID(N'organization.OrganizationChange'))
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    CREATE TABLE [organization].[AttributeDefinition] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [DataType] tinyint NOT NULL,
        [UnitId] bigint NULL,
        [IsRequired] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_AttributeDefinition] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_AttributeDefinition_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_AttributeDefinition_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_AttributeDefinition_DataType] CHECK ([DataType] BETWEEN 1 AND 3),
        CONSTRAINT [CK_AttributeDefinition_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_AttributeDefinition_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_AttributeDefinition_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_AttributeDefinition_Unit] FOREIGN KEY ([OrganizationId], [UnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ClassificationSet] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsMandatory] bit NOT NULL,
        [AllowMultiple] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ClassificationSet] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ClassificationSet_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_ClassificationSet_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_ClassificationSet_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ClassificationSet_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_ClassificationSet_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );

    CREATE TABLE [organization].[Item] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(500) NULL,
        [BaseUnitId] bigint NOT NULL,
        [CanBePurchased] bit NOT NULL,
        [CanBeManufactured] bit NOT NULL,
        [CanBeSold] bit NOT NULL,
        [CanBeConsumed] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Item] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Item_Organization_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Item_Capability] CHECK ([CanBePurchased] = 1 OR [CanBeManufactured] = 1 OR [CanBeSold] = 1 OR [CanBeConsumed] = 1),
        CONSTRAINT [CK_Item_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Item_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Item_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_Item_BaseUnit] FOREIGN KEY ([OrganizationId], [BaseUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_Item_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );

    CREATE TABLE [organization].[Classification] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [ClassificationSetId] bigint NOT NULL,
        [ParentClassificationId] bigint NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Classification] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Classification_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Classification_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Classification_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Classification_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [CK_Classification_NoSelfParent] CHECK ([ParentClassificationId] IS NULL OR [ParentClassificationId] <> [Id]),
        CONSTRAINT [FK_Classification_Parent] FOREIGN KEY ([OrganizationId], [ParentClassificationId]) REFERENCES [organization].[Classification] ([OrganizationId], [Id]),
        CONSTRAINT [FK_Classification_Set] FOREIGN KEY ([OrganizationId], [ClassificationSetId]) REFERENCES [organization].[ClassificationSet] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ItemAttributeValue] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [ItemId] bigint NOT NULL,
        [AttributeDefinitionId] bigint NOT NULL,
        [TextValue] nvarchar(400) NULL,
        [DecimalValue] decimal(18,6) NULL,
        [BooleanValue] bit NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ItemAttributeValue] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ItemAttributeValue_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [FK_ItemAttributeValue_Definition] FOREIGN KEY ([OrganizationId], [AttributeDefinitionId]) REFERENCES [organization].[AttributeDefinition] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ItemAttributeValue_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ItemUnitConversion] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [ItemId] bigint NOT NULL,
        [FromUnitId] bigint NOT NULL,
        [ToUnitId] bigint NOT NULL,
        [Factor] decimal(18,6) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ItemUnitConversion] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ItemUnitConversion_DifferentUnits] CHECK ([FromUnitId] <> [ToUnitId]),
        CONSTRAINT [CK_ItemUnitConversion_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ItemUnitConversion_PositiveFactor] CHECK ([Factor] > 0),
        CONSTRAINT [FK_ItemUnitConversion_FromUnit] FOREIGN KEY ([OrganizationId], [FromUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ItemUnitConversion_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ItemUnitConversion_ToUnit] FOREIGN KEY ([OrganizationId], [ToUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ItemClassification] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [ItemId] bigint NOT NULL,
        [ClassificationId] bigint NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ItemClassification] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ItemClassification_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [FK_ItemClassification_Classification] FOREIGN KEY ([OrganizationId], [ClassificationId]) REFERENCES [organization].[Classification] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ItemClassification_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id])
    );

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event] CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated'))));

    CREATE INDEX [IX_AttributeDefinition_OrganizationId_UnitId] ON [organization].[AttributeDefinition] ([OrganizationId], [UnitId]);
    CREATE UNIQUE INDEX [UX_AttributeDefinition_Org_Code] ON [organization].[AttributeDefinition] ([OrganizationId], [Code]);
    CREATE INDEX [IX_Classification_OrganizationId_ParentClassificationId] ON [organization].[Classification] ([OrganizationId], [ParentClassificationId]);
    CREATE UNIQUE INDEX [UX_Classification_Org_Set_Code] ON [organization].[Classification] ([OrganizationId], [ClassificationSetId], [Code]);
    CREATE UNIQUE INDEX [UX_ClassificationSet_Org_Code] ON [organization].[ClassificationSet] ([OrganizationId], [Code]);
    CREATE INDEX [IX_Item_Organization_IsActive_Code] ON [organization].[Item] ([OrganizationId], [IsActive], [Code], [Id]);
    CREATE INDEX [IX_Item_OrganizationId_BaseUnitId] ON [organization].[Item] ([OrganizationId], [BaseUnitId]);
    CREATE UNIQUE INDEX [UX_Item_Organization_Code] ON [organization].[Item] ([OrganizationId], [Code]);
    CREATE INDEX [IX_ItemAttributeValue_OrganizationId_AttributeDefinitionId] ON [organization].[ItemAttributeValue] ([OrganizationId], [AttributeDefinitionId]);
    CREATE UNIQUE INDEX [UX_ItemAttributeValue_Org_Item_Def] ON [organization].[ItemAttributeValue] ([OrganizationId], [ItemId], [AttributeDefinitionId]);
    CREATE INDEX [IX_ItemClassification_OrganizationId_ClassificationId] ON [organization].[ItemClassification] ([OrganizationId], [ClassificationId]);
    CREATE UNIQUE INDEX [UX_ItemClassification_Org_Item_Class] ON [organization].[ItemClassification] ([OrganizationId], [ItemId], [ClassificationId]);
    CREATE INDEX [IX_ItemUnitConversion_OrganizationId_FromUnitId] ON [organization].[ItemUnitConversion] ([OrganizationId], [FromUnitId]);
    CREATE INDEX [IX_ItemUnitConversion_OrganizationId_ToUnitId] ON [organization].[ItemUnitConversion] ([OrganizationId], [ToUnitId]);
    CREATE UNIQUE INDEX [UX_ItemUnitConversion_Org_Item_From_To] ON [organization].[ItemUnitConversion] ([OrganizationId], [ItemId], [FromUnitId], [ToUnitId]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ClassificationSet] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Classification] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Item] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ItemUnitConversion] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[AttributeDefinition] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ItemClassification] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ItemAttributeValue] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[ClassificationSet] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Classification] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Item] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[ItemUnitConversion] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[AttributeDefinition] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261110171954_ItemsClassificationsAttributes')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110171954_ItemsClassificationsAttributes', N'10.0.14');
END;

IF OBJECT_ID(N'organization.Bom', N'U') IS NULL
BEGIN
    ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    CREATE TABLE [organization].[Bom] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [PrimaryOutputItemId] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Bom] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Bom_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Bom_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Bom_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Bom_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_Bom_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_Bom_PrimaryItem] FOREIGN KEY ([OrganizationId], [PrimaryOutputItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ContextDimension] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ContextDimension] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ContextDimension_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_ContextDimension_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_ContextDimension_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ContextDimension_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_ContextDimension_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );

    CREATE TABLE [organization].[BomVersion] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [BomId] bigint NOT NULL,
        [VersionNumber] int NOT NULL,
        [Status] tinyint NOT NULL,
        [BaseOutputQuantity] decimal(18,6) NOT NULL,
        [BaseOutputUnitId] bigint NOT NULL,
        [EffectiveFromDate] date NOT NULL,
        [EffectiveToDate] date NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_BomVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_BomVersion_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_BomVersion_EffectiveDates] CHECK ([EffectiveToDate] IS NULL OR [EffectiveToDate] >= [EffectiveFromDate]),
        CONSTRAINT [CK_BomVersion_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_BomVersion_Number] CHECK ([VersionNumber] > 0),
        CONSTRAINT [CK_BomVersion_PositiveBaseQty] CHECK ([BaseOutputQuantity] > 0),
        CONSTRAINT [CK_BomVersion_Status] CHECK ([Status] BETWEEN 1 AND 5),
        CONSTRAINT [FK_BomVersion_BaseUnit] FOREIGN KEY ([OrganizationId], [BaseOutputUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_BomVersion_Bom] FOREIGN KEY ([OrganizationId], [BomId]) REFERENCES [organization].[Bom] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[BomApplicability] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [BomVersionId] bigint NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_BomApplicability] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_BomApplicability_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_BomApplicability_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_BomApplicability_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_BomApplicability_Version] FOREIGN KEY ([OrganizationId], [BomVersionId]) REFERENCES [organization].[BomVersion] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[BomInput] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [BomVersionId] bigint NOT NULL,
        [Sequence] int NOT NULL,
        [ItemId] bigint NOT NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [UnitId] bigint NOT NULL,
        [ScrapPercentage] decimal(5,2) NOT NULL,
        [IsOptional] bit NOT NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_BomInput] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_BomInput_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_BomInput_PositiveQty] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_BomInput_ScrapBounds] CHECK ([ScrapPercentage] >= 0 AND [ScrapPercentage] < 100),
        CONSTRAINT [CK_BomInput_Sequence] CHECK ([Sequence] > 0),
        CONSTRAINT [FK_BomInput_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_BomInput_Unit] FOREIGN KEY ([OrganizationId], [UnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_BomInput_Version] FOREIGN KEY ([OrganizationId], [BomVersionId]) REFERENCES [organization].[BomVersion] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[BomOutput] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [BomVersionId] bigint NOT NULL,
        [Sequence] int NOT NULL,
        [ItemId] bigint NOT NULL,
        [OutputType] tinyint NOT NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [UnitId] bigint NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_BomOutput] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_BomOutput_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_BomOutput_PositiveQty] CHECK ([Quantity] > 0),
        CONSTRAINT [CK_BomOutput_Sequence] CHECK ([Sequence] > 0),
        CONSTRAINT [CK_BomOutput_Type] CHECK ([OutputType] BETWEEN 1 AND 4),
        CONSTRAINT [FK_BomOutput_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_BomOutput_Unit] FOREIGN KEY ([OrganizationId], [UnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_BomOutput_Version] FOREIGN KEY ([OrganizationId], [BomVersionId]) REFERENCES [organization].[BomVersion] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[BomApplicabilityCondition] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [BomApplicabilityId] bigint NOT NULL,
        [ContextDimensionId] bigint NOT NULL,
        [Operator] tinyint NOT NULL,
        [Value] nvarchar(400) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_BomApplicabilityCondition] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_BomApplicabilityCondition_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_BomApplicabilityCondition_Operator] CHECK ([Operator] BETWEEN 1 AND 4),
        CONSTRAINT [CK_BomApplicabilityCondition_Value] CHECK (DATALENGTH([Value]) > 0),
        CONSTRAINT [FK_BomApplicabilityCondition_Applicability] FOREIGN KEY ([OrganizationId], [BomApplicabilityId]) REFERENCES [organization].[BomApplicability] ([OrganizationId], [Id]),
        CONSTRAINT [FK_BomApplicabilityCondition_Dimension] FOREIGN KEY ([OrganizationId], [ContextDimensionId]) REFERENCES [organization].[ContextDimension] ([OrganizationId], [Id])
    );

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event] CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated'))));

    CREATE INDEX [IX_Bom_Org_PrimaryItem] ON [organization].[Bom] ([OrganizationId], [PrimaryOutputItemId], [IsActive]);
    CREATE UNIQUE INDEX [UX_Bom_Org_Code] ON [organization].[Bom] ([OrganizationId], [Code]);
    CREATE UNIQUE INDEX [UX_BomApplicability_Org_Version] ON [organization].[BomApplicability] ([OrganizationId], [BomVersionId]);
    CREATE INDEX [IX_BomApplicabilityCondition_OrganizationId_BomApplicabilityId] ON [organization].[BomApplicabilityCondition] ([OrganizationId], [BomApplicabilityId]);
    CREATE INDEX [IX_BomApplicabilityCondition_OrganizationId_ContextDimensionId] ON [organization].[BomApplicabilityCondition] ([OrganizationId], [ContextDimensionId]);
    CREATE UNIQUE INDEX [UX_BomApplicabilityCondition_Dim_Op] ON [organization].[BomApplicabilityCondition] ([BomApplicabilityId], [ContextDimensionId], [Operator]);
    CREATE INDEX [IX_BomInput_Org_Ver_Seq] ON [organization].[BomInput] ([OrganizationId], [BomVersionId], [Sequence]);
    CREATE INDEX [IX_BomInput_OrganizationId_ItemId] ON [organization].[BomInput] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_BomInput_OrganizationId_UnitId] ON [organization].[BomInput] ([OrganizationId], [UnitId]);
    CREATE INDEX [IX_BomOutput_Org_Ver_Seq] ON [organization].[BomOutput] ([OrganizationId], [BomVersionId], [Sequence]);
    CREATE INDEX [IX_BomOutput_OrganizationId_ItemId] ON [organization].[BomOutput] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_BomOutput_OrganizationId_UnitId] ON [organization].[BomOutput] ([OrganizationId], [UnitId]);
    CREATE INDEX [IX_BomVersion_OrganizationId_BaseOutputUnitId] ON [organization].[BomVersion] ([OrganizationId], [BaseOutputUnitId]);
    CREATE UNIQUE INDEX [UX_BomVersion_Org_Bom_Ver] ON [organization].[BomVersion] ([OrganizationId], [BomId], [VersionNumber]);
    CREATE UNIQUE INDEX [UX_ContextDimension_Org_Code] ON [organization].[ContextDimension] ([OrganizationId], [Code]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Bom] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[BomVersion] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ContextDimension] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomInput] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomOutput] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomApplicability] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[BomApplicabilityCondition] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Bom] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[BomVersion] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[ContextDimension] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261110173434_ManufacturingBomsAndApplicability')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110173434_ManufacturingBomsAndApplicability', N'10.0.14');
END;

IF OBJECT_ID(N'organization.ProductionOrder', N'U') IS NULL
BEGIN
    IF OBJECT_ID(N'organization.CK_OrganizationChange_Event', N'C') IS NOT NULL
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    CREATE TABLE [organization].[ProductionOrder] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [OrderNumber] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Status] tinyint NOT NULL,
        [ItemId] bigint NOT NULL,
        [RequestedQuantity] decimal(18,6) NOT NULL,
        [RequestedUnitId] bigint NOT NULL,
        [PlannedStartDate] date NOT NULL,
        [PlannedDueDate] date NOT NULL,
        [BomId] bigint NOT NULL,
        [BomCodeSnapshot] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [BomVersionId] bigint NOT NULL,
        [BomVersionNumberSnapshot] int NOT NULL,
        [BaseOutputQuantitySnapshot] decimal(18,6) NOT NULL,
        [BaseOutputUnitIdSnapshot] bigint NOT NULL,
        [ActualProducedQuantity] decimal(18,6) NOT NULL,
        [ActualScrapQuantity] decimal(18,6) NOT NULL,
        [Notes] nvarchar(500) NULL,
        [CancellationReason] nvarchar(500) NULL,
        [ReleasedAtUtc] datetime2(7) NULL,
        [StartedAtUtc] datetime2(7) NULL,
        [CompletedAtUtc] datetime2(7) NULL,
        [CancelledAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ProductionOrder] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_ProductionOrder_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_ProductionOrder_Actuals] CHECK ([ActualProducedQuantity] >= 0 AND [ActualScrapQuantity] >= 0),
        CONSTRAINT [CK_ProductionOrder_BaseOutputQty] CHECK ([BaseOutputQuantitySnapshot] > 0 AND [BomVersionNumberSnapshot] > 0),
        CONSTRAINT [CK_ProductionOrder_BomCodeSnapshot] CHECK (DATALENGTH([BomCodeSnapshot]) BETWEEN 1 AND 32 AND DATALENGTH([BomCodeSnapshot]) = LEN([BomCodeSnapshot]) AND [BomCodeSnapshot] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([BomCodeSnapshot], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_ProductionOrder_Dates] CHECK ([PlannedDueDate] >= [PlannedStartDate]),
        CONSTRAINT [CK_ProductionOrder_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ProductionOrder_Number] CHECK (DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_ProductionOrder_RequestedQty] CHECK ([RequestedQuantity] > 0),
        CONSTRAINT [CK_ProductionOrder_Status] CHECK ([Status] BETWEEN 1 AND 5),
        CONSTRAINT [FK_ProductionOrder_BaseOutputUnitSnapshot] FOREIGN KEY ([OrganizationId], [BaseOutputUnitIdSnapshot]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrder_Bom] FOREIGN KEY ([OrganizationId], [BomId]) REFERENCES [organization].[Bom] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrder_BomVersion] FOREIGN KEY ([OrganizationId], [BomVersionId]) REFERENCES [organization].[BomVersion] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrder_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrder_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_ProductionOrder_RequestedUnit] FOREIGN KEY ([OrganizationId], [RequestedUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrder_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ProductionOrderContext] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [ProductionOrderId] bigint NOT NULL,
        [DimensionCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [DimensionValue] nvarchar(400) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ProductionOrderContext] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductionOrderContext_DimCode] CHECK (DATALENGTH([DimensionCode]) BETWEEN 1 AND 32 AND DATALENGTH([DimensionCode]) = LEN([DimensionCode]) AND [DimensionCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DimensionCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_ProductionOrderContext_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ProductionOrderContext_Value] CHECK (DATALENGTH([DimensionValue]) > 0),
        CONSTRAINT [FK_ProductionOrderContext_Order] FOREIGN KEY ([OrganizationId], [ProductionOrderId]) REFERENCES [organization].[ProductionOrder] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ProductionOrderInput] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [ProductionOrderId] bigint NOT NULL,
        [Level] int NOT NULL,
        [Sequence] int NOT NULL,
        [ParentItemId] bigint NOT NULL,
        [ItemId] bigint NOT NULL,
        [NetQuantity] decimal(18,6) NOT NULL,
        [ScrapPercentage] decimal(5,2) NOT NULL,
        [GrossQuantity] decimal(18,6) NOT NULL,
        [UnitId] bigint NOT NULL,
        [IsOptional] bit NOT NULL,
        [IsSubAssembly] bit NOT NULL,
        [ResolvedSubBomVersionId] bigint NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ProductionOrderInput] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductionOrderInput_LevelSeq] CHECK ([Level] >= 1 AND [Sequence] >= 1),
        CONSTRAINT [CK_ProductionOrderInput_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ProductionOrderInput_Quantities] CHECK ([NetQuantity] > 0 AND [GrossQuantity] > 0),
        CONSTRAINT [CK_ProductionOrderInput_Scrap] CHECK ([ScrapPercentage] >= 0 AND [ScrapPercentage] < 100),
        CONSTRAINT [FK_ProductionOrderInput_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderInput_Order] FOREIGN KEY ([OrganizationId], [ProductionOrderId]) REFERENCES [organization].[ProductionOrder] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderInput_ParentItem] FOREIGN KEY ([OrganizationId], [ParentItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderInput_SubBomVersion] FOREIGN KEY ([OrganizationId], [ResolvedSubBomVersionId]) REFERENCES [organization].[BomVersion] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderInput_Unit] FOREIGN KEY ([OrganizationId], [UnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[ProductionOrderOutput] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [ProductionOrderId] bigint NOT NULL,
        [Level] int NOT NULL,
        [Sequence] int NOT NULL,
        [ItemId] bigint NOT NULL,
        [OutputType] tinyint NOT NULL,
        [PlannedQuantity] decimal(18,6) NOT NULL,
        [UnitId] bigint NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ProductionOrderOutput] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductionOrderOutput_LevelSeq] CHECK ([Level] >= 1 AND [Sequence] >= 1),
        CONSTRAINT [CK_ProductionOrderOutput_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ProductionOrderOutput_Qty] CHECK ([PlannedQuantity] > 0),
        CONSTRAINT [CK_ProductionOrderOutput_Type] CHECK ([OutputType] BETWEEN 1 AND 4),
        CONSTRAINT [FK_ProductionOrderOutput_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderOutput_Order] FOREIGN KEY ([OrganizationId], [ProductionOrderId]) REFERENCES [organization].[ProductionOrder] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderOutput_Unit] FOREIGN KEY ([OrganizationId], [UnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id])
    );

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event] CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled'))));

    CREATE INDEX [IX_ProductionOrder_Org_BomVersion] ON [organization].[ProductionOrder] ([OrganizationId], [BomVersionId]);
    CREATE INDEX [IX_ProductionOrder_Org_Site_Status_Date] ON [organization].[ProductionOrder] ([OrganizationId], [SiteId], [Status], [PlannedStartDate]);
    CREATE INDEX [IX_ProductionOrder_OrganizationId_BaseOutputUnitIdSnapshot] ON [organization].[ProductionOrder] ([OrganizationId], [BaseOutputUnitIdSnapshot]);
    CREATE INDEX [IX_ProductionOrder_OrganizationId_BomId] ON [organization].[ProductionOrder] ([OrganizationId], [BomId]);
    CREATE INDEX [IX_ProductionOrder_OrganizationId_ItemId] ON [organization].[ProductionOrder] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_ProductionOrder_OrganizationId_RequestedUnitId] ON [organization].[ProductionOrder] ([OrganizationId], [RequestedUnitId]);
    CREATE UNIQUE INDEX [UX_ProductionOrder_Org_Number] ON [organization].[ProductionOrder] ([OrganizationId], [OrderNumber]);

    CREATE INDEX [IX_ProductionOrderContext_OrganizationId_ProductionOrderId] ON [organization].[ProductionOrderContext] ([OrganizationId], [ProductionOrderId]);
    CREATE UNIQUE INDEX [UX_ProductionOrderContext_Order_Dim] ON [organization].[ProductionOrderContext] ([ProductionOrderId], [DimensionCode]);

    CREATE INDEX [IX_ProductionOrderInput_Org_Order_LvlSeq] ON [organization].[ProductionOrderInput] ([OrganizationId], [ProductionOrderId], [Level], [Sequence]);
    CREATE INDEX [IX_ProductionOrderInput_OrganizationId_ItemId] ON [organization].[ProductionOrderInput] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_ProductionOrderInput_OrganizationId_ParentItemId] ON [organization].[ProductionOrderInput] ([OrganizationId], [ParentItemId]);
    CREATE INDEX [IX_ProductionOrderInput_OrganizationId_ResolvedSubBomVersionId] ON [organization].[ProductionOrderInput] ([OrganizationId], [ResolvedSubBomVersionId]);
    CREATE INDEX [IX_ProductionOrderInput_OrganizationId_UnitId] ON [organization].[ProductionOrderInput] ([OrganizationId], [UnitId]);

    CREATE INDEX [IX_ProductionOrderOutput_Org_Order_LvlSeq] ON [organization].[ProductionOrderOutput] ([OrganizationId], [ProductionOrderId], [Level], [Sequence]);
    CREATE INDEX [IX_ProductionOrderOutput_OrganizationId_ItemId] ON [organization].[ProductionOrderOutput] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_ProductionOrderOutput_OrganizationId_UnitId] ON [organization].[ProductionOrderOutput] ([OrganizationId], [UnitId]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ProductionOrder] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderInput] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderOutput] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderContext] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[ProductionOrder] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261110194120_ProductionOrdersAndSnapshots')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110194120_ProductionOrdersAndSnapshots', N'10.0.14');
END;

IF OBJECT_ID(N'organization.Warehouse', N'U') IS NULL
BEGIN
    IF OBJECT_ID(N'organization.CK_OrganizationChange_Event', N'C') IS NOT NULL
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    CREATE TABLE [organization].[Warehouse] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsDefaultForSite] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Warehouse] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Warehouse_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Warehouse_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Warehouse_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Warehouse_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_Warehouse_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_Warehouse_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[InventoryBalance] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [ItemId] bigint NOT NULL,
        [LotCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT '',
        [BaseUnitId] bigint NOT NULL,
        [OnHandBaseQuantity] decimal(18,6) NOT NULL,
        [LastMovedAtUtc] datetime2(7) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_InventoryBalance] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_InventoryBalance_LotCode] CHECK (([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))),
        CONSTRAINT [CK_InventoryBalance_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_InventoryBalance_NonNegative] CHECK ([OnHandBaseQuantity] >= 0),
        CONSTRAINT [FK_InventoryBalance_BaseUnit] FOREIGN KEY ([OrganizationId], [BaseUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryBalance_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryBalance_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryBalance_Warehouse] FOREIGN KEY ([OrganizationId], [WarehouseId]) REFERENCES [organization].[Warehouse] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[InventoryTransaction] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [DestinationWarehouseId] bigint NULL,
        [DocumentNumber] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [TransactionType] tinyint NOT NULL,
        [ProductionOrderId] bigint NULL,
        [ReversedTransactionId] bigint NULL,
        [ReferenceNote] nvarchar(500) NULL,
        [IdempotencyKey] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        [PostedAtUtc] datetime2(7) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_InventoryTransaction] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_InventoryTransaction_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_InventoryTransaction_DocNum] CHECK (DATALENGTH([DocumentNumber]) BETWEEN 1 AND 32 AND DATALENGTH([DocumentNumber]) = LEN([DocumentNumber]) AND [DocumentNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DocumentNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_InventoryTransaction_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_InventoryTransaction_Type] CHECK ([TransactionType] BETWEEN 1 AND 8),
        CONSTRAINT [FK_InventoryTransaction_DestWarehouse] FOREIGN KEY ([OrganizationId], [DestinationWarehouseId]) REFERENCES [organization].[Warehouse] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryTransaction_Order] FOREIGN KEY ([OrganizationId], [ProductionOrderId]) REFERENCES [organization].[ProductionOrder] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryTransaction_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_InventoryTransaction_Reversal] FOREIGN KEY ([OrganizationId], [ReversedTransactionId]) REFERENCES [organization].[InventoryTransaction] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryTransaction_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryTransaction_Warehouse] FOREIGN KEY ([OrganizationId], [WarehouseId]) REFERENCES [organization].[Warehouse] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[InventoryLedgerEntry] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [InventoryTransactionId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [LineNumber] int NOT NULL,
        [ItemId] bigint NOT NULL,
        [LotCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT '',
        [EnteredQuantity] decimal(18,6) NOT NULL,
        [EnteredUnitId] bigint NOT NULL,
        [BaseQuantityDelta] decimal(18,6) NOT NULL,
        [BaseUnitId] bigint NOT NULL,
        [OccurredAtUtc] datetime2(7) NOT NULL,
        [Notes] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_InventoryLedgerEntry] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_InventoryLedgerEntry_LotCode] CHECK (([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))),
        CONSTRAINT [CK_InventoryLedgerEntry_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_InventoryLedgerEntry_Qty] CHECK ([EnteredQuantity] <> 0 AND [BaseQuantityDelta] <> 0),
        CONSTRAINT [FK_InventoryLedgerEntry_BaseUnit] FOREIGN KEY ([OrganizationId], [BaseUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryLedgerEntry_EnteredUnit] FOREIGN KEY ([OrganizationId], [EnteredUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryLedgerEntry_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryLedgerEntry_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryLedgerEntry_Txn] FOREIGN KEY ([OrganizationId], [InventoryTransactionId]) REFERENCES [organization].[InventoryTransaction] ([OrganizationId], [Id]),
        CONSTRAINT [FK_InventoryLedgerEntry_Warehouse] FOREIGN KEY ([OrganizationId], [WarehouseId]) REFERENCES [organization].[Warehouse] ([OrganizationId], [Id])
    );

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event] CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed'))));

    CREATE INDEX [IX_InventoryBalance_OrganizationId_BaseUnitId] ON [organization].[InventoryBalance] ([OrganizationId], [BaseUnitId]);
    CREATE INDEX [IX_InventoryBalance_OrganizationId_ItemId] ON [organization].[InventoryBalance] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_InventoryBalance_OrganizationId_SiteId] ON [organization].[InventoryBalance] ([OrganizationId], [SiteId]);
    CREATE UNIQUE INDEX [UX_InventoryBalance_Org_Wh_Item_Lot] ON [organization].[InventoryBalance] ([OrganizationId], [WarehouseId], [ItemId], [LotCode]);

    CREATE INDEX [IX_InventoryLedgerEntry_Org_Wh_Item_Lot] ON [organization].[InventoryLedgerEntry] ([OrganizationId], [WarehouseId], [ItemId], [LotCode], [OccurredAtUtc]);
    CREATE INDEX [IX_InventoryLedgerEntry_OrganizationId_BaseUnitId] ON [organization].[InventoryLedgerEntry] ([OrganizationId], [BaseUnitId]);
    CREATE INDEX [IX_InventoryLedgerEntry_OrganizationId_EnteredUnitId] ON [organization].[InventoryLedgerEntry] ([OrganizationId], [EnteredUnitId]);
    CREATE INDEX [IX_InventoryLedgerEntry_OrganizationId_ItemId] ON [organization].[InventoryLedgerEntry] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_InventoryLedgerEntry_OrganizationId_SiteId] ON [organization].[InventoryLedgerEntry] ([OrganizationId], [SiteId]);
    CREATE UNIQUE INDEX [UX_InventoryLedgerEntry_Org_Txn_Line] ON [organization].[InventoryLedgerEntry] ([OrganizationId], [InventoryTransactionId], [LineNumber]);

    CREATE INDEX [IX_InventoryTransaction_Org_Site_Date] ON [organization].[InventoryTransaction] ([OrganizationId], [SiteId], [PostedAtUtc]);
    CREATE INDEX [IX_InventoryTransaction_OrganizationId_DestinationWarehouseId] ON [organization].[InventoryTransaction] ([OrganizationId], [DestinationWarehouseId]);
    CREATE INDEX [IX_InventoryTransaction_OrganizationId_ProductionOrderId] ON [organization].[InventoryTransaction] ([OrganizationId], [ProductionOrderId]);
    CREATE INDEX [IX_InventoryTransaction_OrganizationId_WarehouseId] ON [organization].[InventoryTransaction] ([OrganizationId], [WarehouseId]);
    CREATE UNIQUE INDEX [UX_InventoryTransaction_Org_DocNum] ON [organization].[InventoryTransaction] ([OrganizationId], [DocumentNumber]);
    CREATE UNIQUE INDEX [UX_InventoryTransaction_Org_Idempotency] ON [organization].[InventoryTransaction] ([OrganizationId], [IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL;
    CREATE UNIQUE INDEX [UX_InventoryTransaction_Org_Reversal] ON [organization].[InventoryTransaction] ([OrganizationId], [ReversedTransactionId]) WHERE [ReversedTransactionId] IS NOT NULL;

    CREATE UNIQUE INDEX [UX_Warehouse_Org_Code] ON [organization].[Warehouse] ([OrganizationId], [Code]);
    CREATE UNIQUE INDEX [UX_Warehouse_Org_Site_Default] ON [organization].[Warehouse] ([OrganizationId], [SiteId]) WHERE [IsDefaultForSite] = 1 AND [IsActive] = 1;

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Warehouse] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[InventoryBalance] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT ON OBJECT::[organization].[InventoryTransaction] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT ON OBJECT::[organization].[InventoryLedgerEntry] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Warehouse] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[InventoryBalance] TO [ForgeFlowOrganizationRuntime];
        DENY UPDATE, DELETE ON OBJECT::[organization].[InventoryTransaction] TO [ForgeFlowOrganizationRuntime];
        DENY UPDATE, DELETE ON OBJECT::[organization].[InventoryLedgerEntry] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261110201924_InventoryWarehousesAndStockLedger')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110201924_InventoryWarehousesAndStockLedger', N'10.0.14');
END;

IF SCHEMA_ID(N'diag') IS NULL EXEC(N'CREATE SCHEMA [diag];');
IF SCHEMA_ID(N'ops') IS NULL EXEC(N'CREATE SCHEMA [ops];');

IF OBJECT_ID(N'diag.ErrorLog', N'U') IS NULL
BEGIN
    CREATE TABLE [diag].[ErrorLog] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [ErrorReference] varchar(32) NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [OccurredAtUtc] datetime2(7) NOT NULL,
        [Severity] tinyint NOT NULL,
        [Source] tinyint NOT NULL,
        [ExceptionType] varchar(256) NOT NULL,
        [SqlErrorNumber] int NULL,
        [RedactedMessage] nvarchar(2000) NOT NULL,
        [RedactedStackTrace] nvarchar(max) NOT NULL,
        [RequestPath] nvarchar(512) NULL,
        [ComponentName] varchar(128) NULL,
        [ActorUserId] bigint NULL,
        [OrganizationId] bigint NULL,
        [SiteId] bigint NULL,
        [UserAgent] nvarchar(256) NULL,
        CONSTRAINT [PK_ErrorLog] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ErrorLog_Severity] CHECK ([Severity] BETWEEN 1 AND 3),
        CONSTRAINT [CK_ErrorLog_Source] CHECK ([Source] BETWEEN 1 AND 5),
        CONSTRAINT [CK_ErrorLog_Ref] CHECK (LEN([ErrorReference]) > 0 AND DATALENGTH([ErrorReference]) = LEN([ErrorReference])),
        CONSTRAINT [CK_ErrorLog_Scope] CHECK ([SiteId] IS NULL OR [OrganizationId] IS NOT NULL)
    );
    CREATE UNIQUE INDEX [UX_ErrorLog_ErrorReference] ON [diag].[ErrorLog] ([ErrorReference]);
    CREATE INDEX [IX_ErrorLog_OccurredAt_Id] ON [diag].[ErrorLog] ([OccurredAtUtc], [Id]);
    CREATE INDEX [IX_ErrorLog_CorrelationId] ON [diag].[ErrorLog] ([CorrelationId]);
    CREATE INDEX [IX_ErrorLog_Severity_Time] ON [diag].[ErrorLog] ([Severity], [OccurredAtUtc]);
END;

IF OBJECT_ID(N'ops.UserOperationLog', N'U') IS NULL
BEGIN
    CREATE TABLE [ops].[UserOperationLog] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OccurredAtUtc] datetime2(7) NOT NULL,
        [OperationCode] varchar(64) NOT NULL,
        [Outcome] tinyint NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [ActorUserId] bigint NULL,
        [OrganizationId] bigint NULL,
        [SiteId] bigint NULL,
        [TargetEntityType] varchar(64) NULL,
        [TargetEntityRef] nvarchar(128) NULL,
        [FailureCode] varchar(128) NULL,
        [MetadataJson] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_UserOperationLog] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserOperationLog_Code] CHECK (LEN([OperationCode]) > 0 AND DATALENGTH([OperationCode]) = LEN([OperationCode])),
        CONSTRAINT [CK_UserOperationLog_Outcome] CHECK ([Outcome] BETWEEN 1 AND 5),
        CONSTRAINT [CK_UserOperationLog_Json] CHECK (ISJSON([MetadataJson]) = 1),
        CONSTRAINT [CK_UserOperationLog_Scope] CHECK ([SiteId] IS NULL OR [OrganizationId] IS NOT NULL)
    );
    CREATE INDEX [IX_UserOperationLog_OccurredAt_Id] ON [ops].[UserOperationLog] ([OccurredAtUtc], [Id]);
    CREATE INDEX [IX_UserOperationLog_Org_Site_Time] ON [ops].[UserOperationLog] ([OrganizationId], [SiteId], [OccurredAtUtc], [Id]);
    CREATE INDEX [IX_UserOperationLog_Actor_Time] ON [ops].[UserOperationLog] ([ActorUserId], [OccurredAtUtc], [Id]);
    CREATE INDEX [IX_UserOperationLog_CorrelationId] ON [ops].[UserOperationLog] ([CorrelationId]);
END;

IF OBJECT_ID(N'diag.__EFMigrationsHistory', N'U') IS NULL
BEGIN
    CREATE TABLE [diag].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;

IF NOT EXISTS (SELECT 1 FROM [diag].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261110210556_DiagnosticsErrorAndOperationLogs')
BEGIN
    INSERT INTO [diag].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261110210556_DiagnosticsErrorAndOperationLogs', N'10.0.14');
END;

IF COL_LENGTH(N'organization.Item', N'RequiresLotTracking') IS NULL
BEGIN
    ALTER TABLE [organization].[Item] ADD [RequiresLotTracking] bit NOT NULL CONSTRAINT [DF_Item_RequiresLotTracking] DEFAULT 0;
    ALTER TABLE [organization].[Item] ADD [QualityControlMode] tinyint NOT NULL CONSTRAINT [DF_Item_QualityControlMode] DEFAULT 0;
    EXEC(N'ALTER TABLE [organization].[Item] ADD CONSTRAINT [CK_Item_QualityControlMode] CHECK ([QualityControlMode] BETWEEN 0 AND 3);');
END;

IF COL_LENGTH(N'organization.InventoryBalance', N'BlockedBaseQuantity') IS NULL
BEGIN
    ALTER TABLE [organization].[InventoryBalance] ADD [BlockedBaseQuantity] decimal(18,6) NOT NULL CONSTRAINT [DF_InventoryBalance_BlockedBaseQuantity] DEFAULT 0;
    EXEC(N'ALTER TABLE [organization].[InventoryBalance] ADD CONSTRAINT [CK_InventoryBalance_Blocked] CHECK ([BlockedBaseQuantity] >= 0 AND [BlockedBaseQuantity] <= [OnHandBaseQuantity]);');
END;

IF OBJECT_ID(N'organization.ItemLot', N'U') IS NULL
BEGIN
    CREATE TABLE [organization].[ItemLot] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [ItemId] bigint NOT NULL,
        [LotCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [ManufactureDate] date NULL,
        [ExpirationDate] date NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ItemLot] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ItemLot_Dates] CHECK ([ManufactureDate] IS NULL OR [ExpirationDate] IS NULL OR [ExpirationDate] >= [ManufactureDate]),
        CONSTRAINT [CK_ItemLot_LotCode] CHECK (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_ItemLot_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [FK_ItemLot_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ItemLot_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );
    CREATE UNIQUE INDEX [UX_ItemLot_Org_Item_Lot] ON [organization].[ItemLot] ([OrganizationId], [ItemId], [LotCode]);
    CREATE INDEX [IX_ItemLot_Org_Expiration] ON [organization].[ItemLot] ([OrganizationId], [ExpirationDate]);
END;

IF OBJECT_ID(N'organization.QualityInspection', N'U') IS NULL
BEGIN
    CREATE TABLE [organization].[QualityInspection] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [ItemId] bigint NOT NULL,
        [LotCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT [DF_QualityInspection_LotCode] DEFAULT '',
        [InspectionNumber] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [SourceType] tinyint NOT NULL,
        [SourceTransactionId] bigint NULL,
        [ProductionOrderId] bigint NULL,
        [BaseUnitId] bigint NOT NULL,
        [InspectedBaseQuantity] decimal(18,6) NOT NULL,
        [AcceptedBaseQuantity] decimal(18,6) NOT NULL,
        [RejectedBaseQuantity] decimal(18,6) NOT NULL,
        [RemainingBlockedBaseQuantity] decimal(18,6) NOT NULL,
        [Status] tinyint NOT NULL,
        [DefectReasonCode] varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        [Notes] nvarchar(1000) NULL,
        [InspectedAtUtc] datetime2(7) NULL,
        [InspectedByActorId] bigint NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_QualityInspection] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_QualityInspection_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_QualityInspection_LotCode] CHECK (([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))),
        CONSTRAINT [CK_QualityInspection_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_QualityInspection_Number] CHECK (DATALENGTH([InspectionNumber]) BETWEEN 1 AND 32 AND DATALENGTH([InspectionNumber]) = LEN([InspectionNumber]) AND [InspectionNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([InspectionNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_QualityInspection_Quantities] CHECK ([InspectedBaseQuantity] > 0 AND [AcceptedBaseQuantity] >= 0 AND [RejectedBaseQuantity] >= 0 AND [RemainingBlockedBaseQuantity] >= 0 AND ([AcceptedBaseQuantity] + [RejectedBaseQuantity] + [RemainingBlockedBaseQuantity]) <= [InspectedBaseQuantity]),
        CONSTRAINT [CK_QualityInspection_SourceType] CHECK ([SourceType] BETWEEN 1 AND 3),
        CONSTRAINT [CK_QualityInspection_Status] CHECK ([Status] BETWEEN 1 AND 4),
        CONSTRAINT [FK_QualityInspection_BaseUnit] FOREIGN KEY ([OrganizationId], [BaseUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_QualityInspection_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_QualityInspection_Order] FOREIGN KEY ([OrganizationId], [ProductionOrderId]) REFERENCES [organization].[ProductionOrder] ([OrganizationId], [Id]),
        CONSTRAINT [FK_QualityInspection_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_QualityInspection_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_QualityInspection_SourceTxn] FOREIGN KEY ([OrganizationId], [SourceTransactionId]) REFERENCES [organization].[InventoryTransaction] ([OrganizationId], [Id]),
        CONSTRAINT [FK_QualityInspection_Warehouse] FOREIGN KEY ([OrganizationId], [WarehouseId]) REFERENCES [organization].[Warehouse] ([OrganizationId], [Id])
    );
    CREATE UNIQUE INDEX [UX_QualityInspection_Org_Number] ON [organization].[QualityInspection] ([OrganizationId], [InspectionNumber]);
    CREATE INDEX [IX_QualityInspection_Org_Site_Status] ON [organization].[QualityInspection] ([OrganizationId], [SiteId], [Status], [Id]);
    CREATE INDEX [IX_QualityInspection_Org_Item_Lot] ON [organization].[QualityInspection] ([OrganizationId], [ItemId], [LotCode]);
    CREATE INDEX [IX_QualityInspection_OrganizationId_BaseUnitId] ON [organization].[QualityInspection] ([OrganizationId], [BaseUnitId]);
    CREATE INDEX [IX_QualityInspection_OrganizationId_ProductionOrderId] ON [organization].[QualityInspection] ([OrganizationId], [ProductionOrderId]);
    CREATE INDEX [IX_QualityInspection_OrganizationId_SourceTransactionId] ON [organization].[QualityInspection] ([OrganizationId], [SourceTransactionId]);
    CREATE INDEX [IX_QualityInspection_OrganizationId_WarehouseId] ON [organization].[QualityInspection] ([OrganizationId], [WarehouseId]);
END;

IF OBJECT_ID(N'organization.QualityInspectionLine', N'U') IS NULL
BEGIN
    CREATE TABLE [organization].[QualityInspectionLine] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [QualityInspectionId] bigint NOT NULL,
        [LineNumber] int NOT NULL,
        [ParameterName] nvarchar(100) NOT NULL,
        [ExpectedSpec] nvarchar(100) NULL,
        [MeasuredValue] nvarchar(100) NULL,
        [IsPassed] bit NOT NULL,
        [Notes] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_QualityInspectionLine] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_QualityInspectionLine_LineNum] CHECK ([LineNumber] > 0),
        CONSTRAINT [CK_QualityInspectionLine_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_QualityInspectionLine_Param] CHECK (DATALENGTH(LTRIM(RTRIM([ParameterName]))) > 0),
        CONSTRAINT [FK_QualityInspectionLine_Inspection] FOREIGN KEY ([OrganizationId], [QualityInspectionId]) REFERENCES [organization].[QualityInspection] ([OrganizationId], [Id])
    );
    CREATE UNIQUE INDEX [UX_QualityInspectionLine_Org_Insp_Line] ON [organization].[QualityInspectionLine] ([OrganizationId], [QualityInspectionId], [LineNumber]);
END;

IF COL_LENGTH(N'organization.InventoryTransaction', N'QualityInspectionId') IS NULL
BEGIN
    ALTER TABLE [organization].[InventoryTransaction] ADD [QualityInspectionId] bigint NULL;
    EXEC(N'ALTER TABLE [organization].[InventoryTransaction] ADD CONSTRAINT [FK_InventoryTransaction_QualityInspection] FOREIGN KEY ([OrganizationId], [QualityInspectionId]) REFERENCES [organization].[QualityInspection] ([OrganizationId], [Id]);');
    EXEC(N'CREATE INDEX [IX_InventoryTransaction_OrganizationId_QualityInspectionId] ON [organization].[InventoryTransaction] ([OrganizationId], [QualityInspectionId]);');
END;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_InventoryTransaction_Type' AND parent_object_id = OBJECT_ID(N'organization.InventoryTransaction'))
    ALTER TABLE [organization].[InventoryTransaction] DROP CONSTRAINT [CK_InventoryTransaction_Type];
ALTER TABLE [organization].[InventoryTransaction] ADD CONSTRAINT [CK_InventoryTransaction_Type] CHECK ([TransactionType] BETWEEN 1 AND 9);

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrganizationChange_Event' AND parent_object_id = OBJECT_ID(N'organization.OrganizationChange'))
    ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];
ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event] CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled'))));

IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[ItemLot] TO [ForgeFlowOrganizationRuntime];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[QualityInspection] TO [ForgeFlowOrganizationRuntime];
    GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[QualityInspectionLine] TO [ForgeFlowOrganizationRuntime];
    DENY DELETE ON OBJECT::[organization].[ItemLot] TO [ForgeFlowOrganizationRuntime];
    DENY DELETE ON OBJECT::[organization].[QualityInspection] TO [ForgeFlowOrganizationRuntime];
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111041945_QualityControlAndLotTraceability')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111041945_QualityControlAndLotTraceability', N'10.0.14');
END;

IF COL_LENGTH(N'auth.User', N'DisplayName') IS NULL
BEGIN
    ALTER TABLE [auth].[User] ADD [DisplayName] nvarchar(128) NULL;
END;

IF COL_LENGTH(N'auth.User', N'AvatarContentType') IS NULL
BEGIN
    ALTER TABLE [auth].[User] ADD [AvatarContentType] varchar(32) NULL;
END;

IF COL_LENGTH(N'auth.User', N'AvatarData') IS NULL
BEGIN
    ALTER TABLE [auth].[User] ADD [AvatarData] varbinary(max) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_User_Avatar' AND parent_object_id = OBJECT_ID(N'auth.User'))
BEGIN
    EXEC(N'ALTER TABLE [auth].[User] ADD CONSTRAINT [CK_User_Avatar] CHECK (([AvatarContentType] IS NULL AND [AvatarData] IS NULL) OR ([AvatarContentType] IN (''image/png'',''image/jpeg'',''image/webp'') AND [AvatarData] IS NOT NULL AND DATALENGTH([AvatarData]) BETWEEN 1 AND 262144));');
END;

IF OBJECT_ID(N'auth.UserPreference', N'U') IS NULL
BEGIN
    CREATE TABLE [auth].[UserPreference] (
        [UserId] bigint NOT NULL,
        [Language] varchar(5) NOT NULL,
        [Theme] varchar(16) NOT NULL,
        [CalendarSystem] varchar(16) NOT NULL,
        [TimeZoneId] varchar(64) NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UserPreference] PRIMARY KEY ([UserId]),
        CONSTRAINT [CK_UserPreference_Calendar] CHECK ([CalendarSystem] COLLATE Latin1_General_100_BIN2 IN ('persian','gregorian')),
        CONSTRAINT [CK_UserPreference_Language] CHECK (DATALENGTH([Language]) = 5 AND [Language] COLLATE Latin1_General_100_BIN2 IN ('fa-IR','en-US')),
        CONSTRAINT [CK_UserPreference_Metadata] CHECK ([UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]),
        CONSTRAINT [CK_UserPreference_Theme] CHECK ([Theme] COLLATE Latin1_General_100_BIN2 IN ('light','dark','system')),
        CONSTRAINT [CK_UserPreference_TimeZone] CHECK (LEN([TimeZoneId]) BETWEEN 1 AND 64),
        CONSTRAINT [FK_UserPreference_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF OBJECT_ID(N'auth.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [auth].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111045515_UserProfileAndPreferences')
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111045515_UserProfileAndPreferences', N'10.0.14');
END;

IF COL_LENGTH(N'auth.UserPreference', N'ActiveOrganizationId') IS NULL
BEGIN
    ALTER TABLE [auth].[UserPreference] ADD [ActiveOrganizationId] bigint NULL;
END;

IF COL_LENGTH(N'auth.UserPreference', N'ActiveSiteId') IS NULL
BEGIN
    ALTER TABLE [auth].[UserPreference] ADD [ActiveSiteId] bigint NULL;
END;

IF COL_LENGTH(N'auth.Session', N'DeviceSummary') IS NULL
BEGIN
    ALTER TABLE [auth].[Session] ADD [DeviceSummary] nvarchar(128) NULL;
END;

IF COL_LENGTH(N'auth.Session', N'IpAddress') IS NULL
BEGIN
    ALTER TABLE [auth].[Session] ADD [IpAddress] varchar(64) NULL;
END;

IF COL_LENGTH(N'auth.Session', N'IsMobileDevice') IS NULL
BEGIN
    ALTER TABLE [auth].[Session] ADD [IsMobileDevice] bit NOT NULL CONSTRAINT [DF_Session_IsMobileDevice_Tmp] DEFAULT 0;
END;

IF COL_LENGTH(N'auth.Session', N'LastActivityAtUtc') IS NULL
BEGIN
    ALTER TABLE [auth].[Session] ADD [LastActivityAtUtc] datetime2(7) NOT NULL CONSTRAINT [DF_Session_LastActivityAtUtc_Tmp] DEFAULT '0001-01-01T00:00:00.0000000';
    EXEC(N'UPDATE [auth].[Session] SET [LastActivityAtUtc] = [IssuedAtUtc];');
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserPreference_ActiveOrganizationId_ActiveSiteId' AND object_id = OBJECT_ID(N'auth.UserPreference'))
    EXEC(N'CREATE INDEX [IX_UserPreference_ActiveOrganizationId_ActiveSiteId] ON [auth].[UserPreference] ([ActiveOrganizationId], [ActiveSiteId]);');

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_UserPreference_ActiveScope' AND parent_object_id = OBJECT_ID(N'auth.UserPreference'))
    EXEC(N'ALTER TABLE [auth].[UserPreference] ADD CONSTRAINT [CK_UserPreference_ActiveScope] CHECK (([ActiveSiteId] IS NULL OR [ActiveOrganizationId] IS NOT NULL));');

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_UserPreference_Organization_ActiveOrganizationId')
    EXEC(N'ALTER TABLE [auth].[UserPreference] ADD CONSTRAINT [FK_UserPreference_Organization_ActiveOrganizationId] FOREIGN KEY ([ActiveOrganizationId]) REFERENCES [organization].[Organization] ([Id]);');

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_UserPreference_Site_ActiveOrganizationId_ActiveSiteId')
    EXEC(N'ALTER TABLE [auth].[UserPreference] ADD CONSTRAINT [FK_UserPreference_Site_ActiveOrganizationId_ActiveSiteId] FOREIGN KEY ([ActiveOrganizationId], [ActiveSiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]);');

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Session_Lifecycle' AND parent_object_id = OBJECT_ID(N'auth.Session'))
    ALTER TABLE [auth].[Session] DROP CONSTRAINT [CK_Session_Lifecycle];
EXEC(N'ALTER TABLE [auth].[Session] ADD CONSTRAINT [CK_Session_Lifecycle] CHECK ([ExpiresAtUtc] > [IssuedAtUtc] AND [LastActivityAtUtc] >= [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN (''Logout'',''PasswordReset'',''AccountDisabled'',''AccountLocked'',''StampChanged'',''SessionRevokedByUser''))));');

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_SecurityEvent_Code' AND parent_object_id = OBJECT_ID(N'auth.SecurityEvent'))
    ALTER TABLE [auth].[SecurityEvent] DROP CONSTRAINT [CK_SecurityEvent_Code];
ALTER TABLE [auth].[SecurityEvent] ADD CONSTRAINT [CK_SecurityEvent_Code] CHECK (DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated','SessionRevokedByUser','OtherSessionsRevoked','ActiveSiteSwitched'));

IF OBJECT_ID(N'auth.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [auth].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111052007_SessionsAndActiveSiteContext')
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111052007_SessionsAndActiveSiteContext', N'10.0.14');
END;

IF OBJECT_ID(N'auth.UserNotification', N'U') IS NULL
BEGIN
    CREATE TABLE [auth].[UserNotification] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [UserId] bigint NOT NULL,
        [Category] tinyint NOT NULL,
        [Severity] tinyint NOT NULL,
        [TemplateCode] varchar(64) NOT NULL,
        [ParamsJson] nvarchar(1000) NOT NULL,
        [LinkUrl] nvarchar(256) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [ReadAtUtc] datetime2(7) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UserNotification] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserNotification_Category] CHECK ([Category] IN (1, 2)),
        CONSTRAINT [CK_UserNotification_Params] CHECK (ISJSON([ParamsJson]) = 1),
        CONSTRAINT [CK_UserNotification_ReadAt] CHECK ([ReadAtUtc] IS NULL OR [ReadAtUtc] >= [CreatedAtUtc]),
        CONSTRAINT [CK_UserNotification_Severity] CHECK ([Severity] IN (1, 2, 3, 4)),
        CONSTRAINT [CK_UserNotification_Template] CHECK (DATALENGTH([TemplateCode])=LEN([TemplateCode]) AND [TemplateCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempts','RoleOrAccessChanged','MyProductionOrderStatusChanged','PasswordChangedAlert')),
        CONSTRAINT [FK_UserNotification_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );

    CREATE INDEX [IX_UserNotification_UserId_ReadAtUtc_CreatedAtUtc_Id]
        ON [auth].[UserNotification] ([UserId], [ReadAtUtc], [CreatedAtUtc], [Id]);
END;

IF OBJECT_ID(N'auth.UserNotificationPreference', N'U') IS NULL
BEGIN
    CREATE TABLE [auth].[UserNotificationPreference] (
        [UserId] bigint NOT NULL,
        [EventCode] varchar(64) NOT NULL,
        [InAppEnabled] bit NOT NULL,
        [EmailEnabled] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_UserNotificationPreference] PRIMARY KEY ([UserId], [EventCode]),
        CONSTRAINT [CK_UserNotificationPreference_EventCode] CHECK (DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempt','RoleOrAccessChanged','MyProductionOrders')),
        CONSTRAINT [CK_UserNotificationPreference_Metadata] CHECK ([UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]),
        CONSTRAINT [FK_UserNotificationPreference_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF DATABASE_PRINCIPAL_ID(N'ForgeFlowAuthRuntime') IS NOT NULL
BEGIN
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserPreference] TO [ForgeFlowAuthRuntime];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserNotificationPreference] TO [ForgeFlowAuthRuntime];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserNotification] TO [ForgeFlowAuthRuntime];
    DENY DELETE ON OBJECT::[auth].[UserPreference] TO [ForgeFlowAuthRuntime];
    DENY DELETE ON OBJECT::[auth].[UserNotificationPreference] TO [ForgeFlowAuthRuntime];
    DENY DELETE ON OBJECT::[auth].[UserNotification] TO [ForgeFlowAuthRuntime];
END;

IF OBJECT_ID(N'auth.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [auth].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111054035_UserNotificationsAndAlerts')
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111054035_UserNotificationsAndAlerts', N'10.0.14');
END;

IF COL_LENGTH(N'auth.Session', N'DeviceTokenHash') IS NULL
    ALTER TABLE [auth].[Session] ADD [DeviceTokenHash] varchar(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Session_UserId_DeviceTokenHash' AND object_id = OBJECT_ID(N'auth.Session'))
    CREATE INDEX [IX_Session_UserId_DeviceTokenHash] ON [auth].[Session] ([UserId], [DeviceTokenHash]);

IF OBJECT_ID(N'auth.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [auth].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111061951_SessionDeviceTokenHash')
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111061951_SessionDeviceTokenHash', N'10.0.14');
END;

IF OBJECT_ID(N'organization.Supplier', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrganizationChange_Event' AND parent_object_id = OBJECT_ID(N'organization.OrganizationChange'))
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    IF COL_LENGTH(N'organization.InventoryTransaction', N'PurchaseOrderId') IS NULL
        ALTER TABLE [organization].[InventoryTransaction] ADD [PurchaseOrderId] bigint NULL;

    CREATE TABLE [organization].[Supplier] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [ContactName] nvarchar(150) NULL,
        [Phone] varchar(64) NULL,
        [Email] nvarchar(256) NULL,
        [Address] nvarchar(500) NULL,
        [Notes] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Supplier] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Supplier_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Supplier_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Supplier_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Supplier_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_Supplier_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );

    CREATE TABLE [organization].[PurchaseOrder] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [SupplierId] bigint NOT NULL,
        [OrderNumber] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Status] tinyint NOT NULL,
        [OrderDate] date NOT NULL,
        [ExpectedDeliveryDate] date NULL,
        [TargetWarehouseId] bigint NULL,
        [CurrencyCode] varchar(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Notes] nvarchar(500) NULL,
        [CancellationReason] nvarchar(500) NULL,
        [ApprovedAtUtc] datetime2(7) NULL,
        [ClosedAtUtc] datetime2(7) NULL,
        [CancelledAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_PurchaseOrder] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_PurchaseOrder_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_PurchaseOrder_Currency] CHECK (DATALENGTH([CurrencyCode]) = 3 AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 IN ('IRR','USD','EUR')),
        CONSTRAINT [CK_PurchaseOrder_Dates] CHECK ([ExpectedDeliveryDate] IS NULL OR [ExpectedDeliveryDate] >= [OrderDate]),
        CONSTRAINT [CK_PurchaseOrder_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_PurchaseOrder_Number] CHECK (DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_PurchaseOrder_Status] CHECK ([Status] BETWEEN 1 AND 6),
        CONSTRAINT [FK_PurchaseOrder_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_PurchaseOrder_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_PurchaseOrder_Supplier] FOREIGN KEY ([OrganizationId], [SupplierId]) REFERENCES [organization].[Supplier] ([OrganizationId], [Id]),
        CONSTRAINT [FK_PurchaseOrder_TargetWarehouse] FOREIGN KEY ([OrganizationId], [TargetWarehouseId]) REFERENCES [organization].[Warehouse] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[PurchaseOrderLine] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [PurchaseOrderId] bigint NOT NULL,
        [LineNumber] int NOT NULL,
        [ItemId] bigint NOT NULL,
        [OrderedQuantity] decimal(18,6) NOT NULL,
        [OrderedUnitId] bigint NOT NULL,
        [BaseUnitIdSnapshot] bigint NOT NULL,
        [OrderedBaseQuantity] decimal(18,6) NOT NULL,
        [ReceivedBaseQuantity] decimal(18,6) NOT NULL,
        [UnitPrice] decimal(18,4) NULL,
        [Notes] nvarchar(300) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_PurchaseOrderLine] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PurchaseOrderLine_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_PurchaseOrderLine_Num] CHECK ([LineNumber] > 0),
        CONSTRAINT [CK_PurchaseOrderLine_Price] CHECK ([UnitPrice] IS NULL OR [UnitPrice] >= 0),
        CONSTRAINT [CK_PurchaseOrderLine_Qty] CHECK ([OrderedQuantity] > 0 AND [OrderedBaseQuantity] > 0 AND [ReceivedBaseQuantity] >= 0 AND [ReceivedBaseQuantity] <= [OrderedBaseQuantity]),
        CONSTRAINT [FK_PurchaseOrderLine_BaseUnitSnapshot] FOREIGN KEY ([OrganizationId], [BaseUnitIdSnapshot]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_PurchaseOrderLine_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_PurchaseOrderLine_Order] FOREIGN KEY ([OrganizationId], [PurchaseOrderId]) REFERENCES [organization].[PurchaseOrder] ([OrganizationId], [Id]),
        CONSTRAINT [FK_PurchaseOrderLine_OrderedUnit] FOREIGN KEY ([OrganizationId], [OrderedUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id])
    );

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event]
        CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled'))));

    CREATE INDEX [IX_InventoryTransaction_Org_PurchaseOrder] ON [organization].[InventoryTransaction] ([OrganizationId], [PurchaseOrderId]);
    CREATE INDEX [IX_PurchaseOrder_Org_Site_Status] ON [organization].[PurchaseOrder] ([OrganizationId], [SiteId], [Status], [OrderDate]);
    CREATE INDEX [IX_PurchaseOrder_OrganizationId_SupplierId] ON [organization].[PurchaseOrder] ([OrganizationId], [SupplierId]);
    CREATE INDEX [IX_PurchaseOrder_OrganizationId_TargetWarehouseId] ON [organization].[PurchaseOrder] ([OrganizationId], [TargetWarehouseId]);
    CREATE UNIQUE INDEX [UX_PurchaseOrder_Org_Number] ON [organization].[PurchaseOrder] ([OrganizationId], [OrderNumber]);
    CREATE INDEX [IX_PurchaseOrderLine_Org_Item] ON [organization].[PurchaseOrderLine] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_PurchaseOrderLine_OrganizationId_BaseUnitIdSnapshot] ON [organization].[PurchaseOrderLine] ([OrganizationId], [BaseUnitIdSnapshot]);
    CREATE INDEX [IX_PurchaseOrderLine_OrganizationId_OrderedUnitId] ON [organization].[PurchaseOrderLine] ([OrganizationId], [OrderedUnitId]);
    CREATE UNIQUE INDEX [UX_PurchaseOrderLine_Org_Order_Line] ON [organization].[PurchaseOrderLine] ([OrganizationId], [PurchaseOrderId], [LineNumber]);
    CREATE INDEX [IX_Supplier_Org_IsActive_Code] ON [organization].[Supplier] ([OrganizationId], [IsActive], [Code], [Id]);
    CREATE UNIQUE INDEX [UX_Supplier_Org_Code] ON [organization].[Supplier] ([OrganizationId], [Code]);

    ALTER TABLE [organization].[InventoryTransaction] ADD CONSTRAINT [FK_InventoryTransaction_PurchaseOrder]
        FOREIGN KEY ([OrganizationId], [PurchaseOrderId]) REFERENCES [organization].[PurchaseOrder] ([OrganizationId], [Id]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Supplier] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[PurchaseOrder] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[PurchaseOrderLine] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Supplier] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[PurchaseOrder] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111065922_ProcurementSuppliersAndPurchaseOrders')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111065922_ProcurementSuppliersAndPurchaseOrders', N'10.0.14');
END;

IF OBJECT_ID(N'organization.Currency', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_PurchaseOrder_Currency' AND parent_object_id = OBJECT_ID(N'organization.PurchaseOrder'))
        ALTER TABLE [organization].[PurchaseOrder] DROP CONSTRAINT [CK_PurchaseOrder_Currency];

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrganizationChange_Event' AND parent_object_id = OBJECT_ID(N'organization.OrganizationChange'))
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    CREATE TABLE [organization].[Currency] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [Code] varchar(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Symbol] nvarchar(32) NOT NULL,
        [PrecisionScale] tinyint NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Currency] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Currency_Organization_Code] UNIQUE ([OrganizationId], [Code]),
        CONSTRAINT [AK_Currency_Organization_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Currency_Code] CHECK (DATALENGTH([Code]) = 3 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'),
        CONSTRAINT [CK_Currency_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Currency_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [CK_Currency_PrecisionScale] CHECK ([PrecisionScale] BETWEEN 0 AND 6),
        CONSTRAINT [CK_Currency_Symbol] CHECK (DATALENGTH([Symbol]) BETWEEN 2 AND 64 AND DATALENGTH([Symbol]) = DATALENGTH(LTRIM(RTRIM([Symbol]))) AND DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0),
        CONSTRAINT [FK_Currency_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );

    INSERT INTO [organization].[Currency] ([OrganizationId], [Code], [Name], [Symbol], [PrecisionScale], [IsActive], [CreatedAtUtc], [CreatedByActorId])
    SELECT [o].[Id], [seed].[Code], [seed].[Name], [seed].[Symbol], [seed].[PrecisionScale], 1, SYSUTCDATETIME(), [o].[CreatedByActorId]
    FROM [organization].[Organization] AS [o]
    CROSS JOIN (VALUES
        ('IRR', N'Iranian Rial', N'IRR', CAST(0 AS tinyint)),
        ('USD', N'US Dollar', N'$', CAST(2 AS tinyint)),
        ('EUR', N'Euro', N'€', CAST(2 AS tinyint))
    ) AS [seed]([Code], [Name], [Symbol], [PrecisionScale]);

    ALTER TABLE [organization].[PurchaseOrder] ADD CONSTRAINT [CK_PurchaseOrder_Currency]
        CHECK (DATALENGTH([CurrencyCode]) = 3 AND DATALENGTH([CurrencyCode]) = LEN([CurrencyCode]) AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%');

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event]
        CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled'))));

    CREATE INDEX [IX_PurchaseOrder_OrganizationId_CurrencyCode] ON [organization].[PurchaseOrder] ([OrganizationId], [CurrencyCode]);
    CREATE INDEX [IX_Currency_Organization_IsActive_Code] ON [organization].[Currency] ([OrganizationId], [IsActive], [Code], [Id]);
    CREATE UNIQUE INDEX [UX_Currency_Organization_Code] ON [organization].[Currency] ([OrganizationId], [Code]);

    ALTER TABLE [organization].[PurchaseOrder] ADD CONSTRAINT [FK_PurchaseOrder_Currency]
        FOREIGN KEY ([OrganizationId], [CurrencyCode]) REFERENCES [organization].[Currency] ([OrganizationId], [Code]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Currency] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Currency] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111094413_CurrenciesMasterData')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111094413_CurrenciesMasterData', N'10.0.14');
END;

IF OBJECT_ID(N'organization.WorkCenter', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrganizationChange_Event' AND parent_object_id = OBJECT_ID(N'organization.OrganizationChange'))
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    IF COL_LENGTH(N'organization.ProductionOrder', N'RoutingCodeSnapshot') IS NULL
        ALTER TABLE [organization].[ProductionOrder] ADD [RoutingCodeSnapshot] varchar(32) COLLATE Latin1_General_100_BIN2 NULL;

    IF COL_LENGTH(N'organization.ProductionOrder', N'RoutingId') IS NULL
        ALTER TABLE [organization].[ProductionOrder] ADD [RoutingId] bigint NULL;

    CREATE TABLE [organization].[WorkCenter] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [WorkCenterType] tinyint NOT NULL,
        [CapacityUnitsPerHour] decimal(18,6) NOT NULL,
        [DailyAvailableHours] decimal(18,6) NOT NULL,
        [HourlyCostRate] decimal(18,6) NULL,
        [Notes] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_WorkCenter] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_WorkCenter_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [AK_WorkCenter_Org_Site_Id] UNIQUE ([OrganizationId], [SiteId], [Id]),
        CONSTRAINT [CK_WorkCenter_Capacity] CHECK ([CapacityUnitsPerHour] > 0 AND [DailyAvailableHours] > 0 AND [DailyAvailableHours] <= 24 AND ([HourlyCostRate] IS NULL OR [HourlyCostRate] >= 0)),
        CONSTRAINT [CK_WorkCenter_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_WorkCenter_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_WorkCenter_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [CK_WorkCenter_Type] CHECK ([WorkCenterType] BETWEEN 1 AND 5),
        CONSTRAINT [FK_WorkCenter_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_WorkCenter_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[Routing] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [ItemId] bigint NOT NULL,
        [BomVersionId] bigint NULL,
        [VersionNumber] int NOT NULL,
        [Status] tinyint NOT NULL,
        [IsDefault] bit NOT NULL,
        [Notes] nvarchar(500) NULL,
        [ActivatedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Routing] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Routing_Org_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [AK_Routing_Org_Site_Id] UNIQUE ([OrganizationId], [SiteId], [Id]),
        CONSTRAINT [CK_Routing_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Routing_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Routing_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [CK_Routing_Status] CHECK ([Status] BETWEEN 1 AND 3),
        CONSTRAINT [CK_Routing_Version] CHECK ([VersionNumber] >= 1),
        CONSTRAINT [FK_Routing_BomVersion] FOREIGN KEY ([OrganizationId], [BomVersionId]) REFERENCES [organization].[BomVersion] ([OrganizationId], [Id]),
        CONSTRAINT [FK_Routing_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_Routing_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_Routing_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id])
    );

    CREATE TABLE [organization].[RoutingOperation] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [RoutingId] bigint NOT NULL,
        [Sequence] int NOT NULL,
        [OperationCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [OperationName] nvarchar(200) NOT NULL,
        [WorkCenterId] bigint NOT NULL,
        [SetupMinutes] decimal(18,2) NOT NULL,
        [RunMinutesPerUnit] decimal(18,4) NOT NULL,
        [QueueMinutes] decimal(18,2) NOT NULL,
        [WaitMinutes] decimal(18,2) NOT NULL,
        [RequiresQualityCheck] bit NOT NULL,
        [Instructions] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_RoutingOperation] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RoutingOperation_Code] CHECK (DATALENGTH([OperationCode]) BETWEEN 1 AND 32 AND DATALENGTH([OperationCode]) = LEN([OperationCode]) AND [OperationCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OperationCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_RoutingOperation_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_RoutingOperation_Name] CHECK (DATALENGTH([OperationName]) BETWEEN 2 AND 400 AND DATALENGTH([OperationName]) = DATALENGTH(LTRIM(RTRIM([OperationName]))) AND DATALENGTH(LTRIM(RTRIM([OperationName]))) > 0),
        CONSTRAINT [CK_RoutingOperation_Seq] CHECK ([Sequence] >= 1),
        CONSTRAINT [CK_RoutingOperation_Times] CHECK ([SetupMinutes] >= 0 AND [RunMinutesPerUnit] > 0 AND [QueueMinutes] >= 0 AND [WaitMinutes] >= 0),
        CONSTRAINT [FK_RoutingOperation_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_RoutingOperation_Routing] FOREIGN KEY ([OrganizationId], [SiteId], [RoutingId]) REFERENCES [organization].[Routing] ([OrganizationId], [SiteId], [Id]),
        CONSTRAINT [FK_RoutingOperation_WorkCenter] FOREIGN KEY ([OrganizationId], [SiteId], [WorkCenterId]) REFERENCES [organization].[WorkCenter] ([OrganizationId], [SiteId], [Id])
    );

    CREATE TABLE [organization].[ProductionOrderOperation] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [ProductionOrderId] bigint NOT NULL,
        [Sequence] int NOT NULL,
        [OperationCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [OperationName] nvarchar(200) NOT NULL,
        [WorkCenterId] bigint NOT NULL,
        [PlannedSetupMinutes] decimal(18,2) NOT NULL,
        [PlannedRunMinutes] decimal(18,2) NOT NULL,
        [PlannedQueueMinutes] decimal(18,2) NOT NULL,
        [PlannedWaitMinutes] decimal(18,2) NOT NULL,
        [ActualSetupMinutes] decimal(18,2) NOT NULL,
        [ActualRunMinutes] decimal(18,2) NOT NULL,
        [CompletedQuantity] decimal(18,6) NOT NULL,
        [ScrapQuantity] decimal(18,6) NOT NULL,
        [RequiresQualityCheck] bit NOT NULL,
        [Instructions] nvarchar(500) NULL,
        [OperatorNotes] nvarchar(500) NULL,
        [Status] tinyint NOT NULL,
        [StartedAtUtc] datetime2(7) NULL,
        [CompletedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ProductionOrderOperation] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductionOrderOperation_Code] CHECK (DATALENGTH([OperationCode]) BETWEEN 1 AND 32 AND DATALENGTH([OperationCode]) = LEN([OperationCode]) AND [OperationCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OperationCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_ProductionOrderOperation_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_ProductionOrderOperation_Name] CHECK (DATALENGTH([OperationName]) BETWEEN 2 AND 400 AND DATALENGTH([OperationName]) = DATALENGTH(LTRIM(RTRIM([OperationName]))) AND DATALENGTH(LTRIM(RTRIM([OperationName]))) > 0),
        CONSTRAINT [CK_ProductionOrderOperation_Qty] CHECK ([CompletedQuantity] >= 0 AND [ScrapQuantity] >= 0),
        CONSTRAINT [CK_ProductionOrderOperation_Seq] CHECK ([Sequence] >= 1),
        CONSTRAINT [CK_ProductionOrderOperation_Status] CHECK ([Status] BETWEEN 1 AND 6),
        CONSTRAINT [CK_ProductionOrderOperation_Times] CHECK ([PlannedSetupMinutes] >= 0 AND [PlannedRunMinutes] >= 0 AND [PlannedQueueMinutes] >= 0 AND [PlannedWaitMinutes] >= 0 AND [ActualSetupMinutes] >= 0 AND [ActualRunMinutes] >= 0),
        CONSTRAINT [FK_ProductionOrderOperation_Order] FOREIGN KEY ([OrganizationId], [ProductionOrderId]) REFERENCES [organization].[ProductionOrder] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderOperation_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_ProductionOrderOperation_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_ProductionOrderOperation_WorkCenter] FOREIGN KEY ([OrganizationId], [SiteId], [WorkCenterId]) REFERENCES [organization].[WorkCenter] ([OrganizationId], [SiteId], [Id])
    );

    EXEC(N'
        CREATE INDEX [IX_ProductionOrder_OrganizationId_SiteId_RoutingId] ON [organization].[ProductionOrder] ([OrganizationId], [SiteId], [RoutingId]);
        ALTER TABLE [organization].[ProductionOrder] ADD CONSTRAINT [CK_ProductionOrder_RoutingCodeSnapshot] CHECK ([RoutingCodeSnapshot] IS NULL OR (DATALENGTH([RoutingCodeSnapshot]) BETWEEN 1 AND 32 AND DATALENGTH([RoutingCodeSnapshot]) = LEN([RoutingCodeSnapshot]) AND [RoutingCodeSnapshot] COLLATE Latin1_General_100_BIN2 NOT LIKE ''%[^A-Z0-9_-]%'' AND LEFT([RoutingCodeSnapshot], 1) COLLATE Latin1_General_100_BIN2 LIKE ''[A-Z0-9]''));
        ALTER TABLE [organization].[ProductionOrder] ADD CONSTRAINT [CK_ProductionOrder_RoutingPair] CHECK (([RoutingId] IS NULL AND [RoutingCodeSnapshot] IS NULL) OR ([RoutingId] IS NOT NULL AND [RoutingCodeSnapshot] IS NOT NULL));
        ALTER TABLE [organization].[ProductionOrder] ADD CONSTRAINT [FK_ProductionOrder_Routing] FOREIGN KEY ([OrganizationId], [SiteId], [RoutingId]) REFERENCES [organization].[Routing] ([OrganizationId], [SiteId], [Id]);
    ');

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event]
        CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped'))));

    CREATE INDEX [IX_ProductionOrderOperation_Org_Site_WC_Status] ON [organization].[ProductionOrderOperation] ([OrganizationId], [SiteId], [WorkCenterId], [Status]);
    CREATE UNIQUE INDEX [UX_ProductionOrderOperation_Order_Seq] ON [organization].[ProductionOrderOperation] ([OrganizationId], [ProductionOrderId], [Sequence]);
    CREATE INDEX [IX_Routing_Org_Site_Item_Status] ON [organization].[Routing] ([OrganizationId], [SiteId], [ItemId], [Status]);
    CREATE INDEX [IX_Routing_OrganizationId_BomVersionId] ON [organization].[Routing] ([OrganizationId], [BomVersionId]);
    CREATE INDEX [IX_Routing_OrganizationId_ItemId] ON [organization].[Routing] ([OrganizationId], [ItemId]);
    CREATE UNIQUE INDEX [UX_Routing_Org_Site_Code_Version] ON [organization].[Routing] ([OrganizationId], [SiteId], [Code], [VersionNumber]);
    CREATE UNIQUE INDEX [UX_Routing_Org_Site_Item_Default] ON [organization].[Routing] ([OrganizationId], [SiteId], [ItemId]) WHERE [Status] = 2 AND [IsDefault] = 1;
    CREATE INDEX [IX_RoutingOperation_Org_Site_WC] ON [organization].[RoutingOperation] ([OrganizationId], [SiteId], [WorkCenterId]);
    CREATE INDEX [IX_RoutingOperation_OrganizationId_SiteId_RoutingId] ON [organization].[RoutingOperation] ([OrganizationId], [SiteId], [RoutingId]);
    CREATE UNIQUE INDEX [UX_RoutingOperation_Routing_Seq] ON [organization].[RoutingOperation] ([OrganizationId], [RoutingId], [Sequence]);
    CREATE INDEX [IX_WorkCenter_Org_Site_Active] ON [organization].[WorkCenter] ([OrganizationId], [SiteId], [IsActive], [Code]);
    CREATE UNIQUE INDEX [UX_WorkCenter_Org_Site_Code] ON [organization].[WorkCenter] ([OrganizationId], [SiteId], [Code]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[WorkCenter] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Routing] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[RoutingOperation] TO [ForgeFlowOrganizationRuntime];
        GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[organization].[ProductionOrderOperation] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[WorkCenter] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[Routing] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111102337_WorkCentersAndRoutings')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111102337_WorkCentersAndRoutings', N'10.0.14');
END;

IF OBJECT_ID(N'organization.StockReservation', N'U') IS NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OrganizationChange_Event' AND parent_object_id = OBJECT_ID(N'organization.OrganizationChange'))
        ALTER TABLE [organization].[OrganizationChange] DROP CONSTRAINT [CK_OrganizationChange_Event];

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_InventoryBalance_Blocked' AND parent_object_id = OBJECT_ID(N'organization.InventoryBalance'))
        ALTER TABLE [organization].[InventoryBalance] DROP CONSTRAINT [CK_InventoryBalance_Blocked];

    IF COL_LENGTH(N'organization.InventoryBalance', N'ReservedBaseQuantity') IS NULL
        ALTER TABLE [organization].[InventoryBalance] ADD [ReservedBaseQuantity] decimal(18,6) NOT NULL CONSTRAINT [DF_InventoryBalance_ReservedBaseQuantity] DEFAULT 0.0;

    CREATE TABLE [organization].[StockReservation] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [ProductionOrderId] bigint NOT NULL,
        [ProductionOrderInputId] bigint NULL,
        [ItemId] bigint NOT NULL,
        [LotCode] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT N'',
        [BaseUnitId] bigint NOT NULL,
        [ReservedBaseQuantity] decimal(18,6) NOT NULL,
        [ConsumedBaseQuantity] decimal(18,6) NOT NULL DEFAULT 0.0,
        [Status] tinyint NOT NULL,
        [Notes] nvarchar(500) NULL,
        [ReleasedAtUtc] datetime2(7) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_StockReservation] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StockReservation_LotCode] CHECK (([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))),
        CONSTRAINT [CK_StockReservation_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_StockReservation_Qty] CHECK ([ReservedBaseQuantity] > 0 AND [ConsumedBaseQuantity] >= 0 AND [ConsumedBaseQuantity] <= [ReservedBaseQuantity]),
        CONSTRAINT [CK_StockReservation_Status] CHECK ([Status] BETWEEN 1 AND 4),
        CONSTRAINT [FK_StockReservation_Item] FOREIGN KEY ([OrganizationId], [ItemId]) REFERENCES [organization].[Item] ([OrganizationId], [Id]),
        CONSTRAINT [FK_StockReservation_Order] FOREIGN KEY ([OrganizationId], [ProductionOrderId]) REFERENCES [organization].[ProductionOrder] ([OrganizationId], [Id]),
        CONSTRAINT [FK_StockReservation_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_StockReservation_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_StockReservation_Unit] FOREIGN KEY ([OrganizationId], [BaseUnitId]) REFERENCES [organization].[Unit] ([OrganizationId], [Id]),
        CONSTRAINT [FK_StockReservation_Warehouse] FOREIGN KEY ([OrganizationId], [WarehouseId]) REFERENCES [organization].[Warehouse] ([OrganizationId], [Id])
    );

    EXEC(N'ALTER TABLE [organization].[InventoryBalance] ADD CONSTRAINT [CK_InventoryBalance_Blocked] CHECK ([BlockedBaseQuantity] >= 0 AND [ReservedBaseQuantity] >= 0 AND ([BlockedBaseQuantity] + [ReservedBaseQuantity]) <= [OnHandBaseQuantity]);');

    ALTER TABLE [organization].[OrganizationChange] ADD CONSTRAINT [CK_OrganizationChange_Event]
        CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated','Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated','UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated','Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated','Item.Created','Item.Updated','Item.Activated','Item.Deactivated','ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated','ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated','AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated','ItemAttributeValue.Updated','ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated','Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated','BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved','ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped'))));

    CREATE INDEX [IX_StockReservation_Org_Order_Status] ON [organization].[StockReservation] ([OrganizationId], [ProductionOrderId], [Status]);
    CREATE INDEX [IX_StockReservation_Org_Site_Wh_Item_Lot_Status] ON [organization].[StockReservation] ([OrganizationId], [SiteId], [WarehouseId], [ItemId], [LotCode], [Status]);
    CREATE INDEX [IX_StockReservation_OrganizationId_BaseUnitId] ON [organization].[StockReservation] ([OrganizationId], [BaseUnitId]);
    CREATE INDEX [IX_StockReservation_OrganizationId_ItemId] ON [organization].[StockReservation] ([OrganizationId], [ItemId]);
    CREATE INDEX [IX_StockReservation_OrganizationId_WarehouseId] ON [organization].[StockReservation] ([OrganizationId], [WarehouseId]);

    IF DATABASE_PRINCIPAL_ID(N'ForgeFlowOrganizationRuntime') IS NOT NULL
    BEGIN
        GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[StockReservation] TO [ForgeFlowOrganizationRuntime];
        DENY DELETE ON OBJECT::[organization].[StockReservation] TO [ForgeFlowOrganizationRuntime];
    END;
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20261111110939_StockReservationsAndDashboard')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261111110939_StockReservationsAndDashboard', N'10.0.14');
END;

COMMIT;
"@

$cn = New-Object System.Data.SqlClient.SqlConnection($conn)
try {
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = $sql
    [void]$cmd.ExecuteNonQuery()
    Write-Host "testforgflow database upgraded to 20261111110939_StockReservationsAndDashboard." -ForegroundColor Green
}
finally {
    $cn.Dispose()
}
