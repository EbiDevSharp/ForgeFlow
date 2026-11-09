using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class OrganizationPersistenceModelTests
{
    internal static OrganizationDbContext OfflineContext() => new(new DbContextOptionsBuilder<OrganizationDbContext>()
        .UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=true;Encrypt=true;Connect Timeout=1").Options);

    [Fact]
    public void Business_tables_have_exact_unfiltered_uniqueness_and_scope_keys()
    {
        using var db = OfflineContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Equal(3, model.GetEntityTypes().Count());
        var org = model.FindEntityType(typeof(Organization))!;
        var site = model.FindEntityType(typeof(Site))!;
        Assert.Equal("organization", org.GetSchema());
        Assert.Equal("Organization", org.GetTableName());
        Assert.Equal("Site", site.GetTableName());
        Assert.Equal(new[] { "Id" }, org.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Contains(site.GetKeys(), k => k.Properties.Select(p => p.Name).SequenceEqual(new[] { "OrganizationId", "Id" }));
        var orgIndex = Assert.Single(org.GetIndexes(), i => i.IsUnique);
        Assert.Equal(new[] { "Code" }, orgIndex.Properties.Select(p => p.Name));
        Assert.Null(orgIndex.GetFilter());
        var siteIndex = Assert.Single(site.GetIndexes(), i => i.IsUnique);
        Assert.Equal(new[] { "OrganizationId", "Code" }, siteIndex.Properties.Select(p => p.Name));
        Assert.Null(siteIndex.GetFilter());
        Assert.All(model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }

    [Fact]
    public void Columns_checks_and_rowversion_match_the_physical_contract()
    {
        using var db = OfflineContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(Organization), typeof(Site) })
        {
            var entity = model.FindEntityType(type)!;
            var code = entity.FindProperty("Code")!;
            Assert.Equal(32, code.GetMaxLength());
            Assert.False(code.IsUnicode());
            Assert.Equal("Latin1_General_100_BIN2", code.GetCollation());
            Assert.False(code.IsNullable);
            Assert.Equal(200, entity.FindProperty("Name")!.GetMaxLength());
            Assert.False(entity.FindProperty("Name")!.IsNullable);
            var rowversion = entity.FindProperty("RowVersion")!;
            Assert.True(rowversion.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, rowversion.ValueGenerated);
            Assert.False(rowversion.IsNullable);
            Assert.Equal("datetime2(7)", entity.FindProperty("CreatedAtUtc")!.GetColumnType());
            Assert.False(entity.FindProperty("CreatedAtUtc")!.IsNullable);
            Assert.True(entity.FindProperty("UpdatedAtUtc")!.IsNullable);
            var checks = entity.GetCheckConstraints().Select(c => c.Sql).ToArray();
            Assert.Contains(checks, sql => sql.Contains("DATALENGTH([Code]) = LEN([Code])", StringComparison.Ordinal));
            Assert.Contains(checks, sql => sql.Contains("[UpdatedByActorId] IS NOT NULL", StringComparison.Ordinal));
        }
        var audit = model.FindEntityType(typeof(OrganizationChange))!;
        Assert.Equal(2000, audit.FindProperty("ChangesJson")!.GetMaxLength());
        Assert.Equal(64, audit.FindProperty("EventCode")!.GetMaxLength());
        Assert.True(audit.FindProperty("SiteId")!.IsNullable);
        Assert.Contains(audit.GetForeignKeys(), fk => fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "OrganizationId", "SiteId" })
            && fk.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(new[] { "OrganizationId", "Id" }));
    }

    [Fact]
    public void Factory_creates_independent_operation_contexts_and_does_not_register_a_circuit_context()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:ForgeFlow"] = "Server=invalid.invalid;Database=DesignOnly;Integrated Security=true;Encrypt=true" }).Build();
        using var provider = new ServiceCollection().AddOrganizationPersistence(configuration).BuildServiceProvider();
        Assert.Null(provider.GetService<OrganizationDbContext>());
        var factory = provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>();
        using var first = factory.CreateDbContext();
        using var second = factory.CreateDbContext();
        Assert.NotSame(first, second);
        Assert.NotSame(first.ChangeTracker, second.ChangeTracker);
    }

    [Fact]
    public void Missing_connection_is_lazy_and_has_no_shell_startup_or_network_dependency()
    {
        using var provider = new ServiceCollection().AddOrganizationPersistence(new ConfigurationBuilder().Build()).BuildServiceProvider();
        Assert.Equal("OrganizationSite.PersistenceUnavailable",
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDbContextFactory<OrganizationDbContext>>()).Message);
    }

    [Fact]
    public async Task Unattributed_business_writes_are_blocked_before_any_connection()
    {
        await using var db = OfflineContext();
        db.Organizations.Add(Organization.Create("ORG", "Company"));
        Assert.Equal("OrganizationSite.ActorRequired", Assert.Throws<InvalidOperationException>(() => db.SaveChanges()).Message);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal("OrganizationSite.ActorRequired", error.Message);
    }

    [Fact]
    public void Migration_snapshot_matches_the_model_and_generates_only_the_three_scoped_tables()
    {
        using var db = OfflineContext();
        Assert.Single(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE [organization].[Organization]", script);
        Assert.Contains("CREATE TABLE [organization].[Site]", script);
        Assert.Contains("CREATE TABLE [organization].[OrganizationChange]", script);
        Assert.Contains("rowversion", script);
        Assert.Contains("DENY DELETE", script);
        Assert.Contains("DENY UPDATE, DELETE", script);
        Assert.DoesNotContain("AspNetUsers", script);
        // SQL generation is structural evidence only; live execution is covered by opt-in SQL tests.
    }

    [Fact]
    public void Ef_materialization_and_relationship_fixup_preserve_domain_ownership_and_lifecycle()
    {
        using var db = OfflineContext();
        T Materialize<T>(Dictionary<string, object?> values) where T : class
        {
            var entityType = db.Model.FindEntityType(typeof(T))!;
            var data = new object?[entityType.GetProperties().Count()];
            foreach (var property in entityType.GetProperties())
                data[property.GetIndex()] = values.GetValueOrDefault(property.Name);
            var buffer = new ValueBuffer(data);
            return (T)db.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(entityType)(new MaterializationContext(buffer, db));
        }
        var organization = Materialize<Organization>(new() {
            ["Id"] = 10L, ["Code"] = "ORG", ["Name"] = "Company", ["IsActive"] = true });
        var site = Materialize<Site>(new() {
            ["Id"] = 20L, ["OrganizationId"] = 10L, ["Code"] = "SITE", ["Name"] = "Plant", ["IsActive"] = true });
        db.Attach(organization);
        db.Attach(site);
        Assert.Same(organization, site.Organization);
        Assert.Same(site, Assert.Single(organization.Sites));
        Assert.Equal(organization.Id, site.OrganizationId);
        Assert.Equal(OrganizationSiteErrors.OrganizationHasActiveSites,
            Assert.Throws<OrganizationSiteRuleException>(() => organization.Deactivate()).Code);
        site.Deactivate();
        Assert.True(organization.Deactivate());
        // Exercises EF materialization/fixup, not a SQL query or store round trip.
    }
}
