using System.Data.Common;
using System.Text.Json;
using System.Text.Encodings.Web;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>
/// Create/dispose through IDbContextFactory for each operation, never keep on a Blazor circuit.
/// This infrastructure boundary does not authenticate or authorize: secured use cases must do that first.
/// </summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, TimeProvider? clock = null)
    : DbContext(options)
{
    private bool failed;
    // Stored JSON only, never HTML. Keep valid 200-character Persian before/after names within nvarchar(2000).
    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<OrganizationChange> OrganizationChanges => Set<OrganizationChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => OrganizationModel.Configure(modelBuilder);

    // There is no default actor or unaudited write path through this context.
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => RejectUnattributedWrite();
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        => Task.FromResult(RejectUnattributedWrite());

    private int RejectUnattributedWrite()
    {
        if (failed || ChangeTracker.HasChanges()) throw new InvalidOperationException(OrganizationAccessErrors.ActorRequired);
        return 0;
    }

    /// <summary>
    /// Trusted caller supplies actor only after current permission evaluation. No provider or fake authorization.
    /// Owns its transaction; dispose after any failure. No transparent retry after an unknown commit outcome.
    /// </summary>
    public Task<int> SaveChangesAsync(TrustedActor actor, CancellationToken cancellationToken = default)
        => SaveAttributed(actor, true, cancellationToken);

    internal Task<int> SaveInOperation(TrustedActor actor, CancellationToken ct) => SaveAttributed(actor, false, ct);

    private async Task<int> SaveAttributed(TrustedActor actor, bool ownsTransaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (failed || (ownsTransaction ? Database.CurrentTransaction is not null : Database.CurrentTransaction is null))
            throw new InvalidOperationException(OrganizationAccessErrors.PersistenceUnavailable);
        ChangeTracker.DetectChanges();
        var entries = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (entries.Length == 0) return 0;
        foreach (var entry in entries)
        {
            if (entry.Entity is not (Organization or Site) || entry.State == EntityState.Deleted)
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
            if (entry.State == EntityState.Modified && entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("Name" or "IsActive")))
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
        }
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var changes = entries.SelectMany(CaptureChanges).ToArray();
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await LockAndValidateParents(entries, cancellationToken);
            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Property("CreatedAtUtc").CurrentValue = now;
                    entry.Property("CreatedByActorId").CurrentValue = actor.ActorId;
                    entry.Property("UpdatedAtUtc").CurrentValue = null;
                    entry.Property("UpdatedByActorId").CurrentValue = null;
                }
                else
                {
                    if (now < (DateTime)entry.Property("CreatedAtUtc").CurrentValue!)
                        throw new InvalidOperationException(OrganizationAccessErrors.PersistenceUnavailable);
                    entry.Property("UpdatedAtUtc").CurrentValue = now;
                    entry.Property("UpdatedByActorId").CurrentValue = actor.ActorId;
                }
            }
            var count = await base.SaveChangesAsync(true, cancellationToken);
            foreach (var change in changes)
            {
                var orgId = change.Entry.Entity is Organization org ? org.Id : ((Site)change.Entry.Entity).OrganizationId;
                long? siteId = change.Entry.Entity is Site site ? site.Id : null;
                OrganizationChanges.Add(new OrganizationChange(orgId, siteId, actor.ActorId, change.EventCode,
                    now, actor.CorrelationId, change.Json));
            }
            try { await base.SaveChangesAsync(true, cancellationToken); }
            catch (DbUpdateException ex) when (!ownsTransaction) { throw new OrganizationAuditWriteException(ex); }
            if (transaction is not null)
            {
                try { await transaction.CommitAsync(cancellationToken); }
                catch (Exception ex) { throw new OrganizationCommitOutcomeUnknownException(ex); }
            }
            return count;
        }
        catch
        {
            failed = true;
            // Disposal rolls back even when cancellation prevents an explicit rollback call.
            ChangeTracker.Clear();
            throw;
        }
    }

    private sealed record PendingChange(EntityEntry Entry, string EventCode, string Json);

    private static IEnumerable<PendingChange> CaptureChanges(EntityEntry entry)
    {
        var prefix = entry.Entity is Organization ? "Organization" : "Site";
        if (entry.State == EntityState.Added)
        {
            var after = new { Code = entry.Property("Code").CurrentValue, Name = entry.Property("Name").CurrentValue,
                IsActive = entry.Property("IsActive").CurrentValue };
            yield return new PendingChange(entry, prefix + ".Created", JsonSerializer.Serialize(new { Before = (object?)null, After = after }, AuditJson));
            yield break;
        }
        if (entry.Property("Name").IsModified)
            yield return new PendingChange(entry, prefix + ".Renamed", JsonSerializer.Serialize(new {
                Before = new { Name = entry.Property("Name").OriginalValue }, After = new { Name = entry.Property("Name").CurrentValue } }, AuditJson));
        if (entry.Property("IsActive").IsModified)
            yield return new PendingChange(entry, prefix + ((bool)entry.Property("IsActive").CurrentValue! ? ".Activated" : ".Deactivated"),
                JsonSerializer.Serialize(new { Before = new { IsActive = entry.Property("IsActive").OriginalValue },
                    After = new { IsActive = entry.Property("IsActive").CurrentValue } }, AuditJson));
    }

    private async Task LockAndValidateParents(EntityEntry[] entries, CancellationToken cancellationToken)
    {
        var parents = entries.Select(e => e.Entity is Organization org ? org : ((Site)e.Entity).Organization)
            .Distinct().OrderBy(o => o.Id).ToArray();
        foreach (var parent in parents)
        {
            if (Entry(parent).State == EntityState.Added) continue; // Insert lock retained by our transaction.
            await using DbCommand command = Database.GetDbConnection().CreateCommand();
            command.Transaction = Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SELECT [IsActive] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @id";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = parent.Id;
            command.Parameters.Add(parameter);
            var storedActive = await command.ExecuteScalarAsync(cancellationToken);
            if (storedActive is not bool active)
                throw new InvalidOperationException(OrganizationSiteErrors.OrganizationNotFound);
            var parentEntry = Entry(parent);
            var effectiveActive = parentEntry.Property(o => o.IsActive).IsModified ? parent.IsActive : active;
            if (parentEntry.Property(o => o.Name).IsModified && !effectiveActive)
                throw new InvalidOperationException(OrganizationSiteErrors.OrganizationInactive);
            if (entries.Any(e => e.Entity is Site s && s.Organization == parent) && !effectiveActive
                && (!active || entries.Any(e => e.Entity is Site s && s.Organization == parent && s.IsActive)))
                throw new InvalidOperationException(OrganizationSiteErrors.OrganizationInactive);
            if (parentEntry.Property(o => o.IsActive).IsModified && !parent.IsActive)
            {
                var pendingInactive = entries.Where(e => e.Entity is Site s && s.Organization == parent && !s.IsActive)
                    .Select(e => ((Site)e.Entity).Id).ToArray();
                if (await Sites.AnyAsync(s => s.OrganizationId == parent.Id && s.IsActive && !pendingInactive.Contains(s.Id), cancellationToken)
                    || entries.Any(e => e.Entity is Site s && s.Organization == parent && s.IsActive))
                    throw new InvalidOperationException(OrganizationSiteErrors.OrganizationHasActiveSites);
            }
        }
    }
}
