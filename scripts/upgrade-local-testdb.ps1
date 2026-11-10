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

COMMIT;
"@

$cn = New-Object System.Data.SqlClient.SqlConnection($conn)
try {
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = $sql
    [void]$cmd.ExecuteNonQuery()
    Write-Host "testforgflow database upgraded to 20261110201924_InventoryWarehousesAndStockLedger." -ForegroundColor Green
}
finally {
    $cn.Dispose()
}
