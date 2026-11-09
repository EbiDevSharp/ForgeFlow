IF OBJECT_ID(N'[auth].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'auth') IS NULL EXEC(N'CREATE SCHEMA [auth];');
    CREATE TABLE [auth].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    IF SCHEMA_ID(N'auth') IS NULL EXEC(N'CREATE SCHEMA [auth];');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[User] (
        [Id] bigint NOT NULL IDENTITY,
        [IsActive] bit NOT NULL,
        [PreferredCulture] varchar(5) NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [RowVersion] rowversion NOT NULL,
        [UserName] nvarchar(128) NOT NULL,
        [NormalizedUserName] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [SecurityStamp] nvarchar(256) NOT NULL,
        [ConcurrencyStamp] nvarchar(256) NOT NULL,
        [PhoneNumber] nvarchar(64) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset(7) NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_User] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_User_Culture] CHECK ([PreferredCulture] IS NULL OR (DATALENGTH([PreferredCulture]) = 5 AND [PreferredCulture] COLLATE Latin1_General_100_BIN2 IN ('fa-IR','en-US'))),
        CONSTRAINT [CK_User_Metadata] CHECK ([AccessFailedCount] >= 0 AND ([UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]) AND ([LockoutEnd] IS NULL OR DATEPART(TZOFFSET,[LockoutEnd]) = 0)),
        CONSTRAINT [CK_User_Name] CHECK (DATALENGTH([UserName]) BETWEEN 2 AND 256 AND DATALENGTH([UserName]) = 2 * LEN([UserName]) AND [UserName] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Za-z0-9._@+-]%' AND DATALENGTH([NormalizedUserName]) = DATALENGTH([UserName]) AND [NormalizedUserName] COLLATE Latin1_General_100_BIN2 = UPPER([UserName] COLLATE Latin1_General_100_BIN2))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[BootstrapState] (
        [Id] tinyint NOT NULL,
        [State] tinyint NOT NULL,
        [OperationId] uniqueidentifier NULL,
        [CompletedAtUtc] datetime2(7) NULL,
        [FirstUserId] bigint NULL,
        [OrganizationId] bigint NULL,
        [SiteId] bigint NULL,
        [HostOperatorReference] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_BootstrapState] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Bootstrap_Singleton] CHECK ([Id] = 1),
        CONSTRAINT [CK_Bootstrap_State] CHECK (([State]=0 AND [OperationId] IS NULL AND [CompletedAtUtc] IS NULL AND [FirstUserId] IS NULL AND [OrganizationId] IS NULL AND [SiteId] IS NULL AND [HostOperatorReference] IS NULL) OR ([State]=1 AND [OperationId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [FirstUserId] IS NOT NULL AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL AND [HostOperatorReference] IS NOT NULL)),
        CONSTRAINT [FK_BootstrapState_Organization_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_BootstrapState_Site_OrganizationId_SiteId] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_BootstrapState_User_FirstUserId] FOREIGN KEY ([FirstUserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[OrganizationMembership] (
        [UserId] bigint NOT NULL,
        [OrganizationId] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_OrganizationMembership] PRIMARY KEY ([UserId], [OrganizationId]),
        CONSTRAINT [CK_OrganizationMembership_Metadata] CHECK ([CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))),
        CONSTRAINT [FK_OrganizationMembership_Organization_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_OrganizationMembership_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[Session] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] bigint NOT NULL,
        [SecurityStampSnapshot] nvarchar(256) NOT NULL,
        [IssuedAtUtc] datetime2(7) NOT NULL,
        [ExpiresAtUtc] datetime2(7) NOT NULL,
        [RevokedAtUtc] datetime2(7) NULL,
        [RevokeReason] varchar(32) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Session] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Session_Lifecycle] CHECK ([ExpiresAtUtc] > [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN ('Logout','PasswordReset','AccountDisabled','AccountLocked','StampChanged')))),
        CONSTRAINT [FK_Session_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[UserClaim] (
        [Id] int NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [ClaimType] nvarchar(256) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_UserClaim] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserClaim_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[UserLogin] (
        [LoginProvider] nvarchar(128) NOT NULL,
        [ProviderKey] nvarchar(128) NOT NULL,
        [ProviderDisplayName] nvarchar(256) NULL,
        [UserId] bigint NOT NULL,
        CONSTRAINT [PK_UserLogin] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_UserLogin_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[UserToken] (
        [UserId] bigint NOT NULL,
        [LoginProvider] nvarchar(128) NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_UserToken] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_UserToken_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[SiteMembership] (
        [UserId] bigint NOT NULL,
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_SiteMembership] PRIMARY KEY ([UserId], [OrganizationId], [SiteId]),
        CONSTRAINT [CK_SiteMembership_Metadata] CHECK ([CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))),
        CONSTRAINT [FK_SiteMembership_OrganizationMembership_UserId_OrganizationId] FOREIGN KEY ([UserId], [OrganizationId]) REFERENCES [auth].[OrganizationMembership] ([UserId], [OrganizationId]),
        CONSTRAINT [FK_SiteMembership_Site_OrganizationId_SiteId] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[SecurityEvent] (
        [Id] bigint NOT NULL IDENTITY,
        [EventCode] varchar(64) NOT NULL,
        [OccurredAtUtc] datetime2(7) NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [ActorKind] tinyint NOT NULL,
        [ActorUserId] bigint NULL,
        [SubjectUserId] bigint NULL,
        [SessionId] uniqueidentifier NULL,
        [HostOperatorReference] nvarchar(256) NULL,
        [OrganizationId] bigint NULL,
        [SiteId] bigint NULL,
        [Outcome] tinyint NOT NULL,
        [DataJson] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_SecurityEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SecurityEvent_Actor] CHECK (([ActorKind]=0 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=1 AND [ActorUserId] IS NOT NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=2 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NOT NULL AND LEN([HostOperatorReference]) > 0)),
        CONSTRAINT [CK_SecurityEvent_Code] CHECK (DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','CulturePreferenceChanged')),
        CONSTRAINT [CK_SecurityEvent_Data] CHECK (ISJSON([DataJson])=1 AND [Outcome] IN (0,1,2) AND ([SiteId] IS NULL OR [OrganizationId] IS NOT NULL)),
        CONSTRAINT [FK_SecurityEvent_Organization_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_SecurityEvent_Session_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [auth].[Session] ([Id]),
        CONSTRAINT [FK_SecurityEvent_Site_OrganizationId_SiteId] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id]),
        CONSTRAINT [FK_SecurityEvent_User_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [auth].[User] ([Id]),
        CONSTRAINT [FK_SecurityEvent_User_SubjectUserId] FOREIGN KEY ([SubjectUserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE TABLE [auth].[ScopedRoleAssignment] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [RoleId] tinyint NOT NULL,
        [ScopeKind] tinyint NOT NULL,
        [OrganizationId] bigint NULL,
        [SiteId] bigint NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_ScopedRoleAssignment] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Role_Scope] CHECK (([RoleId]=1 AND [ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([RoleId]=2 AND [ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL) OR ([RoleId]=3 AND [ScopeKind]=0 AND [OrganizationId] IS NULL AND [SiteId] IS NULL)),
        CONSTRAINT [CK_ScopedRoleAssignment_Metadata] CHECK ([CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))),
        CONSTRAINT [FK_ScopedRoleAssignment_OrganizationMembership_UserId_OrganizationId] FOREIGN KEY ([UserId], [OrganizationId]) REFERENCES [auth].[OrganizationMembership] ([UserId], [OrganizationId]),
        CONSTRAINT [FK_ScopedRoleAssignment_SiteMembership_UserId_OrganizationId_SiteId] FOREIGN KEY ([UserId], [OrganizationId], [SiteId]) REFERENCES [auth].[SiteMembership] ([UserId], [OrganizationId], [SiteId]),
        CONSTRAINT [FK_ScopedRoleAssignment_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [auth].[User] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CompletedAtUtc', N'FirstUserId', N'HostOperatorReference', N'OperationId', N'OrganizationId', N'SiteId', N'State') AND [object_id] = OBJECT_ID(N'[auth].[BootstrapState]'))
        SET IDENTITY_INSERT [auth].[BootstrapState] ON;
    EXEC(N'INSERT INTO [auth].[BootstrapState] ([Id], [CompletedAtUtc], [FirstUserId], [HostOperatorReference], [OperationId], [OrganizationId], [SiteId], [State])
    VALUES (CAST(1 AS tinyint), NULL, NULL, NULL, NULL, NULL, NULL, CAST(0 AS tinyint))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CompletedAtUtc', N'FirstUserId', N'HostOperatorReference', N'OperationId', N'OrganizationId', N'SiteId', N'State') AND [object_id] = OBJECT_ID(N'[auth].[BootstrapState]'))
        SET IDENTITY_INSERT [auth].[BootstrapState] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_BootstrapState_FirstUserId] ON [auth].[BootstrapState] ([FirstUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BootstrapState_OperationId] ON [auth].[BootstrapState] ([OperationId]) WHERE [OperationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_BootstrapState_OrganizationId_SiteId] ON [auth].[BootstrapState] ([OrganizationId], [SiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_OrganizationMembership_OrganizationId_IsActive_UserId] ON [auth].[OrganizationMembership] ([OrganizationId], [IsActive], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_ScopedRoleAssignment_UserId_IsActive_ScopeKind_OrganizationId_SiteId] ON [auth].[ScopedRoleAssignment] ([UserId], [IsActive], [ScopeKind], [OrganizationId], [SiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_ScopedRoleAssignment_UserId_OrganizationId_SiteId] ON [auth].[ScopedRoleAssignment] ([UserId], [OrganizationId], [SiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ScopedRoleAssignment_UserId_RoleId] ON [auth].[ScopedRoleAssignment] ([UserId], [RoleId]) WHERE [ScopeKind] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId] ON [auth].[ScopedRoleAssignment] ([UserId], [RoleId], [OrganizationId]) WHERE [ScopeKind] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ScopedRoleAssignment_UserId_RoleId_OrganizationId_SiteId] ON [auth].[ScopedRoleAssignment] ([UserId], [RoleId], [OrganizationId], [SiteId]) WHERE [ScopeKind] = 2');
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SecurityEvent_ActorUserId] ON [auth].[SecurityEvent] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SecurityEvent_CorrelationId] ON [auth].[SecurityEvent] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SecurityEvent_OccurredAtUtc_Id] ON [auth].[SecurityEvent] ([OccurredAtUtc], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SecurityEvent_OrganizationId_OccurredAtUtc_Id] ON [auth].[SecurityEvent] ([OrganizationId], [OccurredAtUtc], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SecurityEvent_OrganizationId_SiteId] ON [auth].[SecurityEvent] ([OrganizationId], [SiteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SecurityEvent_SessionId] ON [auth].[SecurityEvent] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SecurityEvent_SubjectUserId_OccurredAtUtc_Id] ON [auth].[SecurityEvent] ([SubjectUserId], [OccurredAtUtc], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_Session_UserId_RevokedAtUtc_ExpiresAtUtc] ON [auth].[Session] ([UserId], [RevokedAtUtc], [ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_SiteMembership_OrganizationId_SiteId_IsActive_UserId] ON [auth].[SiteMembership] ([OrganizationId], [SiteId], [IsActive], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [auth].[User] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [UX_User_NormalizedUserName] ON [auth].[User] ([NormalizedUserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_UserClaim_UserId] ON [auth].[UserClaim] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE INDEX [IX_UserLogin_UserId] ON [auth].[UserLogin] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    CREATE ROLE [ForgeFlowAuthRuntime] AUTHORIZATION [dbo];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[User] TO [ForgeFlowAuthRuntime];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[Session] TO [ForgeFlowAuthRuntime];
    GRANT SELECT ON OBJECT::[auth].[UserClaim] TO [ForgeFlowAuthRuntime];
    GRANT SELECT ON OBJECT::[auth].[UserLogin] TO [ForgeFlowAuthRuntime];
    GRANT SELECT ON OBJECT::[auth].[UserToken] TO [ForgeFlowAuthRuntime];
    GRANT SELECT ON OBJECT::[auth].[OrganizationMembership] TO [ForgeFlowAuthRuntime];
    GRANT SELECT ON OBJECT::[auth].[SiteMembership] TO [ForgeFlowAuthRuntime];
    GRANT SELECT ON OBJECT::[auth].[ScopedRoleAssignment] TO [ForgeFlowAuthRuntime];
    GRANT SELECT ON OBJECT::[auth].[BootstrapState] TO [ForgeFlowAuthRuntime];
    GRANT SELECT, INSERT ON OBJECT::[auth].[SecurityEvent] TO [ForgeFlowAuthRuntime];
    DENY DELETE ON OBJECT::[auth].[User] TO [ForgeFlowAuthRuntime];
    DENY DELETE ON OBJECT::[auth].[Session] TO [ForgeFlowAuthRuntime];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserClaim] TO [ForgeFlowAuthRuntime];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserLogin] TO [ForgeFlowAuthRuntime];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[UserToken] TO [ForgeFlowAuthRuntime];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[OrganizationMembership] TO [ForgeFlowAuthRuntime];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[SiteMembership] TO [ForgeFlowAuthRuntime];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[ScopedRoleAssignment] TO [ForgeFlowAuthRuntime];
    DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[BootstrapState] TO [ForgeFlowAuthRuntime];
    DENY UPDATE, DELETE ON OBJECT::[auth].[SecurityEvent] TO [ForgeFlowAuthRuntime];
END;

IF NOT EXISTS (
    SELECT * FROM [auth].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline'
)
BEGIN
    INSERT INTO [auth].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108150853_IdentityBootstrapBaseline', N'10.0.14');
END;

COMMIT;
GO

