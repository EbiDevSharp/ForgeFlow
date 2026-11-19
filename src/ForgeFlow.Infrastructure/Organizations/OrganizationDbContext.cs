using System.Data.Common;
using System.Text.Json;
using System.Text.Encodings.Web;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>
/// Create/dispose through IDbContextFactory for each operation, never keep on a Blazor circuit.
/// This infrastructure boundary does not authenticate or authorize: secured use cases must do that first.
/// </summary>
public sealed class OrganizationDbContext(
    DbContextOptions<OrganizationDbContext> options,
    TimeProvider? clock = null,
    ICurrentOrganizationAccessor? organizationAccessor = null)
    : DbContext(options), IAuditTrailSink
{
    private long? _currentOrganizationId;

    /// <summary>
    /// Context-scoped active OrganizationId.
    /// Dynamically parameterized per query in EF Core HasQueryFilter.
    /// Evaluates explicit context property, then scoped accessor, then ambient accessor.
    /// </summary>
    public long? CurrentOrganizationId
    {
        get => _currentOrganizationId ?? organizationAccessor?.CurrentOrganizationId ?? AmbientOrganizationAccessor.Current;
        set => _currentOrganizationId = value;
    }

    public long CurrentOrganizationFilterId => CurrentOrganizationId ?? -1L;
    internal TimeProvider? Clock => clock;
    private bool failed;
    private bool inOperationScope;
    private TrustedActor? currentActor;
    private (TrustedActor Actor, DateTime Now, OrganizationAuditChangeSerializer.PendingChange[] Changes)? pendingAudit;
    private readonly HashSet<string> _dispatchedDomainEventCodes = [];

    internal bool Failed => failed;
    internal HashSet<string> DispatchedDomainEventCodes => _dispatchedDomainEventCodes;
    internal (TrustedActor Actor, DateTime Now, OrganizationAuditChangeSerializer.PendingChange[] Changes)? PendingAudit
    {
        get => pendingAudit;
        set => pendingAudit = value;
    }
    internal TrustedActor? CurrentActor => currentActor;
    internal bool IsSavingAuditBatch { get; set; }

    void IAuditTrailSink.Record(long organizationId, long? siteId, string eventCode, string detailsJson)
    {
        var actor = currentActor;
        if (actor is null) return;
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        OrganizationChanges.Add(new OrganizationChange(
            organizationId,
            siteId,
            actor.ActorId,
            eventCode,
            now,
            actor.CorrelationId,
            detailsJson));
        _dispatchedDomainEventCodes.Add(eventCode);
    }

    internal void DispatchDomainEventsSync(
        IDomainEventDispatcher? dispatcher,
        IAuditTrailSinkAccessor? sinkAccessor,
        TrustedActor? actor)
        => DispatchDomainEventsAsync(dispatcher, sinkAccessor, actor, CancellationToken.None).GetAwaiter().GetResult();

    internal async Task DispatchDomainEventsAsync(
        IDomainEventDispatcher? dispatcher,
        IAuditTrailSinkAccessor? sinkAccessor,
        TrustedActor? actor,
        CancellationToken cancellationToken)
    {
        var entries = ChangeTracker.Entries<ForgeFlow.Domain.Common.IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .ToList();

        if (entries.Count == 0)
            return;

        var events = entries.SelectMany(e => e.Entity.DomainEvents).ToList();

        foreach (var entry in entries)
        {
            foreach (var evt in entry.Entity.DomainEvents)
            {
                if (evt is ForgeFlow.Domain.Common.IAuditableDomainEvent auditable)
                {
                    _dispatchedDomainEventCodes.Add(auditable.EventCode);
                }
            }
            entry.Entity.ClearDomainEvents();
        }

        IDisposable? scope = null;
        if (sinkAccessor is AmbientAuditTrailSinkAccessor ambient)
        {
            scope = ambient.BeginScope(this);
        }

        try
        {
            if (dispatcher is not null)
            {
                await dispatcher.DispatchAsync(events, cancellationToken);
            }
            else if (actor is not null)
            {
                var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
                foreach (var evt in events.OfType<ForgeFlow.Domain.Common.IAuditableDomainEvent>())
                {
                    OrganizationChanges.Add(new OrganizationChange(
                        evt.OrganizationId,
                        evt.SiteId,
                        actor.ActorId,
                        evt.EventCode,
                        now,
                        actor.CorrelationId,
                        evt.BuildAuditJson()));
                }
            }
        }
        finally
        {
            scope?.Dispose();
        }
    }

    internal void MarkFailed()
    {
        failed = true;
        pendingAudit = null;
        ChangeTracker.Clear();
    }
    // Stored JSON only, never HTML. Keep valid 200-character Persian before/after names within nvarchar(2000).
    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();
    public DbSet<Currency> Currencies => Set<Currency>();
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
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionOrderInput> ProductionOrderInputs => Set<ProductionOrderInput>();
    public DbSet<ProductionOrderOutput> ProductionOrderOutputs => Set<ProductionOrderOutput>();
    public DbSet<ProductionOrderContext> ProductionOrderContexts => Set<ProductionOrderContext>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<InventoryLedgerEntry> InventoryLedgerEntries => Set<InventoryLedgerEntry>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<ItemLot> ItemLots => Set<ItemLot>();
    public DbSet<QualityInspection> QualityInspections => Set<QualityInspection>();
    public DbSet<QualityInspectionLine> QualityInspectionLines => Set<QualityInspectionLine>();
    public DbSet<QualityParameter> QualityParameters => Set<QualityParameter>();
    public DbSet<QualitySpec> QualitySpecs => Set<QualitySpec>();
    public DbSet<QualitySpecLine> QualitySpecLines => Set<QualitySpecLine>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<Routing> Routings => Set<Routing>();
    public DbSet<RoutingOperation> RoutingOperations => Set<RoutingOperation>();
    public DbSet<ProductionOrderOperation> ProductionOrderOperations => Set<ProductionOrderOperation>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<DeliveryOrder> DeliveryOrders => Set<DeliveryOrder>();
    public DbSet<DeliveryOrderLine> DeliveryOrderLines => Set<DeliveryOrderLine>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<BusinessPartner> BusinessPartners => Set<BusinessPartner>();
    public DbSet<BusinessPartnerRoleDefinition> BusinessPartnerRoleDefinitions => Set<BusinessPartnerRoleDefinition>();
    public DbSet<BusinessPartnerRoleAssignment> BusinessPartnerRoleAssignments => Set<BusinessPartnerRoleAssignment>();
    public DbSet<IranProvince> IranProvinces => Set<IranProvince>();
    public DbSet<IranCity> IranCities => Set<IranCity>();
    public DbSet<OrganizationChange> OrganizationChanges => Set<OrganizationChange>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        var coreExt = optionsBuilder.Options.FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>();
        var existing = coreExt?.Interceptors ?? [];
        if (!existing.OfType<ForgeFlow.Infrastructure.Persistence.Interceptors.TenantEnforcementSaveChangesInterceptor>().Any())
            optionsBuilder.AddInterceptors(new ForgeFlow.Infrastructure.Persistence.Interceptors.TenantEnforcementSaveChangesInterceptor());
        if (!existing.OfType<ForgeFlow.Infrastructure.Persistence.Interceptors.AuditingSaveChangesInterceptor>().Any())
            optionsBuilder.AddInterceptors(new ForgeFlow.Infrastructure.Persistence.Interceptors.AuditingSaveChangesInterceptor());
        if (!existing.OfType<ForgeFlow.Infrastructure.Persistence.Interceptors.DomainEventsSaveChangesInterceptor>().Any())
            optionsBuilder.AddInterceptors(new ForgeFlow.Infrastructure.Persistence.Interceptors.DomainEventsSaveChangesInterceptor());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(OrganizationDbContext).Assembly,
            t => typeof(Configurations.IOrganizationEntityConfiguration).IsAssignableFrom(t) && !t.IsAbstract);
        ApplyOrganizationQueryFilters(modelBuilder);
    }

    private void ApplyOrganizationQueryFilters(ModelBuilder modelBuilder)
    {
        var method = typeof(OrganizationDbContext).GetMethod(
            nameof(ConfigureOrganizationQueryFilter),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType != typeof(Organization) &&
                entityType.ClrType != typeof(OrganizationChange) &&
                (typeof(ForgeFlow.Domain.Common.IOrganizationEntity).IsAssignableFrom(entityType.ClrType) ||
                 (entityType.FindProperty("OrganizationId") is { ClrType: { } clrType } && clrType == typeof(long))))
            {
                method.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ConfigureOrganizationQueryFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class
        => modelBuilder.Entity<TEntity>().HasQueryFilter(
            e => EF.Property<long>(e, "OrganizationId") == CurrentOrganizationFilterId);

    /// <summary>
    /// Trusted caller supplies actor only after current permission evaluation. No provider or fake authorization.
    /// Owns its transaction; dispose after any failure. No transparent retry after an unknown commit outcome.
    /// </summary>
    public Task<int> SaveChangesAsync(TrustedActor actor, CancellationToken cancellationToken = default)
        => SaveAttributed(actor, true, cancellationToken);

    public Task<int> SaveInOperation(TrustedActor actor, CancellationToken ct = default) => SaveAttributed(actor, false, ct);

    private async Task<int> SaveAttributed(TrustedActor actor, bool ownsTransaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (failed || (ownsTransaction ? Database.CurrentTransaction is not null : Database.CurrentTransaction is null))
            throw new InvalidOperationException(OrganizationAccessErrors.PersistenceUnavailable);

        var previousActor = currentActor;
        var previousInOp = inOperationScope;
        currentActor = actor;
        inOperationScope = !ownsTransaction;

        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var count = await base.SaveChangesAsync(true, cancellationToken);
            if (transaction is not null)
            {
                try { await transaction.CommitAsync(cancellationToken); }
                catch (Exception ex) { throw new OrganizationCommitOutcomeUnknownException(ex); }
            }
            return count;
        }
        catch
        {
            MarkFailed();
            throw;
        }
        finally
        {
            currentActor = previousActor;
            inOperationScope = previousInOp;
        }
    }



    internal void FlushPendingAuditChangesSync()
    {
        if (pendingAudit is not { } batch)
        {
            _dispatchedDomainEventCodes.Clear();
            return;
        }
        pendingAudit = null;
        if (batch.Changes.Length == 0)
        {
            _dispatchedDomainEventCodes.Clear();
            return;
        }

        IsSavingAuditBatch = true;
        try
        {
            EnqueueOrganizationChanges(batch.Actor, batch.Now, batch.Changes);
            base.SaveChanges(true);
        }
        catch (DbUpdateException ex) when (inOperationScope) { throw new OrganizationAuditWriteException(ex); }
        finally
        {
            IsSavingAuditBatch = false;
            _dispatchedDomainEventCodes.Clear();
            ClearTrackedDomainEvents();
        }
    }

    internal async Task FlushPendingAuditChangesAsync(CancellationToken cancellationToken)
    {
        if (pendingAudit is not { } batch)
        {
            _dispatchedDomainEventCodes.Clear();
            ClearTrackedDomainEvents();
            return;
        }
        pendingAudit = null;
        if (batch.Changes.Length == 0)
        {
            _dispatchedDomainEventCodes.Clear();
            ClearTrackedDomainEvents();
            return;
        }

        IsSavingAuditBatch = true;
        try
        {
            EnqueueOrganizationChanges(batch.Actor, batch.Now, batch.Changes);
            await base.SaveChangesAsync(true, cancellationToken);
        }
        catch (DbUpdateException ex) when (inOperationScope) { throw new OrganizationAuditWriteException(ex); }
        finally
        {
            IsSavingAuditBatch = false;
            _dispatchedDomainEventCodes.Clear();
            ClearTrackedDomainEvents();
        }
    }

    private void ClearTrackedDomainEvents()
    {
        foreach (var entry in ChangeTracker.Entries<ForgeFlow.Domain.Common.IHasDomainEvents>())
            entry.Entity.ClearDomainEvents();
    }

    private void EnqueueOrganizationChanges(TrustedActor actor, DateTime now, OrganizationAuditChangeSerializer.PendingChange[] changes)
    {
        foreach (var entry in ChangeTracker.Entries<ForgeFlow.Domain.Common.IHasDomainEvents>())
        {
            foreach (var evt in entry.Entity.DomainEvents.OfType<ForgeFlow.Domain.Common.IAuditableDomainEvent>())
            {
                OrganizationChanges.Add(new OrganizationChange(
                    evt.OrganizationId, evt.SiteId, actor.ActorId, evt.EventCode, now, actor.CorrelationId, evt.BuildAuditJson()));
            }
        }
        foreach (var change in changes)
        {
            var orgId = ResolveEntityOrganizationId(change.Entry.Entity);
            long? siteId = ResolveEntitySiteId(change.Entry.Entity);
            OrganizationChanges.Add(new OrganizationChange(orgId, siteId, actor.ActorId, change.EventCode,
                now, actor.CorrelationId, change.Json));
        }
    }

    internal static void ApplyAuditMetadata(EntityEntry[] entries, TrustedActor? actor, DateTime now)
    {
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Deleted)
                continue;

            if (entry.Metadata.FindProperty("CreatedAtUtc") is null)
                continue;

            if (entry.State == EntityState.Added)
            {
                entry.Property("CreatedAtUtc").CurrentValue = now;
                entry.Property("CreatedByActorId").CurrentValue = actor?.ActorId ?? 1L;
                entry.Property("UpdatedAtUtc").CurrentValue = null;
                entry.Property("UpdatedByActorId").CurrentValue = null;
            }
            else
            {
                if (now < (DateTime)entry.Property("CreatedAtUtc").CurrentValue!)
                    throw new InvalidOperationException(OrganizationAccessErrors.PersistenceUnavailable);
                entry.Property("UpdatedAtUtc").CurrentValue = now;
                entry.Property("UpdatedByActorId").CurrentValue = actor?.ActorId ?? (long)entry.Property("CreatedByActorId").CurrentValue!;
            }
        }
    }

    internal static void ValidateEntries(EntityEntry[] entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Entity is not (Organization or Site or Unit or UnitConversion or Currency
                or ClassificationSet or Classification or Item or ItemClassification
                or ItemUnitConversion or AttributeDefinition or ItemAttributeValue
                or ContextDimension or Bom or BomVersion or BomInput or BomOutput
                or BomApplicability or BomApplicabilityCondition
                or ProductionOrder or ProductionOrderInput or ProductionOrderOutput or ProductionOrderContext
                or Warehouse or InventoryTransaction or InventoryLedgerEntry or InventoryBalance or StockReservation
                or ItemLot or QualityInspection or QualityInspectionLine
                or QualityParameter or QualitySpec or QualitySpecLine
                or Supplier or PurchaseOrder or PurchaseOrderLine
                or WorkCenter or Routing or RoutingOperation or ProductionOrderOperation
                or Customer or SalesOrder or SalesOrderLine or DeliveryOrder or DeliveryOrderLine
                or DocumentSequence or BusinessPartner or BusinessPartnerRoleAssignment))
            {
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
            }
            if (entry.State == EntityState.Deleted && entry.Entity is not (ItemClassification or ItemAttributeValue
                or BomInput or BomOutput or BomApplicability or BomApplicabilityCondition
                or ProductionOrderInput or ProductionOrderOutput or ProductionOrderContext
                or QualityInspectionLine or QualitySpecLine or PurchaseOrderLine or SalesOrderLine
                or RoutingOperation or ProductionOrderOperation or BusinessPartnerRoleAssignment))
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
            if (entry.State == EntityState.Modified)
            {
                var allowedProps = entry.Entity switch
                {
                    Unit => new[] { "Name", "Symbol", "PrecisionScale", "IsActive" },
                    UnitConversion or ItemUnitConversion => new[] { "Factor", "IsActive" },
                    Currency => new[] { "Name", "Symbol", "PrecisionScale", "IsActive" },
                    ClassificationSet => new[] { "Name", "IsMandatory", "AllowMultiple", "IsActive" },
                    Classification => new[] { "Name", "ParentClassificationId", "IsActive" },
                    Item => new[] { "Name", "Description", "BaseUnitId", "CanBePurchased", "CanBeManufactured", "CanBeSold", "CanBeConsumed", "RequiresLotTracking", "QualityControlMode", "IsActive" },
                    AttributeDefinition => new[] { "Name", "UnitId", "IsRequired", "IsActive" },
                    ItemAttributeValue => new[] { "TextValue", "DecimalValue", "BooleanValue" },
                    ContextDimension => new[] { "Name", "IsActive" },
                    Bom => new[] { "Name", "IsActive" },
                    BomVersion => new[] { "Status", "BaseOutputQuantity", "BaseOutputUnitId", "EffectiveFromDate", "EffectiveToDate", "Notes" },
                    BomApplicability => new[] { "Name", "IsActive" },
                    ProductionOrder => new[] { "Status", "RequestedQuantity", "RequestedUnitId", "PlannedStartDate", "PlannedDueDate",
                        "BomId", "BomCodeSnapshot", "BomVersionId", "BomVersionNumberSnapshot", "BaseOutputQuantitySnapshot", "BaseOutputUnitIdSnapshot",
                        "RoutingId", "RoutingCodeSnapshot",
                        "ActualProducedQuantity", "ActualScrapQuantity", "Notes", "CancellationReason",
                        "ReleasedAtUtc", "StartedAtUtc", "CompletedAtUtc", "CancelledAtUtc" },
                    Warehouse => new[] { "Name", "IsDefaultForSite", "IsActive" },
                    InventoryBalance => new[] { "OnHandBaseQuantity", "BlockedBaseQuantity", "ReservedBaseQuantity", "LastMovedAtUtc" },
                    StockReservation => new[] { "ConsumedBaseQuantity", "Status", "ReleasedAtUtc" },
                    InventoryTransaction or InventoryLedgerEntry => Array.Empty<string>(),
                    ItemLot => new[] { "ManufactureDate", "ExpirationDate", "Notes" },
                    QualityInspection => new[] { "AcceptedBaseQuantity", "RejectedBaseQuantity", "RemainingBlockedBaseQuantity", "Status", "DefectReasonCode", "Notes", "CertificateNumber", "InspectedAtUtc", "InspectedByActorId" },
                    QualityInspectionLine => Array.Empty<string>(),
                    QualityParameter => new[] { "Name", "Symbol", "UnitSymbol", "DecimalPlaces", "DefaultTestMethod", "IsActive" },
                    QualitySpec => new[] { "Name", "Checkpoint", "Notes", "IsActive" },
                    QualitySpecLine => Array.Empty<string>(),
                    Supplier => new[] { "Name", "ContactName", "Phone", "Email", "Address", "Notes", "IsActive" },
                    PurchaseOrder => new[] { "SupplierId", "OrderDate", "ExpectedDeliveryDate", "TargetWarehouseId", "CurrencyCode", "Notes", "CancellationReason", "Status", "ApprovedAtUtc", "ClosedAtUtc", "CancelledAtUtc" },
                    PurchaseOrderLine => new[] { "ReceivedBaseQuantity" },
                    WorkCenter => new[] { "Name", "WorkCenterType", "CapacityUnitsPerHour", "DailyAvailableHours", "HourlyCostRate", "Notes", "IsActive" },
                    Routing => new[] { "Name", "BomVersionId", "Status", "IsDefault", "Notes", "ActivatedAtUtc" },
                    RoutingOperation => Array.Empty<string>(),
                    ProductionOrderOperation => new[] { "Status", "ActualSetupMinutes", "ActualRunMinutes", "CompletedQuantity", "ScrapQuantity", "OperatorNotes", "StartedAtUtc", "CompletedAtUtc" },
                    Customer => new[] { "Name", "ContactName", "Phone", "Email", "ShippingAddress", "TaxCode", "Notes", "IsActive" },
                    SalesOrder => new[] { "CustomerId", "OrderDate", "RequestedDeliveryDate", "SourceWarehouseId", "CurrencyCode", "ShippingAddressSnapshot", "Notes", "CancellationReason", "Status", "ApprovedAtUtc", "ClosedAtUtc", "CancelledAtUtc" },
                    SalesOrderLine => new[] { "DispatchedBaseQuantity" },
                    DeliveryOrder => new[] { "Status", "ReversedAtUtc", "ReversalReason" },
                    DeliveryOrderLine => Array.Empty<string>(),
                    DocumentSequence => new[] { "Prefix", "Separator", "YearMode", "PaddingDigits", "NextNumber", "LastAllocatedYear", "AllowManualOverride", "IsActive" },
                    BusinessPartner => new[] { "PersonType", "Name", "FirstName", "LastName", "NationalCode", "CompanyName", "NationalLegalId", "EconomicCode", "Mobile", "Phone", "Email", "Website", "Country", "Province", "City", "Address", "PostalCode", "Notes", "IsActive" },
                    _ => new[] { "Name", "IsActive" }
                };
                if (entry.Properties.Any(p => p.IsModified
                    && p.Metadata.Name is not ("UpdatedAtUtc" or "UpdatedByActorId" or "RowVersion")
                    && !allowedProps.Contains(p.Metadata.Name, StringComparer.Ordinal)))
                    throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
            }
        }
    }

    internal static long ResolveEntityOrganizationId(object entity)
        => OrganizationAuditChangeSerializer.ResolveEntityOrganizationId(entity);

    internal long? ResolveEntitySiteId(object entity)
        => OrganizationAuditChangeSerializer.ResolveEntitySiteId(this, entity);

    internal static IEnumerable<OrganizationAuditChangeSerializer.PendingChange> CaptureAllChanges(EntityEntry[] entries, HashSet<string>? dispatchedCodes = null)
        => OrganizationAuditChangeSerializer.CaptureAllChanges(entries, dispatchedCodes);

    internal async Task LockAndValidateParents(EntityEntry[] entries, CancellationToken cancellationToken)
    {
        var orgIds = entries.Select(e => ResolveEntityOrganizationId(e.Entity))
            .Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();

        foreach (var orgId in orgIds)
        {
            var addedOrg = entries.FirstOrDefault(e => e.Entity is Organization o && o.Id == orgId && e.State == EntityState.Added);
            if (addedOrg is not null) continue; // Insert lock retained by our transaction.

            if (!Database.IsRelational() || Database.CurrentTransaction is null)
                continue;

            await using DbCommand command = Database.GetDbConnection().CreateCommand();
            command.Transaction = Database.CurrentTransaction.GetDbTransaction();
            command.CommandText = "SELECT [IsActive] FROM [organization].[Organization] WHERE [Id] = @id";
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
                if (await Sites.IgnoreQueryFilters().AnyAsync(s => s.OrganizationId == parent.Id && s.IsActive && !pendingInactive.Contains(s.Id), cancellationToken)
                    || siteEntries.Any(e => ((Site)e.Entity).IsActive))
                    throw new InvalidOperationException(OrganizationSiteErrors.OrganizationHasActiveSites);
            }

            var unitEntries = entries.Where(e => e.Entity is Unit u && u.OrganizationId == orgId).ToArray();
            if (unitEntries.Any(e => ((Unit)e.Entity).IsActive || e.State == EntityState.Added || e.Property("Name").IsModified || e.Property("Symbol").IsModified || e.Property("PrecisionScale").IsModified) && !effectiveActive)
                throw new InvalidOperationException(UnitErrors.ParentOrganizationInactive);

            var convEntries = entries.Where(e => e.Entity is UnitConversion c && c.OrganizationId == orgId).ToArray();
            if (convEntries.Any(e => ((UnitConversion)e.Entity).IsActive || e.State == EntityState.Added || e.Property("Factor").IsModified) && !effectiveActive)
                throw new InvalidOperationException(UnitErrors.ParentOrganizationInactive);

            var currencyEntries = entries.Where(e => e.Entity is Currency c && c.OrganizationId == orgId).ToArray();
            if (currencyEntries.Any(e => ((Currency)e.Entity).IsActive || e.State == EntityState.Added || e.Property("Name").IsModified || e.Property("Symbol").IsModified || e.Property("PrecisionScale").IsModified) && !effectiveActive)
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

            var poChanged = entries.Any(e => e.Entity is ProductionOrder or ProductionOrderInput or ProductionOrderOutput or ProductionOrderContext
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted));
            if (poChanged && !effectiveActive)
                throw new InvalidOperationException(ProductionOrderErrors.ParentOrganizationInactive);

            var invChanged = entries.Any(e => e.Entity is Warehouse or InventoryTransaction or InventoryLedgerEntry or InventoryBalance or StockReservation
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added
                    || (e.Entity is Warehouse wh && (wh.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || e.Entity is InventoryTransaction or InventoryLedgerEntry or InventoryBalance or StockReservation));
            if (invChanged && !effectiveActive)
                throw new InvalidOperationException(InventoryErrors.ParentOrganizationInactive);

            var qualityChanged = entries.Any(e => e.Entity is ItemLot or QualityInspection or QualityInspectionLine
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted));
            if (qualityChanged && !effectiveActive)
                throw new InvalidOperationException(QualityErrors.ParentOrganizationInactive);

            var procurementChanged = entries.Any(e => e.Entity is Supplier or PurchaseOrder or PurchaseOrderLine
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added
                    || e.State == EntityState.Deleted
                    || (e.Entity is Supplier s && (s.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || e.Entity is PurchaseOrder or PurchaseOrderLine));
            if (procurementChanged && !effectiveActive)
                throw new InvalidOperationException(ProcurementErrors.ParentOrganizationInactive);

            var salesChanged = entries.Any(e => e.Entity is Customer or SalesOrder or SalesOrderLine or DeliveryOrder or DeliveryOrderLine
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added
                    || e.State == EntityState.Deleted
                    || (e.Entity is Customer c && (c.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || e.Entity is SalesOrder or SalesOrderLine or DeliveryOrder or DeliveryOrderLine));
            if (salesChanged && !effectiveActive)
                throw new InvalidOperationException(SalesErrors.ParentOrganizationInactive);

            var routingChanged = entries.Any(e => e.Entity is WorkCenter or Routing or RoutingOperation or ProductionOrderOperation
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added
                    || e.State == EntityState.Deleted
                    || (e.Entity is WorkCenter wc && (wc.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || e.Entity is Routing or RoutingOperation or ProductionOrderOperation));
            if (routingChanged && !effectiveActive)
                throw new InvalidOperationException(RoutingErrors.ParentOrganizationInactive);

            var bpChanged = entries.Any(e => (e.Entity is BusinessPartner || e.Entity is BusinessPartnerRoleAssignment)
                && ResolveEntityOrganizationId(e.Entity) == orgId
                && (e.State == EntityState.Added
                    || e.State == EntityState.Deleted
                    || (e.Entity is BusinessPartner bp && (bp.IsActive || e.Properties.Any(p => p.IsModified && p.Metadata.Name != "IsActive")))
                    || e.Entity is BusinessPartnerRoleAssignment));
            if (bpChanged && !effectiveActive)
                throw new InvalidOperationException("BusinessPartner.ParentOrganizationInactive");
        }
    }
}
