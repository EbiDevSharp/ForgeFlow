using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Events;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using ForgeFlow.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class SaveChangesInterceptorPipelineTests
{
    private sealed class TestActorAccessor(TrustedActor? actor) : ICurrentActorAccessor
    {
        public TrustedActor? CurrentActor => actor;
    }

    private sealed class TestOrgAccessor(long? orgId) : ICurrentOrganizationAccessor
    {
        public long? CurrentOrganizationId => orgId;
    }

    private sealed class RecordingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> DispatchedEvents { get; } = [];

        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            DispatchedEvents.Add(domainEvent);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            DispatchedEvents.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    private sealed class SuppressDatabaseExecutionInterceptor(Action<DbContext>? onSaving = null) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            onSaving?.Invoke(eventData.Context!);
            return InterceptionResult<int>.SuppressWithResult(1);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            onSaving?.Invoke(eventData.Context!);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }

    private static AuthDbContext CreateOfflineAuthDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=True;Encrypt=True")
            .Options;
        return new AuthDbContext(options);
    }

    private static OrganizationDbContext CreateOfflineOrgDbContext(
        ICurrentOrganizationAccessor? orgAccessor = null,
        ICurrentActorAccessor? actorAccessor = null,
        IDomainEventDispatcher? dispatcher = null,
        TimeProvider? timeProvider = null,
        Action<DbContext>? onSaving = null)
    {
        var builder = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=True;Encrypt=True");

        builder.AddInterceptors(
            new TenantEnforcementSaveChangesInterceptor(),
            new AuditingSaveChangesInterceptor(actorAccessor, timeProvider),
            new DomainEventsSaveChangesInterceptor(dispatcher, actorAccessor: actorAccessor),
            new SuppressDatabaseExecutionInterceptor(onSaving));

        return new OrganizationDbContext(builder.Options, timeProvider, orgAccessor);
    }

    [Fact]
    public void AuthImmutableHistory_Interceptor_Blocks_Deleted_Entity_On_Sync_Save()
    {
        using var db = CreateOfflineAuthDbContext();
        var account = new ApplicationAccount
        {
            Id = 1,
            UserName = "user1",
            NormalizedUserName = "USER1",
            PasswordHash = "h",
            SecurityStamp = "s",
            ConcurrencyStamp = "c"
        };
        db.Attach(account);
        db.Entry(account).State = EntityState.Deleted;

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal("Auth.ImmutableHistory", ex.Message);
    }

    [Fact]
    public async Task AuthImmutableHistory_Interceptor_Blocks_Deleted_Entity_On_Async_Save()
    {
        using var db = CreateOfflineAuthDbContext();
        var account = new ApplicationAccount
        {
            Id = 2,
            UserName = "user2",
            NormalizedUserName = "USER2",
            PasswordHash = "h",
            SecurityStamp = "s",
            ConcurrencyStamp = "c"
        };
        db.Attach(account);
        db.Entry(account).State = EntityState.Deleted;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal("Auth.ImmutableHistory", ex.Message);
    }

    [Fact]
    public void AuthImmutableHistory_Interceptor_Blocks_Modified_UserName()
    {
        using var db = CreateOfflineAuthDbContext();
        var account = new ApplicationAccount
        {
            Id = 10,
            UserName = "initial.user",
            NormalizedUserName = "INITIAL.USER",
            PasswordHash = "hash",
            SecurityStamp = "stamp",
            ConcurrencyStamp = "stamp"
        };
        db.Attach(account);
        account.UserName = "changed.user";

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal("Auth.ImmutableHistory", ex.Message);
    }

    [Fact]
    public void TenantEnforcement_Interceptor_Blocks_Zero_Or_Negative_OrganizationId()
    {
        using var db = CreateOfflineOrgDbContext();
        var unit = Unit.Create(10, "KG", "Kilogram", "kg", UnitDimension.Mass, 2);
        db.Units.Add(unit);
        db.Entry(unit).Property("OrganizationId").CurrentValue = 0L;

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(OrganizationAccessErrors.AccessDenied, ex.Message);
    }

    [Fact]
    public void TenantEnforcement_Interceptor_Blocks_CrossTenant_Violation_Against_Accessor()
    {
        var orgAccessor = new TestOrgAccessor(100);
        using var db = CreateOfflineOrgDbContext(orgAccessor);

        var unit = Unit.Create(200, "MTR", "Meter", "m", UnitDimension.Length, 2);
        db.Units.Add(unit);

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(OrganizationAccessErrors.AccessDenied, ex.Message);
    }

    [Fact]
    public void Auditing_Interceptor_Rejects_Save_When_Actor_Is_Missing()
    {
        using var db = CreateOfflineOrgDbContext();
        var unit = Unit.Create(100, "BOX", "Box", "bx", UnitDimension.Count, 0);
        db.Units.Add(unit);

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal(OrganizationAccessErrors.ActorRequired, ex.Message);
    }

    [Fact]
    public void Auditing_Interceptor_Stamps_CreatedAt_And_CreatedByActorId_Accurately()
    {
        var trustedActor = new TrustedActor(88, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var actorAccessor = new TestActorAccessor(trustedActor);

        long createdByActorId = 0;
        DateTime? createdAtUtc = null;
        DateTime? updatedAtUtc = null;
        long? updatedByActorId = null;

        using var db = CreateOfflineOrgDbContext(
            actorAccessor: actorAccessor,
            onSaving: context =>
            {
                var entry = context.ChangeTracker.Entries().First(e => e.Entity is Unit);
                createdByActorId = (long)entry.Property("CreatedByActorId").CurrentValue!;
                createdAtUtc = (DateTime?)entry.Property("CreatedAtUtc").CurrentValue;
                updatedAtUtc = (DateTime?)entry.Property("UpdatedAtUtc").CurrentValue;
                updatedByActorId = (long?)entry.Property("UpdatedByActorId").CurrentValue;
            });

        var unit = Unit.Create(100, "L", "Liter", "l", UnitDimension.Volume, 3);
        db.Units.Add(unit);

        db.SaveChanges();

        Assert.Equal(88L, createdByActorId);
        Assert.NotNull(createdAtUtc);
        Assert.Null(updatedAtUtc);
        Assert.Null(updatedByActorId);
    }

    [Fact]
    public async Task DomainEvents_Interceptor_Dispatches_Events_And_Clears_Queue()
    {
        var trustedActor = new TrustedActor(99, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var actorAccessor = new TestActorAccessor(trustedActor);
        var dispatcher = new RecordingDispatcher();

        using var db = CreateOfflineOrgDbContext(actorAccessor: actorAccessor, dispatcher: dispatcher);
        var now = new DateTime(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc);
        var po = ProductionOrder.CreateDraft(
            organizationId: 10,
            siteId: 20,
            orderNumber: "PO-DISP-01",
            itemId: 30,
            requestedQuantity: 50m,
            requestedUnitId: 40,
            plannedStartDate: new DateOnly(2026, 11, 16),
            plannedDueDate: new DateOnly(2026, 11, 20),
            bomId: 1,
            bomCodeSnapshot: "BOM-001",
            bomVersionId: 2,
            bomVersionNumberSnapshot: 1,
            baseOutputQuantitySnapshot: 10m,
            baseOutputUnitIdSnapshot: 40,
            notes: "Test PO");

        po.Release(now);
        Assert.Equal(2, po.DomainEvents.Count);

        db.ProductionOrders.Add(po);

        await db.SaveChangesAsync();

        Assert.Equal(2, dispatcher.DispatchedEvents.Count);
        Assert.Contains(dispatcher.DispatchedEvents, e => e is ProductionOrderReleasedEvent released && released.OrderNumber == "PO-DISP-01");
        Assert.Empty(po.DomainEvents);
    }
}
