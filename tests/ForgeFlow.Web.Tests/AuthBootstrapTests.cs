using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using ForgeFlow.Bootstrap;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthBootstrapTests
{
    internal static AuthDbContext Offline() => new AuthDesignTimeFactory().CreateDbContext([]);
    internal static BootstrapRequest Request(Guid? operation = null) => new(operation ?? Guid.NewGuid(), "initial.admin", "company", "شرکت نمونه", "plant", "سایت نمونه");
    internal static string Password() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    [Theory]
    [InlineData("INSERT INTO [organization].[OrganizationChange] ([ActorId]) OUTPUT INSERTED.[Id] VALUES (@p0);")]
    [InlineData("MERGE [organization].[OrganizationChange] USING (VALUES (@p0, 0), (@p1, 1)) AS i ([ActorId], _Position) ON 1=0 WHEN NOT MATCHED THEN INSERT ([ActorId]) VALUES (i.[ActorId]) OUTPUT INSERTED.[Id], i._Position;")]
    public async Task Business_audit_fault_reaches_its_sentinel_for_single_and_batched_insert_forms(string sql)
    {
        var interceptor = new AuthBootstrapSqlTests.RejectStage("business-audit");
        using var command = new SqlCommand(sql);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => interceptor.ReaderExecutingAsync(command, null!, default).AsTask());
        Assert.Same(interceptor.Failure, error); Assert.Equal(1, interceptor.FailureHits);
        // Command matching only; no SQL connection or transaction proof is claimed.
    }

    [Theory]
    [InlineData("SELECT * FROM [organization].[OrganizationChange]")]
    [InlineData("INSERT INTO [organization].[Organization] ([Code]) VALUES (@p0)")]
    [InlineData("MERGE [auth].[SecurityEvent] USING (VALUES (@p0)) AS i ([Id]) ON 1=0 WHEN NOT MATCHED THEN INSERT ([Id]) VALUES (i.[Id]);")]
    public async Task Business_audit_fault_does_not_fire_for_reads_or_other_persistence_stages(string sql)
    {
        var interceptor = new AuthBootstrapSqlTests.RejectStage("business-audit");
        using var command = new SqlCommand(sql);
        await interceptor.ReaderExecutingAsync(command, null!, default);
        Assert.Equal(0, interceptor.FailureHits);
    }

    [Fact]
    public void Model_contains_only_approved_auth_tables_and_excludes_existing_business_tables()
    {
        using var db = Offline(); var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Equal(17, model.GetEntityTypes().Count(e => e.GetSchema() == "auth"));
        Assert.True(model.FindEntityType(typeof(Organization))!.IsTableExcludedFromMigrations());
        Assert.True(model.FindEntityType(typeof(Site))!.IsTableExcludedFromMigrations());
        Assert.All(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
        Assert.DoesNotContain(model.GetEntityTypes(), e => e.Name.Contains("IdentityRole", StringComparison.Ordinal) || e.Name.Contains("IdentityUserPasskey", StringComparison.Ordinal));
        var user = model.FindEntityType(typeof(ApplicationAccount))!;
        Assert.Equal(typeof(long), Assert.Single(user.FindPrimaryKey()!.Properties).ClrType);
        Assert.Equal(128, user.FindProperty("NormalizedUserName")!.GetMaxLength());
        Assert.Equal("Latin1_General_100_BIN2", user.FindProperty("NormalizedUserName")!.GetCollation());
        Assert.Null(Assert.Single(user.GetIndexes(), i => i.IsUnique).GetFilter());
        Assert.False(user.FindProperty("PasswordHash")!.IsNullable);
        Assert.True(user.FindProperty("ConcurrencyStamp")!.IsConcurrencyToken);
        Assert.True(user.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Empty(user.GetSeedData());
        var seed = Assert.Single(model.FindEntityType(typeof(BootstrapState))!.GetSeedData());
        Assert.Equal((byte)0, seed["State"]);
        Assert.Empty(model.FindEntityType(typeof(ScopedRoleAssignment))!.GetSeedData());
        var site = model.FindEntityType(typeof(SiteMembership))!;
        Assert.Contains(site.GetForeignKeys(), fk => fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "OrganizationId", "SiteId" }));
        Assert.Equal(3, model.FindEntityType(typeof(ScopedRoleAssignment))!.GetIndexes().Count(i => i.IsUnique));
    }

    [Fact]
    public void Additive_migration_snapshot_and_role_denials_match_without_touching_business_tables()
    {
        using var db = Offline();
        Assert.False(db.Database.HasPendingModelChanges());
        var migrations = db.GetService<IMigrationsAssembly>();
        Assert.Equal(9, migrations.Migrations.Count);
        var baseline = migrations.CreateMigration(migrations.Migrations.ElementAt(0).Value, db.Database.ProviderName!);
        Assert.Equal(10, baseline.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.All(baseline.UpOperations.OfType<CreateTableOperation>(), table => Assert.Equal("auth", table.Schema));
        Assert.DoesNotContain(baseline.UpOperations, o => o is DropTableOperation or AlterColumnOperation or DropColumnOperation);
        var slice03 = migrations.CreateMigration(migrations.Migrations.ElementAt(1).Value, db.Database.ProviderName!);
        Assert.Equal(3, slice03.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.All(slice03.UpOperations.OfType<CreateTableOperation>(), table => Assert.Equal("auth", table.Schema));
        Assert.DoesNotContain(slice03.UpOperations, o => o is DropTableOperation or DropColumnOperation);
        var profileMig = migrations.CreateMigration(migrations.Migrations.ElementAt(2).Value, db.Database.ProviderName!);
        Assert.Single(profileMig.UpOperations.OfType<CreateTableOperation>());
        Assert.All(profileMig.UpOperations.OfType<CreateTableOperation>(), table => Assert.Equal("auth", table.Schema));
        Assert.DoesNotContain(profileMig.UpOperations, o => o is DropTableOperation or DropColumnOperation);
        var sessionMig = migrations.CreateMigration(migrations.Migrations.ElementAt(3).Value, db.Database.ProviderName!);
        Assert.DoesNotContain(sessionMig.UpOperations, o => o is DropTableOperation or DropColumnOperation);
        var notifMig = migrations.CreateMigration(migrations.Migrations.ElementAt(4).Value, db.Database.ProviderName!);
        Assert.Equal(2, notifMig.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.All(notifMig.UpOperations.OfType<CreateTableOperation>(), table => Assert.Equal("auth", table.Schema));
        Assert.DoesNotContain(notifMig.UpOperations, o => o is DropTableOperation or DropColumnOperation);
        var devMig = migrations.CreateMigration(migrations.Migrations.ElementAt(5).Value, db.Database.ProviderName!);
        Assert.DoesNotContain(devMig.UpOperations, o => o is DropTableOperation or DropColumnOperation);
        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.DoesNotContain("CREATE TABLE [organization]", script);
        Assert.DoesNotContain("ALTER TABLE [organization]", script);
        Assert.DoesNotContain("INSERT INTO [auth].[User]", script);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[BootstrapState]", script);
        Assert.Contains("DENY UPDATE, DELETE ON OBJECT::[auth].[SecurityEvent]", script);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::[auth].[RoleDefinition]", script);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserPreference]", script);
        Assert.Contains("DENY DELETE ON OBJECT::[auth].[UserPreference]", script);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE ON OBJECT::[auth].[UserNotification]", script);
        Assert.Contains("DENY DELETE ON OBJECT::[auth].[UserNotification]", script);
        Assert.Contains("Auth.NonEmptyRollbackDenied", db.GetService<IMigrator>().GenerateScript("IdentityBootstrapBaseline", "0"));
    }

    [Fact]
    public async Task Real_identity_validators_normalizer_and_password_hasher_are_used_without_a_fake_store()
    {
        var services = new ServiceCollection().AddBootstrapIdentity(options => options.UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=true;Encrypt=true"));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
        Assert.Equal("INITIAL.ADMIN", manager.NormalizeName("initial.admin"));
        Assert.False(scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.HasPendingModelChanges());
        var user = new ApplicationAccount { UserName = "initial.admin" }; var password = Password();
        var validators = manager.PasswordValidators;
        foreach (var validator in validators) Assert.True((await validator.ValidateAsync(manager, user, password)).Succeeded);
        Assert.Contains(await Task.WhenAll(validators.Select(v => v.ValidateAsync(manager, user, password[..4]))), r => !r.Succeeded);
        var oversized = Password() + Password() + Password() + Password() + Password();
        Assert.False((await new BootstrapPasswordValidator().ValidateAsync(manager, user, oversized)).Succeeded);
        var hash = manager.PasswordHasher.HashPassword(user, password);
        Assert.Equal(PasswordVerificationResult.Success, manager.PasswordHasher.VerifyHashedPassword(user, hash, password));
        Assert.Equal(PasswordVerificationResult.Failed, manager.PasswordHasher.VerifyHashedPassword(user, hash, Password()));
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("site")]
    [InlineData("username")]
    [InlineData("operation")]
    [InlineData("password")]
    public async Task Invalid_bootstrap_inputs_are_rejected_before_any_sql_connection(string invalid)
    {
        var request = Request(); var password = Password();
        request = invalid switch {
            "organization" => request with { OrganizationCode = "کد" },
            "site" => request with { SiteCode = "-INVALID" },
            "username" => request with { UserName = "نام" },
            "operation" => request with { OperationId = Guid.Empty }, _ => request };
        if (invalid == "password") password = password[..4];
        var result = await new HostBootstrapper("not a connection string").InitializeAsync(request, password, HostOperatorConsent.Confirm("INITIALIZE"));
        Assert.Equal(invalid == "organization" ? "Organization.CodeInvalid" : invalid == "site" ? "Site.CodeInvalid" : "Auth.BootstrapInputInvalid", result.Code);
    }

    [Fact]
    public async Task Operator_confirmation_is_explicit_and_never_an_automatic_application_grant()
    {
        Assert.Null(HostOperatorConsent.Confirm(null)); Assert.Null(HostOperatorConsent.Confirm("initialize"));
        var result = await new HostBootstrapper("not a connection string").InitializeAsync(Request(), Password(), null);
        Assert.Equal("Auth.OperatorRequired", result.Code);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    public void Host_prompts_resolve_explicit_resources(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var provider = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
            var text = provider.GetRequiredService<IStringLocalizer<BootstrapText>>();
            foreach (var key in new[] { "Usage", "Password", "PasswordAgain", "OperatorResponsibility", "Result", "Failure" })
                Assert.False(text[key].ResourceNotFound);
            Assert.Equal(2, System.Text.CompositeFormat.Parse(text["Result"].Value).MinimumArgumentCount);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
