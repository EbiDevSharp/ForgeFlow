using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>
/// Shared infrastructure guard providing non-blocking active organization checks for routine domain writes,
/// and exclusive UPDLOCK/HOLDLOCK locks for administrative lifecycle commands (F-03).
/// </summary>
public static class OrganizationActivityGuard
{
    /// <summary>
    /// Routine domain writes (inventory, sales, procurement, quality, sequences, boms, items, etc.):
    /// Uses non-blocking AsNoTracking query without UPDLOCK/HOLDLOCK.
    /// Eliminates lock contention across independent operations in the same organization.
    /// </summary>
    public static async Task<(bool Ok, string? Error)> RequireActiveOrganizationAsync(
        OrganizationDbContext db,
        long organizationId,
        string inactiveErrorCode,
        string notFoundErrorCode,
        CancellationToken ct = default)
    {
        var row = await db.Organizations
            .IgnoreAutoIncludes()
            .AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new { o.IsActive })
            .SingleOrDefaultAsync(ct);

        if (row is null)
            return (false, notFoundErrorCode);

        if (!row.IsActive)
            return (false, inactiveErrorCode);

        return (true, null);
    }

    /// <summary>
    /// Administrative lifecycle commands (deactivation, archival, reactivation, site creation):
    /// Exclusive UPDLOCK, HOLDLOCK on the Organization row.
    /// </summary>
    public static async Task<(bool Ok, string? Error)> LockExclusiveOrganizationAsync(
        OrganizationDbContext db,
        long organizationId,
        string inactiveErrorCode,
        string notFoundErrorCode,
        CancellationToken ct = default)
    {
        var active = await db.Database.SqlQueryRaw<bool>(
            "SELECT [IsActive] AS [Value] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", organizationId)
            .SingleOrDefaultAsync(ct);

        if (!active)
        {
            var exists = await db.Organizations.IgnoreAutoIncludes().AsNoTracking().AnyAsync(o => o.Id == organizationId, ct);
            return (false, exists ? inactiveErrorCode : notFoundErrorCode);
        }

        return (true, null);
    }

    /// <summary>
    /// Non-blocking active organization check for AuthDbContext stores (SqlUserOperationsStore, SqlRoleOperationsStore).
    /// Uses raw SQL against [organization].[Organization] to maintain clean context boundaries without entity mapping.
    /// </summary>
    public static async Task<OrganizationActivitySnapshot?> RequireActiveOrganizationAsync(
        AuthDbContext db,
        long organizationId,
        CancellationToken ct = default)
    {
        return await db.Database.SqlQueryRaw<OrganizationActivitySnapshot>(
            "SELECT [Id], [Name], [IsActive] FROM [organization].[Organization] WHERE [Id] = {0}", organizationId)
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// Exclusive UPDLOCK, HOLDLOCK on Organization for AuthDbContext administrative commands.
    /// </summary>
    public static async Task<OrganizationActivitySnapshot?> LockExclusiveOrganizationAsync(
        AuthDbContext db,
        long organizationId,
        CancellationToken ct = default)
    {
        return await db.Database.SqlQueryRaw<OrganizationActivitySnapshot>(
            "SELECT [Id], [Name], [IsActive] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {0}", organizationId)
            .SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// Non-blocking active site check for AuthDbContext stores.
    /// </summary>
    public static async Task<OrganizationActivitySnapshot?> RequireActiveSiteAsync(
        AuthDbContext db,
        long organizationId,
        long siteId,
        CancellationToken ct = default)
    {
        return await db.Database.SqlQueryRaw<OrganizationActivitySnapshot>(
            "SELECT [Id], [Name], [IsActive] FROM [organization].[Site] WHERE [OrganizationId] = {0} AND [Id] = {1}", organizationId, siteId)
            .SingleOrDefaultAsync(ct);
    }
}

public sealed record OrganizationActivitySnapshot(long Id, string Name, bool IsActive);
