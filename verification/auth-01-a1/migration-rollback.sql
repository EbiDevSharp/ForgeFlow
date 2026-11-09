BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM [auth].[User]) OR EXISTS (SELECT 1 FROM [auth].[UserClaim])
 OR EXISTS (SELECT 1 FROM [auth].[UserLogin]) OR EXISTS (SELECT 1 FROM [auth].[UserToken])
 OR EXISTS (SELECT 1 FROM [auth].[OrganizationMembership]) OR EXISTS (SELECT 1 FROM [auth].[SiteMembership])
 OR EXISTS (SELECT 1 FROM [auth].[ScopedRoleAssignment]) OR EXISTS (SELECT 1 FROM [auth].[Session])
 OR EXISTS (SELECT 1 FROM [auth].[SecurityEvent]) OR EXISTS (SELECT 1 FROM [auth].[BootstrapState] WHERE [State] <> 0)
    THROW 51001, 'Auth.NonEmptyRollbackDenied', 1;
DROP ROLE [ForgeFlowAuthRuntime];

DROP TABLE [auth].[BootstrapState];

DROP TABLE [auth].[ScopedRoleAssignment];

DROP TABLE [auth].[SecurityEvent];

DROP TABLE [auth].[UserClaim];

DROP TABLE [auth].[UserLogin];

DROP TABLE [auth].[UserToken];

DROP TABLE [auth].[SiteMembership];

DROP TABLE [auth].[Session];

DROP TABLE [auth].[OrganizationMembership];

DROP TABLE [auth].[User];

DELETE FROM [auth].[__EFMigrationsHistory]
WHERE [MigrationId] = N'20261108150853_IdentityBootstrapBaseline';

COMMIT;
GO

