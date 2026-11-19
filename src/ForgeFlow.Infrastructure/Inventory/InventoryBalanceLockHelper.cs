using System.Text;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Inventory;

internal static class InventoryBalanceLockHelper
{
    internal static async Task<List<InventoryBalance>> LockBalancesAsync(
        OrganizationDbContext db,
        long organizationId,
        IEnumerable<(long WarehouseId, long ItemId, string LotCode)> keys,
        CancellationToken ct)
    {
        var sortedKeys = keys
            .Distinct()
            .OrderBy(k => k.WarehouseId)
            .ThenBy(k => k.ItemId)
            .ThenBy(k => k.LotCode, StringComparer.Ordinal)
            .ToArray();

        if (sortedKeys.Length == 0)
            return [];

        if (sortedKeys.Length == 1)
        {
            var g = sortedKeys[0];
            return await db.InventoryBalances
                .FromSqlInterpolated($"SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, ROWLOCK) WHERE [OrganizationId] = {organizationId} AND [WarehouseId] = {g.WarehouseId} AND [ItemId] = {g.ItemId} AND [LotCode] = {g.LotCode}")
                .OrderBy(b => b.Id)
                .ToListAsync(ct);
        }

        var sqlBuilder = new StringBuilder();
        sqlBuilder.Append("SELECT * FROM [organization].[InventoryBalance] WITH (UPDLOCK, ROWLOCK) WHERE [OrganizationId] = @p0 AND (");
        var parameters = new List<object>(1 + sortedKeys.Length * 3) { new SqlParameter("@p0", organizationId) };

        for (var i = 0; i < sortedKeys.Length; i++)
        {
            if (i > 0) sqlBuilder.Append(" OR ");
            var wIdx = parameters.Count;
            var iIdx = wIdx + 1;
            var lIdx = wIdx + 2;
            sqlBuilder.Append($"([WarehouseId] = @p{wIdx} AND [ItemId] = @p{iIdx} AND [LotCode] = @p{lIdx})");
            parameters.Add(new SqlParameter($"@p{wIdx}", sortedKeys[i].WarehouseId));
            parameters.Add(new SqlParameter($"@p{iIdx}", sortedKeys[i].ItemId));
            parameters.Add(new SqlParameter($"@p{lIdx}", sortedKeys[i].LotCode));
        }
        sqlBuilder.Append(')');

        return await db.InventoryBalances
            .FromSqlRaw(sqlBuilder.ToString(), parameters.ToArray())
            .OrderBy(b => b.Id)
            .ToListAsync(ct);
    }
}
