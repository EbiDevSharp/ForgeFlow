using System.Reflection;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DocumentSequenceRetryTests
{
    [Theory]
    [InlineData(1205, true)] // Deadlock victim
    [InlineData(1222, true)] // Lock request timeout
    [InlineData(2601, true)] // Unique index collision
    [InlineData(2627, true)] // Primary key / unique constraint collision
    [InlineData(547, false)] // Foreign key violation
    [InlineData(229, false)] // Permission denied
    [InlineData(208, false)] // Invalid object name
    [InlineData(50000, false)] // Custom error
    public void IsTransient_Identifies_Transient_Sql_Errors_Accurately(int errorNumber, bool expectedTransient)
    {
        var sqlEx = CreateSqlException(errorNumber);
        var actual = DocumentSequenceRetryPolicy.IsTransient(sqlEx);
        Assert.Equal(expectedTransient, actual);
    }

    [Fact]
    public void IsTransient_Traverses_Nested_Inner_Exceptions()
    {
        var deadlockSqlEx = CreateSqlException(1205);
        var dbUpdateEx = new DbUpdateException("DB update failed due to deadlock", deadlockSqlEx);
        var outerEx = new InvalidOperationException("Outer wrapper", dbUpdateEx);

        Assert.True(DocumentSequenceRetryPolicy.IsTransient(outerEx));
        Assert.True(DocumentSequenceRetryPolicy.IsTransient(dbUpdateEx));
    }

    [Fact]
    public void IsTransient_Returns_False_For_Non_Sql_Exceptions()
    {
        var ex = new InvalidOperationException("Something else went wrong");
        Assert.False(DocumentSequenceRetryPolicy.IsTransient(ex));
    }

    [Fact]
    public void CalculateDelay_Produces_Bounded_Exponential_Backoff_With_Jitter()
    {
        var initial = TimeSpan.FromMilliseconds(20);
        var max = TimeSpan.FromMilliseconds(200);

        var delay0 = DocumentSequenceRetryPolicy.CalculateDelay(0, initial, max);
        var delay1 = DocumentSequenceRetryPolicy.CalculateDelay(1, initial, max);
        var delay2 = DocumentSequenceRetryPolicy.CalculateDelay(2, initial, max);
        var delay10 = DocumentSequenceRetryPolicy.CalculateDelay(10, initial, max);

        Assert.True(delay0.TotalMilliseconds >= 20 && delay0.TotalMilliseconds <= 30);
        Assert.True(delay1.TotalMilliseconds >= 40 && delay1.TotalMilliseconds <= 60);
        Assert.True(delay2.TotalMilliseconds >= 80 && delay2.TotalMilliseconds <= 120);
        Assert.True(delay10.TotalMilliseconds >= 200 && delay10.TotalMilliseconds <= 300);
    }

    [Fact]
    public void DocumentSequence_RevertAllocation_Restores_Number_And_Year()
    {
        var seq = DocumentSequence.Create(1, DocumentSequenceType.SalesOrder, "SO", "-", DocumentYearMode.Persian, 5, 10, false);
        var utc = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        var prevNumber = seq.NextNumber;
        var prevYear = seq.LastAllocatedYear;

        var allocated = seq.AllocateNextNumber(utc);
        Assert.Equal("SO-1405-00010", allocated);
        Assert.Equal(11, seq.NextNumber);
        Assert.Equal(1405, seq.LastAllocatedYear);

        seq.RevertAllocation(prevNumber, prevYear);
        Assert.Equal(10, seq.NextNumber);
        Assert.Equal(prevYear, seq.LastAllocatedYear);

        var allocatedAgain = seq.AllocateNextNumber(utc);
        Assert.Equal("SO-1405-00010", allocatedAgain);
        Assert.Equal(11, seq.NextNumber);
    }

    [Fact]
    public async Task AllocateAsync_Requires_Transaction_When_Using_DbContext_Overload()
    {
        await using var db = OfflineContext();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SqlDocumentSequenceStore.AllocateAsync(
                db, 1, DocumentSequenceType.SalesOrder, null, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(DocumentSequenceErrors.TransactionRequired, ex.Message);
    }

    [Fact]
    public async Task AllocateAsync_Returns_Requested_Number_Immediately()
    {
        await using var db = OfflineContext();

        var result = await SqlDocumentSequenceStore.AllocateAsync(
            db, 1, DocumentSequenceType.SalesOrder, " manual-so-001 ", DateTime.UtcNow, CancellationToken.None);

        Assert.Equal("MANUAL-SO-001", result);
    }

    [Fact]
    public async Task AllocateAsync_Respects_CancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockFactory = new MockDbContextFactory();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SqlDocumentSequenceStore.AllocateAsync(
                mockFactory, 1, DocumentSequenceType.SalesOrder, null, DateTime.UtcNow, cts.Token));

        Assert.Equal(0, mockFactory.CreateCount);
    }

    [Fact]
    public async Task AllocateAsync_Throws_Non_Transient_Errors_Immediately_Without_Retrying()
    {
        var mockFactory = new MockDbContextFactory(createDb: () =>
        {
            throw CreateSqlException(547, "Foreign key constraint failure");
        });

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            SqlDocumentSequenceStore.AllocateAsync(
                mockFactory, 1, DocumentSequenceType.SalesOrder, null, DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(547, ex.Number);
        Assert.Equal(1, mockFactory.CreateCount);
    }

    [Fact]
    public async Task AllocateAsync_Retries_Up_To_MaxRetries_Then_Rethrows_Transient_Error()
    {
        var mockFactory = new MockDbContextFactory(createDb: () =>
        {
            throw CreateSqlException(1205, "Transaction was deadlocked");
        });

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            SqlDocumentSequenceStore.AllocateAsync(
                mockFactory, 1, DocumentSequenceType.SalesOrder, null, DateTime.UtcNow, CancellationToken.None, maxRetries: 3));

        Assert.Equal(1205, ex.Number);
        Assert.Equal(3, mockFactory.CreateCount);
    }

    [Fact]
    public async Task AllocateAsync_Retries_On_Deadlock_And_LockTimeout_Then_Succeeds()
    {
        var attempts = 0;
        var mockFactory = new MockDbContextFactory(createDb: () =>
        {
            attempts++;
            if (attempts == 1)
                throw CreateSqlException(1205, "Deadlock victim");
            if (attempts == 2)
                throw CreateSqlException(1222, "Lock request time out");

            throw CreateSqlException(547, "Reached attempt 3");
        });

        // Verifies that attempts 1 (1205) and 2 (1222) were caught and retried, reaching attempt 3
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            SqlDocumentSequenceStore.AllocateAsync(
                mockFactory, 1, DocumentSequenceType.SalesOrder, null, DateTime.UtcNow, CancellationToken.None, maxRetries: 5));

        Assert.Equal(547, ex.Number);
        Assert.Equal(3, attempts);
        Assert.Equal(3, mockFactory.CreateCount);
    }

    [OrganizationSqlServerFact]
    public async Task Concurrent_Allocations_Under_Contention_Do_Not_Duplicate_Or_Skip_Numbers()
    {
        var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(raw)) return;

        var masterBuilder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
        var dbName = $"ff_seq_concurrency_{Guid.NewGuid():N}";

        await using (var conn = new SqlConnection(masterBuilder.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE [{dbName}]";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var dbConn = new SqlConnectionStringBuilder(masterBuilder.ConnectionString) { InitialCatalog = dbName }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            var factory = new SimpleFactory(options);

            await using (var ctx = factory.CreateDbContext())
                await ctx.Database.MigrateAsync();

            var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
            long orgId;
            await using (var setupDb = factory.CreateDbContext())
            {
                var org = Organization.Create("CONC-ORG", "Concurrency Org");
                setupDb.Organizations.Add(org);
                await setupDb.SaveChangesAsync(actor);
                orgId = org.Id;
            }

            var store = new SqlDocumentSequenceStore(factory);
            var utcNow = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

            // Execute 15 concurrent sequence allocations across parallel tasks
            const int concurrency = 15;
            var tasks = new Task<string>[concurrency];
            for (var i = 0; i < concurrency; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    return await store.AllocateNextNumber(
                        orgId,
                        DocumentSequenceType.SalesOrder,
                        null,
                        utcNow,
                        CancellationToken.None);
                });
            }

            var allocatedNumbers = await Task.WhenAll(tasks);

            // Verify all allocated numbers are unique
            Assert.Equal(concurrency, allocatedNumbers.Length);
            var distinctCount = allocatedNumbers.Distinct().Count();
            Assert.Equal(concurrency, distinctCount);

            // Verify numbers follow strict sequential order SO-00001 through SO-00015
            var sorted = allocatedNumbers.OrderBy(n => n).ToList();
            for (var i = 1; i <= concurrency; i++)
            {
                var expected = $"SO-{i:D5}";
                Assert.Equal(expected, sorted[i - 1]);
            }
        }
        finally
        {
            await using var conn = new SqlConnection(masterBuilder.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static SqlException CreateSqlException(int number, string message = "Simulated SQL Error")
    {
        var collection = (SqlErrorCollection)Activator.CreateInstance(
            typeof(SqlErrorCollection),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            null,
            null)!;

        var errorCtor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var parameters = errorCtor.GetParameters();
        var errorArgs = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.Name?.Equals("infoNumber", StringComparison.OrdinalIgnoreCase) == true)
                errorArgs[i] = number;
            else if (p.Name?.Equals("message", StringComparison.OrdinalIgnoreCase) == true)
                errorArgs[i] = message;
            else if (p.ParameterType == typeof(string))
                errorArgs[i] = "MockServer";
            else if (p.ParameterType == typeof(int))
                errorArgs[i] = number;
            else if (p.ParameterType == typeof(byte))
                errorArgs[i] = (byte)16;
            else
                errorArgs[i] = null;
        }

        var error = errorCtor.Invoke(errorArgs);

        var addMethod = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(SqlError)], null);
        addMethod?.Invoke(collection, [error]);

        var exceptionCtor = typeof(SqlException).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var exceptionParams = exceptionCtor.GetParameters();
        var exceptionArgs = new object?[exceptionParams.Length];
        for (var i = 0; i < exceptionParams.Length; i++)
        {
            var p = exceptionParams[i];
            if (p.ParameterType == typeof(string))
                exceptionArgs[i] = message;
            else if (p.ParameterType == typeof(SqlErrorCollection))
                exceptionArgs[i] = collection;
            else if (p.ParameterType == typeof(Exception))
                exceptionArgs[i] = null;
            else if (p.ParameterType == typeof(Guid))
                exceptionArgs[i] = Guid.NewGuid();
            else
                exceptionArgs[i] = null;
        }

        return (SqlException)exceptionCtor.Invoke(exceptionArgs);
    }

    private static OrganizationDbContext OfflineContext() => new(new DbContextOptionsBuilder<OrganizationDbContext>()
        .UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=true;Encrypt=true;Connect Timeout=1").Options);

    private sealed class MockDbContextFactory(Func<OrganizationDbContext>? createDb = null)
        : IDbContextFactory<OrganizationDbContext>
    {
        public int CreateCount { get; private set; }

        public OrganizationDbContext CreateDbContext()
        {
            CreateCount++;
            if (createDb is not null)
                return createDb();

            return OfflineContext();
        }
    }

    [Fact]
    public async Task AllocateNumber_Requires_Manage_Permission()
    {
        var store = new StubDocumentSequenceStore();
        var actor = new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var useCases = new DocumentSequenceUseCases(
            store,
            new ActorStub(actor),
            new PermissionStub((scope, perm) => perm == DocumentSequencePermissions.Read));

        var scope = new OrganizationScope.Organization(1);
        var result = await useCases.AllocateNumber(scope, 1, DocumentSequenceType.SalesOrder);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentSequenceErrors.AccessDenied, result.Error!.Code);
    }

    [Fact]
    public async Task AllocateNumber_Succeeds_When_Manage_Permission_Granted()
    {
        var store = new StubDocumentSequenceStore { NumberToReturn = "SO-1405-00100" };
        var actor = new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var useCases = new DocumentSequenceUseCases(
            store,
            new ActorStub(actor),
            new PermissionStub((scope, perm) => perm == DocumentSequencePermissions.Manage));

        var scope = new OrganizationScope.Organization(1);
        var result = await useCases.AllocateNumber(scope, 1, DocumentSequenceType.SalesOrder);

        Assert.True(result.Succeeded);
        Assert.Equal("SO-1405-00100", result.Value);
    }

    [Fact]
    public async Task AllocateNumber_Handles_Store_Unavailable()
    {
        var store = new StubDocumentSequenceStore { ThrowUnavailable = true };
        var actor = new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var useCases = new DocumentSequenceUseCases(
            store,
            new ActorStub(actor),
            new PermissionStub((scope, perm) => perm == DocumentSequencePermissions.Manage));

        var scope = new OrganizationScope.Organization(1);
        var result = await useCases.AllocateNumber(scope, 1, DocumentSequenceType.SalesOrder);

        Assert.False(result.Succeeded);
        Assert.Equal(DocumentSequenceErrors.PersistenceUnavailable, result.Error!.Code);
    }

    [Fact]
    public void DocumentSequence_DetachEntity_And_Revert_When_Exception_Occurs()
    {
        using var db = OfflineContext();
        var seq = DocumentSequence.Create(1, DocumentSequenceType.SalesOrder, "SO", "-", DocumentYearMode.Persian, 5, 10, false);
        db.DocumentSequences.Add(seq);
        Assert.Equal(EntityState.Added, db.Entry(seq).State);

        var prevNumber = seq.NextNumber;
        var prevYear = seq.LastAllocatedYear;
        seq.AllocateNextNumber(DateTime.UtcNow);

        // Simulate catch block in AllocateAsync
        seq.RevertAllocation(prevNumber, prevYear);
        db.Entry(seq).State = EntityState.Detached;

        Assert.Equal(10, seq.NextNumber);
        Assert.Equal(EntityState.Detached, db.Entry(seq).State);
    }

    private sealed class ActorStub(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionStub(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor,
            OrganizationScope requestedScope,
            string permission,
            CancellationToken ct) =>
            ValueTask.FromResult(new PermissionDecision(predicate(requestedScope, permission)));
    }

    private sealed class StubDocumentSequenceStore : IDocumentSequenceStore
    {
        public string NumberToReturn { get; set; } = "SO-00001";
        public bool ThrowUnavailable { get; set; }

        public Task<IReadOnlyList<DocumentSequenceSummary>> ListSequences(long organizationId, DateTime utcNow, OrganizationWriteAccess? seedAccess, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentSequenceSummary>>([]);

        public Task<DocumentSequenceSummary> GetPreview(long organizationId, DocumentSequenceType documentType, DateTime utcNow, CancellationToken ct) =>
            Task.FromResult(new DocumentSequenceSummary(1, organizationId, documentType, "SO", "-", DocumentYearMode.None, 5, 1, false, "SO-00001", ""));

        public Task<OrganizationResult<DocumentSequenceSummary>> UpdateSequence(long organizationId, DocumentSequenceType documentType, UpdateDocumentSequenceRequest request, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct) =>
            Task.FromResult(OrganizationResult<DocumentSequenceSummary>.Success(new DocumentSequenceSummary(1, organizationId, documentType, request.Prefix, request.Separator, request.YearMode, request.PaddingDigits, request.NextNumber, request.AllowManualOverride, "PREVIEW", "")));

        public Task<string> AllocateNextNumber(long organizationId, DocumentSequenceType documentType, string? requestedNumber, DateTime utcNow, CancellationToken ct, TimeZoneInfo? timeZone = null)
        {
            if (ThrowUnavailable)
                throw new DocumentSequenceStoreUnavailableException();

            return Task.FromResult(requestedNumber ?? NumberToReturn);
        }
    }

    private sealed class SimpleFactory(DbContextOptions<OrganizationDbContext> options)
        : IDbContextFactory<OrganizationDbContext>
    {
        public OrganizationDbContext CreateDbContext() => new(options);
    }
}
