IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    IF SCHEMA_ID(N'organization') IS NULL EXEC(N'CREATE SCHEMA [organization];');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE TABLE [organization].[Organization] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2(7) NOT NULL,
        [CreatedByActorId] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [UpdatedAtUtc] datetime2(7) NULL,
        [UpdatedByActorId] bigint NULL,
        CONSTRAINT [PK_Organization] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Organization_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Organization_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Organization_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE TABLE [organization].[Site] (
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
        CONSTRAINT [PK_Site] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Site_Organization_Id] UNIQUE ([OrganizationId], [Id]),
        CONSTRAINT [CK_Site_Code] CHECK (DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'),
        CONSTRAINT [CK_Site_Metadata] CHECK ([CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))),
        CONSTRAINT [CK_Site_Name] CHECK (DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0),
        CONSTRAINT [FK_Site_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE TABLE [organization].[OrganizationChange] (
        [Id] bigint NOT NULL IDENTITY,
        [OrganizationId] bigint NOT NULL,
        [SiteId] bigint NULL,
        [ActorId] bigint NOT NULL,
        [EventCode] varchar(64) NOT NULL,
        [OccurredAtUtc] datetime2(7) NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [ChangesJson] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_OrganizationChange] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrganizationChange_Actor] CHECK ([ActorId] > 0),
        CONSTRAINT [CK_OrganizationChange_Event] CHECK (DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))),
        CONSTRAINT [CK_OrganizationChange_Json] CHECK (ISJSON([ChangesJson]) = 1),
        CONSTRAINT [FK_OrganizationChange_Organization] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organization] ([Id]),
        CONSTRAINT [FK_OrganizationChange_Site] FOREIGN KEY ([OrganizationId], [SiteId]) REFERENCES [organization].[Site] ([OrganizationId], [Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE INDEX [IX_Organization_IsActive_Code] ON [organization].[Organization] ([IsActive], [Code], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Organization_Code] ON [organization].[Organization] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE INDEX [IX_OrganizationChange_Organization_Time] ON [organization].[OrganizationChange] ([OrganizationId], [OccurredAtUtc], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE INDEX [IX_OrganizationChange_Site_Time] ON [organization].[OrganizationChange] ([OrganizationId], [SiteId], [OccurredAtUtc], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE INDEX [IX_Site_Organization_IsActive_Code] ON [organization].[Site] ([OrganizationId], [IsActive], [Code], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Site_Organization_Code] ON [organization].[Site] ([OrganizationId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    CREATE ROLE [ForgeFlowOrganizationRuntime] AUTHORIZATION [dbo];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Organization] TO [ForgeFlowOrganizationRuntime];
    GRANT SELECT, INSERT, UPDATE ON OBJECT::[organization].[Site] TO [ForgeFlowOrganizationRuntime];
    DENY DELETE ON OBJECT::[organization].[Organization] TO [ForgeFlowOrganizationRuntime];
    DENY DELETE ON OBJECT::[organization].[Site] TO [ForgeFlowOrganizationRuntime];
    GRANT SELECT, INSERT ON OBJECT::[organization].[OrganizationChange] TO [ForgeFlowOrganizationRuntime];
    DENY UPDATE, DELETE ON OBJECT::[organization].[OrganizationChange] TO [ForgeFlowOrganizationRuntime];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261108124505_OrganizationSitesBaseline', N'10.0.14');
END;

COMMIT;
GO

