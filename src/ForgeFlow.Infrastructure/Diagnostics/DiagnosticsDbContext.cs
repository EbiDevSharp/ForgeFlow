using ForgeFlow.Domain.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ForgeFlow.Infrastructure.Diagnostics;

/// <summary>
/// Isolated append-only persistence context for sanitized system error logs (diag.ErrorLog)
/// and user operation activity records (ops.UserOperationLog).
/// Deliberately has zero FKs to auth/organization so logging succeeds even during tenant/tx rollbacks.
/// </summary>
public sealed class DiagnosticsDbContext(DbContextOptions<DiagnosticsDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTime, DateTime> Utc = new(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    public DbSet<ErrorLogRecord> ErrorLogs => Set<ErrorLogRecord>();
    public DbSet<UserOperationLogRecord> UserOperationLogs => Set<UserOperationLogRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var err = model.Entity<ErrorLogRecord>();
        err.ToTable("ErrorLog", "diag", t =>
        {
            t.HasCheckConstraint("CK_ErrorLog_Severity", "[Severity] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_ErrorLog_Source", "[Source] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_ErrorLog_Ref", "LEN([ErrorReference]) > 0 AND DATALENGTH([ErrorReference]) = LEN([ErrorReference])");
            t.HasCheckConstraint("CK_ErrorLog_Scope", "[SiteId] IS NULL OR [OrganizationId] IS NOT NULL");
        });
        err.HasKey(e => e.Id).HasName("PK_ErrorLog");
        err.Property(e => e.Id).UseIdentityColumn();
        err.Property(e => e.ErrorReference).HasMaxLength(32).IsUnicode(false).IsRequired();
        err.Property(e => e.CorrelationId).IsRequired();
        err.Property(e => e.OccurredAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        err.Property(e => e.Severity).HasColumnType("tinyint").IsRequired();
        err.Property(e => e.Source).HasColumnType("tinyint").IsRequired();
        err.Property(e => e.ExceptionType).HasMaxLength(256).IsUnicode(false).IsRequired();
        err.Property(e => e.RedactedMessage).HasMaxLength(2000).IsRequired();
        err.Property(e => e.RedactedStackTrace).HasColumnType("nvarchar(max)").IsRequired();
        err.Property(e => e.RequestPath).HasMaxLength(512);
        err.Property(e => e.ComponentName).HasMaxLength(128).IsUnicode(false);
        err.Property(e => e.UserAgent).HasMaxLength(256);

        err.HasIndex(e => e.ErrorReference).IsUnique().HasDatabaseName("UX_ErrorLog_ErrorReference");
        err.HasIndex(e => new { e.OccurredAtUtc, e.Id }).HasDatabaseName("IX_ErrorLog_OccurredAt_Id");
        err.HasIndex(e => e.CorrelationId).HasDatabaseName("IX_ErrorLog_CorrelationId");
        err.HasIndex(e => new { e.Severity, e.OccurredAtUtc }).HasDatabaseName("IX_ErrorLog_Severity_Time");

        var op = model.Entity<UserOperationLogRecord>();
        op.ToTable("UserOperationLog", "ops", t =>
        {
            t.HasCheckConstraint("CK_UserOperationLog_Code", "LEN([OperationCode]) > 0 AND DATALENGTH([OperationCode]) = LEN([OperationCode])");
            t.HasCheckConstraint("CK_UserOperationLog_Outcome", "[Outcome] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_UserOperationLog_Json", "ISJSON([MetadataJson]) = 1");
            t.HasCheckConstraint("CK_UserOperationLog_Scope", "[SiteId] IS NULL OR [OrganizationId] IS NOT NULL");
        });
        op.HasKey(o => o.Id).HasName("PK_UserOperationLog");
        op.Property(o => o.Id).UseIdentityColumn();
        op.Property(o => o.OccurredAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        op.Property(o => o.OperationCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        op.Property(o => o.Outcome).HasColumnType("tinyint").IsRequired();
        op.Property(o => o.CorrelationId).IsRequired();
        op.Property(o => o.TargetEntityType).HasMaxLength(64).IsUnicode(false);
        op.Property(o => o.TargetEntityRef).HasMaxLength(128);
        op.Property(o => o.FailureCode).HasMaxLength(128).IsUnicode(false);
        op.Property(o => o.MetadataJson).HasMaxLength(2000).IsRequired();

        op.HasIndex(o => new { o.OccurredAtUtc, o.Id }).HasDatabaseName("IX_UserOperationLog_OccurredAt_Id");
        op.HasIndex(o => new { o.OrganizationId, o.SiteId, o.OccurredAtUtc, o.Id }).HasDatabaseName("IX_UserOperationLog_Org_Site_Time");
        op.HasIndex(o => new { o.ActorUserId, o.OccurredAtUtc, o.Id }).HasDatabaseName("IX_UserOperationLog_Actor_Time");
        op.HasIndex(o => o.CorrelationId).HasDatabaseName("IX_UserOperationLog_CorrelationId");
    }
}

public sealed class DiagnosticsDesignTimeFactory : IDesignTimeDbContextFactory<DiagnosticsDbContext>
{
    public DiagnosticsDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<DiagnosticsDbContext>()
            .UseSqlServer(
                "Server=invalid.invalid;Database=ForgeFlow_DiagDesignOnly;Integrated Security=true;Encrypt=true;Connect Timeout=1",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "diag"))
            .Options);
}
