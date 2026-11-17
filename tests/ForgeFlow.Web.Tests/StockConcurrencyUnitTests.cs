using System.Reflection;
using System.Runtime.CompilerServices;
using ForgeFlow.Domain.Common;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class StockConcurrencyUnitTests
{
    [Fact]
    public void InventoryBalance_And_StockReservation_Implement_IHasRowVersion()
    {
        Assert.True(typeof(IHasRowVersion).IsAssignableFrom(typeof(InventoryBalance)));
        Assert.True(typeof(IHasRowVersion).IsAssignableFrom(typeof(StockReservation)));

        var balProp = typeof(InventoryBalance).GetProperty(nameof(IHasRowVersion.RowVersion));
        Assert.NotNull(balProp);
        Assert.Equal(typeof(byte[]), balProp.PropertyType);

        var srProp = typeof(StockReservation).GetProperty(nameof(IHasRowVersion.RowVersion));
        Assert.NotNull(srProp);
        Assert.Equal(typeof(byte[]), srProp.PropertyType);
    }

    [Fact]
    public void InventoryBalance_AvailableQuantity_Calculates_Correctly_And_Blocks_OverReservation()
    {
        var now = DateTime.UtcNow;
        var bal = InventoryBalance.CreateZero(1, 1, 1, 10, "LOT-1", 1, now);
        bal.ApplyDelta(100m, now);

        Assert.Equal(100m, bal.OnHandBaseQuantity);
        Assert.Equal(0m, bal.BlockedBaseQuantity);
        Assert.Equal(0m, bal.ReservedBaseQuantity);
        Assert.Equal(100m, bal.AvailableBaseQuantity);

        // Reserve 40
        bal.ReserveQuantity(40m, now);
        Assert.Equal(40m, bal.ReservedBaseQuantity);
        Assert.Equal(60m, bal.AvailableBaseQuantity);

        // Block 30 (quarantine)
        bal.BlockQuantity(30m, now);
        Assert.Equal(30m, bal.BlockedBaseQuantity);
        Assert.Equal(30m, bal.AvailableBaseQuantity);

        // Attempting to reserve 31 fails immediately
        var ex = Assert.Throws<InventoryRuleException>(() => bal.ReserveQuantity(31m, now));
        Assert.Equal(InventoryErrors.ReservationExceedsAvailable, ex.Code);

        // Reserving exactly 30 succeeds
        bal.ReserveQuantity(30m, now);
        Assert.Equal(70m, bal.ReservedBaseQuantity);
        Assert.Equal(30m, bal.BlockedBaseQuantity);
        Assert.Equal(0m, bal.AvailableBaseQuantity);

        // Attempting to reserve even 0.000001 fails
        var ex2 = Assert.Throws<InventoryRuleException>(() => bal.ReserveQuantity(0.000001m, now));
        Assert.Equal(InventoryErrors.ReservationExceedsAvailable, ex2.Code);
    }

    [Fact]
    public void InventoryBalance_Outbound_Delta_Enforces_Available_Quantity()
    {
        var now = DateTime.UtcNow;
        var bal = InventoryBalance.CreateZero(1, 1, 1, 10, "LOT-2", 1, now);
        bal.ApplyDelta(50m, now);
        bal.ReserveQuantity(30m, now);

        // Available is 20, OnHand is 50. Outbound delta of -25 must fail because available is only 20
        var ex = Assert.Throws<InventoryRuleException>(() => bal.ApplyDelta(-25m, now, enforceAvailableOnOutbound: true));
        Assert.Equal(InventoryErrors.InsufficientStock, ex.Code);

        // Outbound delta of -20 succeeds
        bal.ApplyDelta(-20m, now, enforceAvailableOnOutbound: true);
        Assert.Equal(30m, bal.OnHandBaseQuantity);
        Assert.Equal(30m, bal.ReservedBaseQuantity);
        Assert.Equal(0m, bal.AvailableBaseQuantity);

        // Release 10 reserved
        bal.ReleaseReservedQuantity(10m, now);
        Assert.Equal(20m, bal.ReservedBaseQuantity);
        Assert.Equal(10m, bal.AvailableBaseQuantity);
    }

    [Fact]
    public void StockReservation_Lifecycle_And_Partial_Consumptions()
    {
        var now = DateTime.UtcNow;
        var sr = StockReservation.Create(1, 1, 1, 100, null, 10, "LOT-3", 1, 50m, "Test PO reservation");
        Assert.Equal(ReservationStatus.Active, sr.Status);
        Assert.True(sr.IsOpen);
        Assert.Equal(50m, sr.RemainingReservedBaseQuantity);

        // Consume 20
        var consumed1 = sr.Consume(20m);
        Assert.Equal(20m, consumed1);
        Assert.Equal(20m, sr.ConsumedBaseQuantity);
        Assert.Equal(30m, sr.RemainingReservedBaseQuantity);
        Assert.Equal(ReservationStatus.PartiallyConsumed, sr.Status);
        Assert.True(sr.IsOpen);

        // Consume remaining 30
        var consumed2 = sr.Consume(30m);
        Assert.Equal(30m, consumed2);
        Assert.Equal(50m, sr.ConsumedBaseQuantity);
        Assert.Equal(0m, sr.RemainingReservedBaseQuantity);
        Assert.Equal(ReservationStatus.Fulfilled, sr.Status);
        Assert.False(sr.IsOpen);

        // Further consume or release fails on closed reservation
        Assert.Throws<InventoryRuleException>(() => sr.Consume(5m));
        Assert.Throws<InventoryRuleException>(() => sr.Release(now));
    }

    [Fact]
    public void StockConcurrencyRetryPolicy_Identifies_Transient_Errors()
    {
        // 1. DbUpdateConcurrencyException is transient
        var concurrencyEx = new DbUpdateConcurrencyException("Concurrency conflict");
        Assert.True(StockConcurrencyRetryPolicy.IsTransient(concurrencyEx));

        // 2. Wrap DbUpdateConcurrencyException in DbUpdateException
        var wrapped = new DbUpdateException("Wrapped", concurrencyEx);
        Assert.True(StockConcurrencyRetryPolicy.IsTransient(wrapped));

        // 3. SqlException 1205 (Deadlock victim)
        var deadlockSql = CreateSqlException(1205, "Transaction was deadlocked on lock resources with another process and has been chosen as the deadlock victim.");
        Assert.True(StockConcurrencyRetryPolicy.IsTransient(deadlockSql));
        Assert.True(StockConcurrencyRetryPolicy.IsTransient(new DbUpdateException("Failed", deadlockSql)));

        // 4. SqlException 1222 (Lock timeout)
        var timeoutSql = CreateSqlException(1222, "Lock request time out period exceeded.");
        Assert.True(StockConcurrencyRetryPolicy.IsTransient(timeoutSql));

        // 5. SqlException 2601 / 2627 (Unique constraint collision on initial balance creation)
        var unique2601 = CreateSqlException(2601, "Cannot insert duplicate key row in object 'organization.InventoryBalance' with unique index 'UX_InventoryBalance_Org_Wh_Item_Lot'.");
        var unique2627 = CreateSqlException(2627, "Violation of UNIQUE KEY constraint 'UX_InventoryBalance_Org_Wh_Item_Lot'.");
        Assert.True(StockConcurrencyRetryPolicy.IsTransient(unique2601));
        Assert.True(StockConcurrencyRetryPolicy.IsTransient(unique2627));

        // 6. Non-transient exceptions
        Assert.False(StockConcurrencyRetryPolicy.IsTransient(new InvalidOperationException("Non-transient")));
        Assert.False(StockConcurrencyRetryPolicy.IsTransient(new ArgumentNullException("param")));
        var fkSql = CreateSqlException(547, "The INSERT statement conflicted with the FOREIGN KEY constraint");
        Assert.False(StockConcurrencyRetryPolicy.IsTransient(fkSql));
    }

    [Fact]
    public void StockConcurrencyRetryPolicy_Calculates_Bounded_Delay_With_Jitter()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var delay = StockConcurrencyRetryPolicy.CalculateDelay(attempt);
            Assert.True(delay >= StockConcurrencyRetryPolicy.DefaultInitialDelay);
            Assert.True(delay <= TimeSpan.FromMilliseconds(StockConcurrencyRetryPolicy.DefaultMaxDelay.TotalMilliseconds * 1.5));
        }

        // High attempt count capped at DefaultMaxDelay (+ 50% max jitter)
        var cappedDelay = StockConcurrencyRetryPolicy.CalculateDelay(20);
        Assert.True(cappedDelay <= TimeSpan.FromMilliseconds(StockConcurrencyRetryPolicy.DefaultMaxDelay.TotalMilliseconds * 1.5));
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
}
