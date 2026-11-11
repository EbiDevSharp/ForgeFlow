using System.Globalization;
using System.Text.Json;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DiagnosticTests
{
    private static readonly TrustedActor PlatformActor = new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void D01_redactor_scrubs_secrets_tokens_cookies_and_query_params_while_keeping_structure()
    {
        var secretPwd = "Sup3rS3cretP@ssword!!";
        var longToken = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdef";
        var inner = new InvalidOperationException($"Login failed for 'admin_user' token={longToken}; password={secretPwd}; Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig");
        var outer = new Exception("Wrapper error on [diag].[ErrorLog] failed", inner);

        var redacted = DiagnosticRedactor.Redact(outer, secretPwd);
        Assert.DoesNotContain(secretPwd, redacted.RedactedMessage);
        Assert.DoesNotContain(secretPwd, redacted.RedactedStackTrace);
        Assert.DoesNotContain(longToken, redacted.RedactedMessage);
        Assert.DoesNotContain("admin_user", redacted.RedactedMessage);
        Assert.DoesNotContain("eyJhbGciOi", redacted.RedactedMessage);
        Assert.Contains("[redacted]", redacted.RedactedMessage);
        Assert.Equal("System.Exception", redacted.ExceptionType);

        var path = DiagnosticRedactor.RedactRequestPath("/reports?poId=42&token=secret123&code=abc");
        Assert.Equal("/reports?poId=42&token=[redacted]&code=[redacted]", path);
    }

    [Fact]
    public async Task D02_error_recorder_falls_back_to_rolling_jsonl_when_database_is_unavailable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ff-diag-test-{Guid.NewGuid():N}");
        try
        {
            var unavailableOptions = new DbContextOptionsBuilder<DiagnosticsDbContext>()
                .UseSqlServer("Server=127.0.0.1,1;Database=DoesNotExist;Connect Timeout=1;Encrypt=False")
                .Options;
            var factory = new FixedDiagnosticsFactory(unavailableOptions);
            var store = new SqlErrorLogStore(
                factory,
                new EmptyScopeFactory(),
                NullLogger<SqlErrorLogStore>.Instance,
                new DiagnosticsStorageOptions { FallbackLogDirectory = tempDir });

            var recorded = await store.RecordExceptionAsync(
                new InvalidOperationException("password=MySecret123! Failure"),
                new ErrorLogRecordContext(
                    ErrorSeverity.Critical,
                    ErrorSourceKind.HttpMiddleware,
                    "/inventory?secret=abc&poId=5",
                    "Inventory.razor",
                    ActorUserId: 7,
                    OrganizationId: 2,
                    SiteId: 3));

            Assert.False(recorded.SavedToDatabase);
            Assert.StartsWith("FF-", recorded.ErrorReference);
            Assert.NotEqual(Guid.Empty, recorded.CorrelationId);

            var files = Directory.GetFiles(tempDir, "errors-*.jsonl");
            var file = Assert.Single(files);
            var content = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain("MySecret123!", content);
            Assert.DoesNotContain("secret=abc", content);
            Assert.Contains(recorded.ErrorReference, content);
            Assert.Contains("poId=5", content);
            using var doc = JsonDocument.Parse(content.Trim());
            Assert.Equal("Critical", doc.RootElement.GetProperty("Severity").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task D03_error_log_use_cases_enforce_platform_or_org_read_and_platform_only_purge()
    {
        var store = new InMemoryErrorLogStore();
        var allowAll = new AllowAllPermissions();
        var useCases = new ErrorLogUseCases(store, new StaticActor(PlatformActor), allowAll);

        var orgRead = await useCases.ListErrorLogs(new OrganizationScope.Organization(1), new ErrorLogListQuery());
        Assert.True(orgRead.Succeeded);
        Assert.IsType<OrganizationScope.Organization>(store.LastReadScope);

        var orgStats = await useCases.GetStats(new OrganizationScope.Organization(1));
        Assert.True(orgStats.Succeeded);

        var orgPurgeAttempt = await useCases.PurgeOldErrorLogs(new OrganizationScope.Organization(1), 90);
        Assert.False(orgPurgeAttempt.Succeeded);
        Assert.Equal(DiagnosticErrors.AccessDenied, orgPurgeAttempt.Error!.Code);

        var siteAttempt = await useCases.GetErrorLog(new OrganizationScope.Site(1, 2), 1);
        Assert.False(siteAttempt.Succeeded);
        Assert.Equal(DiagnosticErrors.AccessDenied, siteAttempt.Error!.Code);

        var invalidRange = await useCases.ListErrorLogs(
            new OrganizationScope.Platform(),
            new ErrorLogListQuery(FromDate: new DateOnly(2026, 11, 10), ToDate: new DateOnly(2026, 11, 1)));
        Assert.False(invalidRange.Succeeded);
        Assert.Equal(DiagnosticErrors.InvalidQuery, invalidRange.Error!.Code);

        var invalidPurge = await useCases.PurgeOldErrorLogs(new OrganizationScope.Platform(), 2);
        Assert.False(invalidPurge.Succeeded);
        Assert.Equal(DiagnosticErrors.InvalidRetentionDays, invalidPurge.Error!.Code);

        var okList = await useCases.ListErrorLogs(new OrganizationScope.Platform(), new ErrorLogListQuery());
        Assert.True(okList.Succeeded);
        Assert.IsType<OrganizationScope.Platform>(store.LastReadScope);

        var okPurge = await useCases.PurgeOldErrorLogs(new OrganizationScope.Platform(), 90);
        Assert.True(okPurge.Succeeded);
    }

    [Fact]
    public void D04_diagnostics_db_context_and_migration_satisfy_append_only_and_isolation_contracts()
    {
        using var db = new DiagnosticsDesignTimeFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Equal(2, model.GetEntityTypes().Count());
        Assert.Empty(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()));

        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE [diag].[ErrorLog]", script);
        Assert.Contains("CREATE TABLE [ops].[UserOperationLog]", script);
        Assert.Contains("DENY UPDATE ON OBJECT::[diag].[ErrorLog]", script);
        Assert.Contains("DENY UPDATE, DELETE ON OBJECT::[ops].[UserOperationLog]", script);
    }

    [Fact]
    public void D05_diagnostic_and_audit_export_permissions_respect_platform_vs_tenant_boundaries()
    {
        Assert.True(PermissionCatalog.TryGet(DiagnosticPermissions.ErrorLogsRead, out var readDesc));
        Assert.Equal(PermissionScopeApplicability.OrganizationOnly, readDesc.Applicability);
        Assert.True(PermissionCatalog.TryGet(DiagnosticPermissions.ErrorLogsManage, out var manageDesc));
        Assert.Equal(PermissionScopeApplicability.PlatformOnly, manageDesc.Applicability);
        Assert.True(PermissionCatalog.TryGet(AuditPermissions.AuditExport, out var exportDesc));
        Assert.Equal(PermissionScopeApplicability.OrganizationOrSite, exportDesc.Applicability);

        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(DiagnosticPermissions.ErrorLogsRead, 1));
        Assert.False(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(DiagnosticPermissions.ErrorLogsRead, 2));
        Assert.False(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(DiagnosticPermissions.ErrorLogsManage, 1));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(AuditPermissions.AuditExport, 1));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(AuditPermissions.AuditExport, 2));

        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.DeploymentProvisioner, new OrganizationScope.Platform(), DiagnosticPermissions.ErrorLogsRead));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.DeploymentProvisioner, new OrganizationScope.Platform(), DiagnosticPermissions.ErrorLogsManage));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Organization(1), DiagnosticPermissions.ErrorLogsRead));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Site(1, 2), DiagnosticPermissions.ErrorLogsRead));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Organization(1), DiagnosticPermissions.ErrorLogsManage));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.SiteOperator, new OrganizationScope.Site(1, 2), AuditPermissions.AuditExport));
    }

    [Fact]
    public async Task D06_audit_export_records_operation_and_validates_date_and_site_bounds()
    {
        var recorder = new CapturingOperationRecorder();
        var auditStore = new StubAuditStore();
        var orgScope = new OrganizationScope.Organization(1);
        var siteScope = new OrganizationScope.Site(1, 5);
        var auditUseCases = new AuditUseCases(
            auditStore,
            new StaticActor(PlatformActor),
            new AllowAllPermissions(),
            recorder);

        // Site scope cannot query sibling site
        var crossSite = await auditUseCases.ListAuditEntries(siteScope, new AuditListQuery(SiteId: 9));
        Assert.False(crossSite.Succeeded);
        Assert.Equal(ForgeFlow.Domain.Audit.AuditErrors.InvalidQuery, crossSite.Error!.Code);

        // FromDate > ToDate rejected
        var badRange = await auditUseCases.ListAuditEntries(orgScope, new AuditListQuery(
            FromDate: new DateOnly(2026, 11, 15), ToDate: new DateOnly(2026, 11, 1)));
        Assert.False(badRange.Succeeded);

        var headers8 = new CsvExportColumnHeaders(["1", "2", "3", "4", "5", "6", "7", "8"], "audit.csv");
        var exportRes = await auditUseCases.ExportAuditCsv(orgScope, new AuditListQuery(), headers8);
        Assert.True(exportRes.Succeeded);
        Assert.Equal(0xEF, exportRes.Value!.Bytes[0]);

        var recorded = Assert.Single(recorder.Entries);
        Assert.Equal(UserOperationCatalog.AuditExported, recorded.OperationCode);
        Assert.Equal(UserOperationOutcome.Succeeded, recorded.Outcome);
        Assert.Equal(1L, recorded.OrganizationId);
    }

    [Fact]
    public void D07_display_formatter_converts_utc_to_target_timezone_without_utc_suffix()
    {
        var tehran = TimeZoneInfo.CreateCustomTimeZone("IranTest", TimeSpan.FromHours(3.5), "Iran", "Iran");
        var formatter = new ForgeFlow.Web.Localization.DisplayFormatter(new ForgeFlow.Web.Localization.CulturePolicy(), tehran);
        var utcTime = new DateTime(2026, 11, 10, 19, 6, 54, DateTimeKind.Utc);
        var formatted = formatter.Timestamp(utcTime);

        Assert.DoesNotContain("UTC", formatted, StringComparison.Ordinal);
        Assert.EndsWith("22:36:54", formatted, StringComparison.Ordinal);
        Assert.StartsWith("2026/11/10", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void D08_redactor_scrub_json_preserves_valid_json_while_redacting_secrets()
    {
        var scrubbed = DiagnosticRedactor.ScrubJson("{\"rows\":3,\"token\":\"abc123\",\"note\":\"clean\",\"authHeader\":\"Bearer secret.jwt.value\"}");
        using var doc = System.Text.Json.JsonDocument.Parse(scrubbed);
        Assert.Equal(3, doc.RootElement.GetProperty("rows").GetInt32());
        Assert.Equal("[redacted]", doc.RootElement.GetProperty("token").GetString());
        Assert.Equal("clean", doc.RootElement.GetProperty("note").GetString());
        Assert.Equal("Bearer [redacted]", doc.RootElement.GetProperty("authHeader").GetString());
    }

    [Fact]
    public async Task D09_rejected_mutations_across_core_modules_record_action_rejected_operation_logs()
    {
        var recorder = new CapturingOperationRecorder();
        var actorAccessor = new StaticActor(PlatformActor);
        var allowAll = new AllowAllPermissions();
        var orgScope = new OrganizationScope.Organization(1);

        var orgs = new OrganizationUseCases(null!, actorAccessor, allowAll, recorder);
        var orgRes = await orgs.CreateOrganization(orgScope, new CreateOrganization("X", "WrongScope"));
        Assert.False(orgRes.Succeeded);
        var siteRes = await orgs.CreateSite(orgScope, new CreateSite(999, "S", "WrongOrg"));
        Assert.False(siteRes.Succeeded);

        var boms = new ForgeFlow.Application.Boms.BomUseCases(null!, actorAccessor, allowAll, recorder);
        var bomRes = await boms.CreateBom(orgScope, new ForgeFlow.Application.Boms.CreateBomRequest(1, "", "Invalid", 10));
        Assert.False(bomRes.Succeeded);

        var units = new ForgeFlow.Application.Units.UnitUseCases(null!, actorAccessor, allowAll, recorder);
        var unitRes = await units.CreateUnit(orgScope, new ForgeFlow.Application.Units.CreateUnitRequest(1, "", "Invalid", "kg", ForgeFlow.Domain.Units.UnitDimension.Mass, 2));
        Assert.False(unitRes.Succeeded);

        var items = new ForgeFlow.Application.Items.ItemUseCases(null!, null, actorAccessor, allowAll, recorder);
        var itemRes = await items.CreateItem(orgScope, new ForgeFlow.Application.Items.CreateItemRequest(1, "", "Invalid", null, 1, true, false, false, true));
        Assert.False(itemRes.Succeeded);

        var users = new ForgeFlow.Application.Users.UserUseCases(null!, actorAccessor, allowAll, recorder);
        var userRes = await users.CreateOrganizationUser(orgScope, new ForgeFlow.Application.Users.CreateOrganizationUser(1, "", "ValidPass12345!", false, [], false));
        Assert.False(userRes.Succeeded);

        var roles = new ForgeFlow.Application.Roles.RoleUseCases(null!, actorAccessor, allowAll, recorder);
        var roleRes = await roles.CreateOrganizationRole(orgScope, new ForgeFlow.Application.Roles.CreateOrganizationRole(1, "", ForgeFlow.Application.Roles.RoleScopeTarget.Organization, []));
        Assert.False(roleRes.Succeeded);

        Assert.Equal(7, recorder.Entries.Count);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.OrganizationActionRejected && e.Outcome == UserOperationOutcome.AccessDenied);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.SiteActionRejected && e.Outcome == UserOperationOutcome.BusinessRuleRejected);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.BomActionRejected && e.Outcome == UserOperationOutcome.BusinessRuleRejected);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.UnitActionRejected && e.Outcome == UserOperationOutcome.BusinessRuleRejected);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.ItemActionRejected && e.Outcome == UserOperationOutcome.BusinessRuleRejected);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.UserActionRejected && e.Outcome == UserOperationOutcome.BusinessRuleRejected);
        Assert.Contains(recorder.Entries, e => e.OperationCode == UserOperationCatalog.RoleActionRejected && e.Outcome == UserOperationOutcome.BusinessRuleRejected);
    }

    [Fact]
    public void D10_client_ip_resolver_reads_forwarded_for_and_normalizes_addresses()
    {
        var forwarded = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        forwarded.Request.Headers["X-Forwarded-For"] = "203.0.113.9, 10.0.0.5";
        forwarded.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        Assert.Equal("203.0.113.9", ForgeFlow.Web.Authentication.AuthenticationBackend.ResolveClientIp(forwarded));

        var mappedWithPort = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        mappedWithPort.Request.Headers["X-Forwarded-For"] = "198.51.100.7:51432";
        Assert.Equal("198.51.100.7", ForgeFlow.Web.Authentication.AuthenticationBackend.ResolveClientIp(mappedWithPort));

        var ipv6MappedFallback = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ipv6MappedFallback.Request.Headers["X-Forwarded-For"] = "not-an-ip";
        ipv6MappedFallback.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::ffff:192.168.1.24");
        Assert.Equal("192.168.1.24", ForgeFlow.Web.Authentication.AuthenticationBackend.ResolveClientIp(ipv6MappedFallback));
    }

    private sealed class CapturingOperationRecorder : IUserOperationRecorder
    {
        public List<UserOperationRecordRequest> Entries { get; } = [];
        public Task RecordAsync(UserOperationRecordRequest request, CancellationToken ct = default)
        {
            Entries.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuditStore : IAuditOperationsStore
    {
        public Task<OrganizationPage<AuditEntrySummary>> ListAuditEntries(OrganizationScope scope, AuditListQuery query, CancellationToken ct = default)
            => Task.FromResult(new OrganizationPage<AuditEntrySummary>(
                [
                    new AuditEntrySummary(
                        AuditSourceKind.Operations,
                        1,
                        new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc),
                        UserOperationCatalog.ReportsShortageExported,
                        1,
                        5,
                        1,
                        "=evilUser",
                        null,
                        null,
                        null,
                        0,
                        Guid.NewGuid(),
                        "{}")
                ],
                1,
                query.Page,
                query.PageSize));

        public Task<AuditEntryDetails?> GetAuditEntry(OrganizationScope scope, AuditSourceKind source, long entryId, CancellationToken ct = default)
            => Task.FromResult<AuditEntryDetails?>(null);

        public Task<IReadOnlyList<AuditActorOption>> ListActors(OrganizationScope scope, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditActorOption>>([]);

        public Task<IReadOnlyList<string>> ListDistinctEventCodes(OrganizationScope scope, AuditCategory category, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class FixedDiagnosticsFactory(DbContextOptions<DiagnosticsDbContext> options)
        : IDbContextFactory<DiagnosticsDbContext>
    {
        public DiagnosticsDbContext CreateDbContext() => new(options);
    }

    private sealed class EmptyScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => new EmptyScope();
        private sealed class EmptyScope : Microsoft.Extensions.DependencyInjection.IServiceScope
        {
            public IServiceProvider ServiceProvider => EmptyProvider.Instance;
            public void Dispose() { }
        }
        private sealed class EmptyProvider : IServiceProvider
        {
            public static readonly EmptyProvider Instance = new();
            public object? GetService(Type serviceType) => null;
        }
    }

    private sealed class StaticActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default)
            => ValueTask.FromResult(new PermissionDecision(true));
    }

    private sealed class InMemoryErrorLogStore : IErrorLogStore
    {
        public OrganizationScope? LastReadScope { get; private set; }
        public Task<OrganizationPage<ErrorLogSummary>> ListAsync(OrganizationScope scope, ErrorLogListQuery query, CancellationToken ct = default)
        {
            LastReadScope = scope;
            return Task.FromResult(new OrganizationPage<ErrorLogSummary>([], 0, query.Page, query.PageSize));
        }
        public Task<ErrorLogDetails?> GetAsync(OrganizationScope scope, long id, CancellationToken ct = default)
        {
            LastReadScope = scope;
            return Task.FromResult<ErrorLogDetails?>(null);
        }
        public Task<ErrorLogStats> GetStatsAsync(OrganizationScope scope, DateTime utcNow, CancellationToken ct = default)
        {
            LastReadScope = scope;
            return Task.FromResult(new ErrorLogStats(0, 0, 0, 0));
        }
        public Task<int> PurgeBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
