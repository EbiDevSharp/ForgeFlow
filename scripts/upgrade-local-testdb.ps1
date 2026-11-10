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

COMMIT TRANSACTION;
"@

$cn = New-Object System.Data.SqlClient.SqlConnection($conn)
try {
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = $sql
    [void]$cmd.ExecuteNonQuery()
    Write-Host "testforgflow database upgraded to 20261110134703_RolesAndGranularPermissions and admin full permissions verified." -ForegroundColor Green
}
finally {
    $cn.Dispose()
}
