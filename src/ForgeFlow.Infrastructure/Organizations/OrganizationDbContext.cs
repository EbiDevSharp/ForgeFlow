using System.Data.Common;
using System.Text.Json;
using System.Text.Encodings.Web;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
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
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();
    public DbSet<ClassificationSet> ClassificationSets => Set<ClassificationSet>();
    public DbSet<Classification> Classifications => Set<Classification>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemClassification> ItemClassifications => Set<ItemClassification>();
    public DbSet<ItemUnitConversion> ItemUnitConversions => Set<ItemUnitConversion>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<ItemAttributeValue> ItemAttributeValues => Set<ItemAttributeValue>();
    public DbSet<ContextDimension> ContextDimensions => Set<ContextDimension>();
    public DbSet<Bom> Boms => Set<Bom>();
    public DbSet<BomVersion> BomVersions => Set<BomVersion>();
    public DbSet<BomInput> BomInputs => Set<BomInput>();
    public DbSet<BomOutput> BomOutputs => Set<BomOutput>();
    public DbSet<BomApplicability> BomApplicabilities => Set<BomApplicability>();
    public DbSet<BomApplicabilityCondition> BomApplicabilityConditions => Set<BomApplicabilityCondition>();
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
            if (entry.Entity is not (Organization or Site or Unit or UnitConversion
                or ClassificationSet or Classification or Item or ItemClassification
                or ItemUnitConversion or AttributeDefinition or ItemAttributeValue
                or ContextDimension or Bom or BomVersion or BomInput or BomOutput
                or BomApplicability or BomApplicabilityCondition))
            {
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
            }
            if (entry.State == EntityState.Deleted && entry.Entity is not (ItemClassification or ItemAttributeValue
                or BomInput or BomOutput or BomApplicability or BomApplicabilityCondition))
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
            if (entry.State == EntityState.Modified)
            {
                var allowedProps = entry.Entity switch
                {
                    Unit => new[] { "Name", "Symbol", "PrecisionScale", "IsActive" },
                    UnitConversion or ItemUnitConversion => new[] { "Factor", "IsActive" },
                    ClassificationSet => new[] { "Name", "IsMandatory", "AllowMultiple", "IsActive" },
                    Classification => new[] { "Name", "ParentClassificationId", "IsActive" },
                    Item => new[] { "Name", "Description", "BaseUnitId", "CanBePurchased", "CanBeManufactured", "CanBeSold", "CanBeConsumed", "IsActive" },
                    AttributeDefinition => new[] { "Name", "UnitId", "IsRequired", "IsActive" },
                    ItemAttributeValue => new[] { "TextValue", "DecimalValue", "BooleanValue" },
                    ContextDimension => new[] { "Name", "IsActive" },
                    Bom => new[] { "Name", "IsActive" },
                    BomVersion => new[] { "Status", "BaseOutputQuantity", "BaseOutputUnitId", "EffectiveFromDate", "EffectiveToDate", "Notes" },
                    BomApplicability => new[] { "Name", "IsActive" },
                    _ => new[] { "Name", "IsActive" }
                };
                if (entry.Properties.Any(p => p.IsModified && !allowedProps.Contains(p.Metadata.Name, StringComparer.Ordinal)))
                    throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
            }
        }
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var changes = CaptureAllChanges(entries).ToArray();
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            await LockAndValidateParents(entries, cancellationToken);
            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Deleted)
                    continue;
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
                var orgId = ResolveEntityOrganizationId(change.Entry.Entity);
                long? siteId = change.Entry.Entity is Site siteEntity ? siteEntity.Id : null;
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

    private static long ResolveEntityOrganizationId(object entity) => entity switch
    {
        Organization org => org.Id,
        Site site => site.OrganizationId,
        Unit unit => unit.OrganizationId,
        UnitConversion conv => conv.OrganizationId,
        ClassificationSet cs => cs.OrganizationId,
        Classification c => c.OrganizationId,
        Item i => i.OrganizationId,
        ItemClassification ic => ic.OrganizationId,
        ItemUnitConversion icv => icv.OrganizationId,
        AttributeDefinition ad => ad.OrganizationId,
        ItemAttributeValue av => av.OrganizationId,
        ContextDimension cd => cd.OrganizationId,
        Bom b => b.OrganizationId,
        BomVersion bv => bv.OrganizationId,
        BomInput bi => bi.OrganizationId,
        BomOutput bo => bo.OrganizationId,
        BomApplicability ba => ba.OrganizationId,
        BomApplicabilityCondition bc => bc.OrganizationId,
        _ => throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied)
    };

    private static IEnumerable<PendingChange> CaptureAllChanges(EntityEntry[] entries)
    {
        var seenBomLinesVersions = new HashSet<long>();
        var seenApplicabilityIds = new HashSet<long>();

        foreach (var entry in entries)
        {
            if (entry.Entity is BomInput or BomOutput)
            {
                var verId = entry.Entity is BomInput bi ? bi.BomVersionId : ((BomOutput)entry.Entity).BomVersionId;
                if (seenBomLinesVersions.Add(verId))
                {
                    yield return new PendingChange(entry, "BomVersion.LinesSaved",
                        JsonSerializer.Serialize(new { BomVersionId = verId }, AuditJson));
                }
                continue;
            }

            if (entry.Entity is BomApplicabilityCondition cond)
            {
                if (seenApplicabilityIds.Add(cond.BomApplicabilityId))
                {
                    yield return new PendingChange(entry, "BomVersion.ApplicabilitySaved",
                        JsonSerializer.Serialize(new { cond.BomApplicabilityId }, AuditJson));
                }
                continue;
            }

            foreach (var change in CaptureChanges(entry))
                yield return change;
        }
    }

    private static IEnumerable<PendingChange> CaptureChanges(EntityEntry entry)
    {
        if (entry.Entity is ContextDimension cd)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "ContextDimension.Created",
                    JsonSerializer.Serialize(new { cd.Code, cd.Name, cd.IsActive }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified)
            {
                yield return new PendingChange(entry, "ContextDimension.Updated",
                    JsonSerializer.Serialize(new { cd.Code, cd.Name }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "ContextDimension.Activated" : "ContextDimension.Deactivated",
                    JsonSerializer.Serialize(new { cd.Code, cd.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is Bom bom)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Bom.Created",
                    JsonSerializer.Serialize(new { bom.Code, bom.Name, bom.PrimaryOutputItemId }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified)
            {
                yield return new PendingChange(entry, "Bom.Renamed",
                    JsonSerializer.Serialize(new { bom.Code, bom.Name }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Bom.Activated" : "Bom.Deactivated",
                    JsonSerializer.Serialize(new { bom.Code, bom.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is BomVersion bv)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "BomVersion.Created",
                    JsonSerializer.Serialize(new { bv.BomId, bv.VersionNumber, Status = bv.Status.ToString(), bv.BaseOutputQuantity, bv.BaseOutputUnitId }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified)
            {
                yield return new PendingChange(entry, "BomVersion.StatusChanged",
                    JsonSerializer.Serialize(new
                    {
                        bv.BomId,
                        bv.VersionNumber,
                        Before = entry.Property("Status").OriginalValue?.ToString(),
                        After = bv.Status.ToString()
                    }, AuditJson));
            }
            if (entry.Property("BaseOutputQuantity").IsModified || entry.Property("BaseOutputUnitId").IsModified
                || entry.Property("EffectiveFromDate").IsModified || entry.Property("EffectiveToDate").IsModified
                || entry.Property("Notes").IsModified)
            {
                yield return new PendingChange(entry, "BomVersion.Updated",
                    JsonSerializer.Serialize(new { bv.BomId, bv.VersionNumber, bv.BaseOutputQuantity, bv.BaseOutputUnitId }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is BomApplicability ba)
        {
            yield return new PendingChange(entry, "BomVersion.ApplicabilitySaved",
                JsonSerializer.Serialize(new { ba.BomVersionId, ba.Name, ba.IsActive }, AuditJson));
            yield break;
        }

        if (entry.Entity is Item item)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Item.Created", JsonSerializer.Serialize(new
                {
                    Before = (object?)null,
                    After = new { item.Code, item.Name, item.BaseUnitId, item.CanBePurchased, item.CanBeManufactured, item.CanBeSold, item.CanBeConsumed }
                }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("Description").IsModified || entry.Property("BaseUnitId").IsModified
                || entry.Property("CanBePurchased").IsModified || entry.Property("CanBeManufactured").IsModified
                || entry.Property("CanBeSold").IsModified || entry.Property("CanBeConsumed").IsModified)
            {
                yield return new PendingChange(entry, "Item.Updated", JsonSerializer.Serialize(new
                {
                    item.Code,
                    Before = new { Name = entry.Property("Name").OriginalValue, BaseUnitId = entry.Property("BaseUnitId").OriginalValue },
                    After = new { item.Name, item.BaseUnitId, item.CanBePurchased, item.CanBeManufactured, item.CanBeSold, item.CanBeConsumed }
                }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Item.Activated" : "Item.Deactivated",
                    JsonSerializer.Serialize(new { item.Code, IsActive = item.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ClassificationSet cs)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "ClassificationSet.Created",
                    JsonSerializer.Serialize(new { cs.Code, cs.Name, cs.IsMandatory, cs.AllowMultiple }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is Classification cls)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Classification.Created",
                    JsonSerializer.Serialize(new { cls.ClassificationSetId, cls.ParentClassificationId, cls.Code, cls.Name }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("ParentClassificationId").IsModified)
            {
                yield return new PendingChange(entry, "Classification.Updated",
                    JsonSerializer.Serialize(new { cls.Code, cls.Name, cls.ParentClassificationId }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Classification.Activated" : "Classification.Deactivated",
                    JsonSerializer.Serialize(new { cls.Code, cls.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ItemClassification ic)
        {
            yield return new PendingChange(entry, "ItemClassification.Updated",
                JsonSerializer.Serialize(new { ic.ItemId, ic.ClassificationId, Action = entry.State == EntityState.Deleted ? "Removed" : "Assigned" }, AuditJson));
            yield break;
        }

        if (entry.Entity is ItemUnitConversion icv)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "ItemUnitConversion.Created",
                    JsonSerializer.Serialize(new { icv.ItemId, icv.FromUnitId, icv.ToUnitId, icv.Factor }, AuditJson));
                yield break;
            }
            if (entry.Property("Factor").IsModified)
            {
                yield return new PendingChange(entry, "ItemUnitConversion.Updated",
                    JsonSerializer.Serialize(new { icv.ItemId, icv.FromUnitId, icv.ToUnitId, Factor = icv.Factor }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "ItemUnitConversion.Activated" : "ItemUnitConversion.Deactivated",
                    JsonSerializer.Serialize(new { icv.ItemId, icv.FromUnitId, icv.ToUnitId, icv.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is AttributeDefinition ad)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "AttributeDefinition.Created",
                    JsonSerializer.Serialize(new { ad.Code, ad.Name, DataType = ad.DataType.ToString(), ad.UnitId, ad.IsRequired }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("UnitId").IsModified || entry.Property("IsRequired").IsModified)
            {
                yield return new PendingChange(entry, "AttributeDefinition.Updated",
                    JsonSerializer.Serialize(new { ad.Code, ad.Name, ad.UnitId, ad.IsRequired }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "AttributeDefinition.Activated" : "AttributeDefinition.Deactivated",
                    JsonSerializer.Serialize(new { ad.Code, ad.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ItemAttributeValue av)
        {
            yield return new PendingChange(entry, "ItemAttributeValue.Updated",
                JsonSerializer.Serialize(new { av.ItemId, av.AttributeDefinitionId, Action = entry.State == EntityState.Deleted ? "Cleared" : "Set" }, AuditJson));
            yield break;
        }

        if (entry.Entity is Unit unit)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Unit.Created", JsonSerializer.Serialize(new
                {
                    Before = (object?)null,
                    After = new { unit.Code, unit.Name, unit.Symbol, Dimension = unit.Dimension.ToString(), unit.PrecisionScale, unit.IsActive }
                }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("Symbol").IsModified || entry.Property("PrecisionScale").IsModified)
            {
                yield return new PendingChange(entry, "Unit.Updated", JsonSerializer.Serialize(new
                {
                    Code = unit.Code,
                    Before = new
                    {
                        Name = entry.Property("Name").OriginalValue,
                        Symbol = entry.Property("Symbol").OriginalValue,
                        PrecisionScale = entry.Property("PrecisionScale").OriginalValue
                    },
                    After = new
                    {
                        Name = entry.Property("Name").CurrentValue,
                        Symbol = entry.Property("Symbol").CurrentValue,
                        PrecisionScale = entry.Property("PrecisionScale").CurrentValue
                    }
                }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Unit.Activated" : "Unit.Deactivated",
                    JsonSerializer.Serialize(new
                    {
                        Code = unit.Code,
                        Before = new { IsActive = entry.Property("IsActive").OriginalValue },
                        After = new { IsActive = entry.Property("IsActive").CurrentValue }
                    }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is UnitConversion conversion)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "UnitConversion.Created", JsonSerializer.Serialize(new
                {
                    Before = (object?)null,
                    After = new { conversion.FromUnitId, conversion.ToUnitId, conversion.Factor, conversion.IsActive }
                }, AuditJson));
                yield break;
            }
            if (entry.Property("Factor").IsModified)
            {
                yield return new PendingChange(entry, "UnitConversion.Updated", JsonSerializer.Serialize(new
                {
                    conversion.FromUnitId,
                    conversion.ToUnitId,
                    Before = new { Factor = entry.Property("Factor").OriginalValue },
                    After = new { Factor = entry.Property("Factor").CurrentValue }
                }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "UnitConversion.Activated" : "UnitConversion.Deactivated",
                    JsonSerializer.Serialize(new
                    {
                        conversion.FromUnitId,
                        conversion.ToUnitId,
                        Before = new { IsActive = entry.Property("IsActive").OriginalValue },
                        After = new { IsActive = entry.Property("IsActive").CurrentValue }
                    }, AuditJson));
            }
            yield break;
        }

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
        var orgIds = entries.Select(e => ResolveEntityOrganizationId(e.Entity))
            .Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();

        foreach (var orgId in orgIds)
        {
            var addedOrg = entries.FirstOrDefault(e => e.Entity is Organization o && o.Id == orgId && e.State == EntityState.Added);
            if (addedOrg is not null) continue; // Insert lock retained by our transaction.

            await using DbCommand command = Database.GetDbConnection().CreateCommand();
            command.Transaction = Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = "SELECT [IsActive] FROM [organization].[Organization] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = @id";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = orgId;
            command.Parameters.Add(parameter);
            var storedActive = await command.ExecuteScalarAsync(cancellationToken);
            if (storedActive is not bool active)
                throw new InvalidOperationException(OrganizationSiteErrors.OrganizationNotFound);

            var orgEntry = entries.FirstOrDefault(e => e.Entity is Organization o && o.Id == orgId);
            var effectiveActive = orgEntry is not null && orgEntry.Property("IsActive").IsModified
                ? ((Organization)orgEntry.Entity).IsActive
                : active;

            if (orgEntry is not null && orgEntry.Property("Name").IsModified && !effectiveActive)
                throw new InvalidOperationException(OrganizationSiteErrors.OrganizationInactive);

            var siteEntries = entries.Where(e => e.Entity is Site s && s.OrganizationId == orgId).ToArray();
            if (siteEntries.Length > 0 && !effectiveActive
                && (!active || siteEntries.Any(e => ((Site)e.Entity).IsActive)))
                throw new InvalidOperationException(OrganizationSiteErrors.OrganizationInactive);

            if (orgEntry is not null && orgEntry.Property("IsActive").IsModified && !((Organization)orgEntry.Entity).IsActive)
            {
                var parent = (Organization)orgEntry.Entity;
                var pendingInactive = siteEntries.Where(e => !((Site)e.Entity).IsActive).Select(e => ((Site)e.Entity).Id).ToArray();
                if (await Sites.AnyAsync(s => s.OrganizationId == parent.Id && s.IsActive && !pendingInactive.Contains(s.Id), cancellationToken)
                    || siteEntries.Any(e => ((Site)e.Entity).IsActive))
                    throw new InvalidOperationException(OrganizationSiteErrors.OrganizationHasActiveSites);
            }

            var unitEntries = entries.Where(e => e.Entity is Unit u && u.OrganizationId == orgId).ToArray();
            if (unitEntries.Any(e => ((Unit)e.Entity).IsActive || e.State == EntityState.Added || e.Property("Name").IsModified || e.Property("Symbol").IsModified || e.Property("PrecisionScale").IsModified) && !effectiveActive)
                throw new InvalidOperationException(UnitErrors.ParentOrganizationInactive);

            var convEntries = entries.Where(e => e.Entity is UnitConversion c && c.OrganizationId == orgId).ToArray();
            if (convEntries.Any(e => ((UnitConversion)e.Entity).IsActive || e.State == EntityState.Added || e.Property("Factor").IsModified) && !effectiveActive)
                throw new InvalidOperationException(UnitErrors.ParentOrganizationInactive);

            var itemMasterChanged = entries.Any(e => e.Entity is ClassificationSet or Classification or Item or ItemClassification or ItemUnitConversion or AttributeDefinition or ItemAttributeValue
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added
                    || e.State == EntityState.Deleted
                    || (e.Entity is Item it && (it.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || (e.Entity is Classification cl && (cl.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || (e.Entity is ClassificationSet cs && (cs.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || (e.Entity is ItemUnitConversion ic && (ic.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || (e.Entity is AttributeDefinition ad && (ad.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || e.Entity is ItemAttributeValue));
            if (itemMasterChanged && !effectiveActive)
                throw new InvalidOperationException(ItemErrors.ParentOrganizationInactive);

            var bomChanged = entries.Any(e => e.Entity is ContextDimension or Bom or BomVersion or BomInput or BomOutput or BomApplicability or BomApplicabilityCondition
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added
                    || e.State == EntityState.Deleted
                    || (e.Entity is Bom b && (b.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || (e.Entity is ContextDimension cd && (cd.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || e.Entity is BomVersion or BomInput or BomOutput or BomApplicability or BomApplicabilityCondition));
            if (bomChanged && !effectiveActive)
                throw new InvalidOperationException(BomErrors.ParentOrganizationInactive);
        }
    }
}
