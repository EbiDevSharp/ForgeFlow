BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM [organization].[Organization])
   OR EXISTS (SELECT 1 FROM [organization].[Site])
   OR EXISTS (SELECT 1 FROM [organization].[OrganizationChange])
    THROW 51001, 'OrganizationSite.NonEmptyRollbackDenied', 1;
DROP ROLE [ForgeFlowOrganizationRuntime];

DROP TABLE [organization].[OrganizationChange];

DROP TABLE [organization].[Site];

DROP TABLE [organization].[Organization];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261108124505_OrganizationSitesBaseline';

COMMIT;
GO

