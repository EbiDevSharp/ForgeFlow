using System.Globalization;
using System.Reflection;
using Bit.BlazorUI;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Components.Layout;
using ForgeFlow.Web.Drafts;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Organizations;
using ForgeFlow.Web.Roles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;
using RolesPage = ForgeFlow.Web.Components.Pages.Roles;

namespace ForgeFlow.Web.Tests;

public sealed class RoleManagementPageTests
{
    private sealed class ActorStub : ITrustedActorAccessor
    {
        public TrustedActor? Actor = new(10, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(Actor);
    }

    private sealed class AllowOrgPermissions(long orgId, IReadOnlySet<string>? allowedPermissions = null) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requested, string permission, CancellationToken ct)
        {
            var orgMatch = requested is OrganizationScope.Organization o && o.OrganizationId == orgId;
            var permMatch = allowedPermissions is null || allowedPermissions.Contains(permission);
            return ValueTask.FromResult(new PermissionDecision(orgMatch && permMatch));
        }
    }

    private sealed class NoOpJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class RoleStoreStub : IRoleOperationsStore
    {
        public bool CreateCalled;
        public RoleDetails? CurrentDetails;

        public Task<OrganizationPage<RoleSummary>> ListOrganizationRoles(long organizationId, RoleListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<RoleSummary>([], 0, query.Page, query.PageSize));

        public Task<RoleDetails?> GetOrganizationRole(long organizationId, long roleId, CancellationToken ct)
            => Task.FromResult(CurrentDetails);

        public Task<OrganizationResult<RoleDetails>> CreateOrganizationRole(
            long organizationId, RoleDefinitionName canonicalName, RoleScopeTarget targetScope, IReadOnlyList<string> permissionCodes, OrganizationWriteAccess access, CancellationToken ct)
        {
            CreateCalled = true;
            var v = Convert.ToBase64String(new byte[8]);
            return Task.FromResult(OrganizationResult<RoleDetails>.Success(
                new RoleDetails(200, organizationId, canonicalName.Value, false, targetScope, true, v, permissionCodes)));
        }

        public Task<OrganizationResult<RoleDetails>> UpdateOrganizationRole(
            long organizationId, long roleId, RoleDefinitionName canonicalName, bool isActive, IReadOnlyList<string> permissionCodes, byte[] expectedVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Task.FromResult(OrganizationResult<RoleDetails>.Success(
                CurrentDetails! with { Name = canonicalName.Value, IsActive = isActive, Permissions = permissionCodes }));

        public Task<UserPermissionMatrix?> GetUserPermissions(long organizationId, long userId, CancellationToken ct)
            => Task.FromResult<UserPermissionMatrix?>(null);

        public Task<OrganizationResult<UserPermissionMatrix>> AssignUserRole(
            AssignUserRole request, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActive(
            long organizationId, long userId, long assignmentId, bool isActive, byte[] expectedVersion, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrants(
            long organizationId, long userId, RoleScopeTarget targetScope, long? siteId, IReadOnlyList<(string PermissionCode, bool IsActive, byte[]? ExpectedVersion)> normalizedChanges, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
    }

    [Fact]
    public void Roles_page_exposes_all_expected_routes()
    {
        var routes = typeof(RolesPage).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).OrderBy(x => x).ToArray();
        Assert.Equal(
            ["/organizations/{OrganizationId:long}/roles", "/organizations/{OrganizationId:long}/roles/{RoleId:long}", "/roles", "/roles/{RoleId:long}"],
            routes);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    public void Role_and_permission_localization_maps_are_exhaustive_in_both_cultures(string cultureName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        using var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<SharedResources>>();

        var prevCulture = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            var c = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = c;
            CultureInfo.CurrentUICulture = c;

            var errorFields = typeof(RolePermissionErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();
            Assert.NotEmpty(errorFields);
            foreach (var code in errorFields)
            {
                var key = RolesPage.FailureKey(code);
                var localized = localizer[key];
                Assert.False(localized.ResourceNotFound, $"Missing resource key '{key}' for error '{code}' in '{cultureName}'");
                Assert.False(string.IsNullOrWhiteSpace(localized.Value));
            }

            foreach (var module in RolePermissionLocalization.TenantModules)
            {
                var pageKey = RolePermissionLocalization.PageTitleKey(module);
                var pageTitle = localizer[pageKey];
                Assert.False(pageTitle.ResourceNotFound);
                Assert.False(string.IsNullOrWhiteSpace(pageTitle.Value));
            }

            foreach (var perm in RolePermissionLocalization.OrganizationScopePermissions)
            {
                var opKey = RolePermissionLocalization.OperationLabelKey(perm.Code);
                var opLabel = localizer[opKey];
                Assert.False(opLabel.ResourceNotFound);
                Assert.False(string.IsNullOrWhiteSpace(opLabel.Value));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    [Fact]
    public void ToggleOperationWithAutoRead_automatically_checks_Read_on_write_and_clears_writes_on_Read_uncheck()
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var descriptors = RolePermissionLocalization.OrganizationScopePermissions;
        var sitesCreate = descriptors.Single(x => x.Code == "Sites.Create");
        var sitesUpdate = descriptors.Single(x => x.Code == "Sites.Update");
        var sitesRead = descriptors.Single(x => x.Code == "Sites.Read");

        RolePermissionLocalization.ToggleOperationWithAutoRead(selected, sitesCreate, isChecked: true, descriptors);
        Assert.Contains("Sites.Create", selected);
        Assert.Contains("Sites.Read", selected);

        RolePermissionLocalization.ToggleOperationWithAutoRead(selected, sitesUpdate, isChecked: true, descriptors);
        Assert.Contains("Sites.Update", selected);

        RolePermissionLocalization.ToggleOperationWithAutoRead(selected, sitesRead, isChecked: false, descriptors);
        Assert.Empty(selected);
    }

    [Theory]
    [InlineData("fa-IR", "نقش‌ها")]
    [InlineData("en-US", "Roles")]
    public async Task Roles_page_renders_bilingual_denied_state_when_actor_unavailable(string culture, string expectedHeading)
    {
        var prev = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            var info = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = info;
            CultureInfo.CurrentUICulture = info;

            var services = new ServiceCollection().AddLogging();
            services.AddLocalization(o => o.ResourcesPath = "Resources");
            services.AddSingleton<IJSRuntime, NoOpJs>();
            services.AddBitBlazorUIServices();
            services.AddSingleton<CulturePolicy>();
            services.AddScoped<DraftCoordinator>();
            services.AddDbContext<AuthDbContext>(o => o.UseSqlServer("Server=invalid.invalid;Database=missing;Integrated Security=True;Encrypt=True;Connect Timeout=1"));
            var nullActor = new ActorStub { Actor = null };
            var grants = new AllowOrgPermissions(1);
            services.AddSingleton<ITrustedActorAccessor>(nullActor);
            services.AddSingleton<IOrganizationPermissionEvaluator>(grants);
            services.AddSingleton<IRoleOperationsStore>(new RoleStoreStub());
            services.AddScoped<RoleUseCases>();
            services.AddScoped<ReadScopes>();

            await using var provider = services.BuildServiceProvider();
            var localizer = provider.GetRequiredService<IStringLocalizer<SharedResources>>();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var (html, deniedText) = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                CultureInfo.CurrentCulture = info;
                CultureInfo.CurrentUICulture = info;
                var root = await renderer.RenderComponentAsync<RolesPage>(ParameterView.Empty);
                return (System.Net.WebUtility.HtmlDecode(root.ToHtmlString()), localizer["ReadDenied"].Value);
            });

            Assert.Contains(expectedHeading, html);
            Assert.Contains(deniedText, html);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    [Fact]
    public async Task Create_and_update_role_dialog_flow_persists_draft_scope_and_updates_state()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = new RolesPage();
        var v = Convert.ToBase64String(new byte[8]);
        var store = new RoleStoreStub
        {
            CurrentDetails = new RoleDetails(100, 1, "Auditor", false, RoleScopeTarget.Organization, true, v, ["Sites.Read"])
        };
        var actors = new ActorStub();
        var grants = new AllowOrgPermissions(1);

        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<SharedResources>>();

        typeof(RolesPage).GetProperty("RolesService", flags)!.SetValue(page, new RoleUseCases(store, actors, grants));
        typeof(RolesPage).GetProperty("Text", flags)!.SetValue(page, localizer);
        typeof(RolesPage).GetField("available", flags)!.SetValue(page, new List<OrganizationScope> { new OrganizationScope.Organization(1) });
        typeof(RolesPage).GetField("scopeCanManageRoles", flags)!.SetValue(page, true);
        typeof(RolesPage).GetField("currentActorId", flags)!.SetValue(page, 10L);

        await (Task)typeof(RolesPage).GetMethod("OpenCreate", flags)!.Invoke(page, null)!;
        var pendingDraft = (DraftDefinition)typeof(RolesPage).GetField("pendingDraft", flags)!.GetValue(page)!;
        Assert.Equal("actor:10:org:1:create-role", pendingDraft.Scope);
        var field = Assert.Single(pendingDraft.Fields);
        Assert.Equal("Name", field.Name);

        typeof(RolesPage).GetField("createName", flags)!.SetValue(page, "Quality Inspector");
        var createPerms = (HashSet<string>)typeof(RolesPage).GetField("createSelectedPerms", flags)!.GetValue(page)!;
        createPerms.Add("Sites.Read");
        await (Task)typeof(RolesPage).GetMethod("SubmitCreate", flags)!.Invoke(page, null)!;

        Assert.True(store.CreateCalled);
        Assert.False((bool)typeof(RolesPage).GetField("creating", flags)!.GetValue(page)!);
        Assert.False((bool)typeof(RolesPage).GetField("createOpen", flags)!.GetValue(page)!);

        await (Task)typeof(RolesPage).GetMethod("OpenRoleDialog", flags)!.Invoke(page, [100L, true])!;
        Assert.True((bool)typeof(RolesPage).GetField("detailOpen", flags)!.GetValue(page)!);
        typeof(RolesPage).GetField("confirmToggleActive", flags)!.SetValue(page, false);
        await (Task)typeof(RolesPage).GetMethod("ExecuteToggleActive", flags)!.Invoke(page, null)!;
        var updated = (RoleDetails)typeof(RolesPage).GetField("selected", flags)!.GetValue(page)!;
        Assert.False(updated.IsActive);
    }

    [Fact]
    public void MainLayout_omits_Settings_parent_menu_when_actor_has_no_accessible_child_items()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var layout = new MainLayout();
        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<SharedResources>>();
        typeof(MainLayout).GetProperty("Text", flags)!.SetValue(layout, localizer);

        var navItemsWhenEmpty = (IList<BitNavItem>)typeof(MainLayout).GetProperty("NavigationItems", flags)!.GetValue(layout)!;
        var singleHome = Assert.Single(navItemsWhenEmpty);
        Assert.Equal("/", singleHome.Url);

        typeof(MainLayout).GetField("roleScopes", flags)!.SetValue(
            layout, new List<OrganizationScope> { new OrganizationScope.Organization(1) });
        var navItemsWithRoles = (IList<BitNavItem>)typeof(MainLayout).GetProperty("NavigationItems", flags)!.GetValue(layout)!;
        Assert.Equal(2, navItemsWithRoles.Count);
        var settingsGroup = navItemsWithRoles[1];
        var roleChild = Assert.Single(settingsGroup.ChildItems);
        Assert.Equal("/roles", roleChild.Url);
    }

    [Fact]
    public void MainLayout_orders_navigation_groups_by_workflow_priority_and_keeps_them_collapsed()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var layout = new MainLayout();
        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<SharedResources>>();
        typeof(MainLayout).GetProperty("Text", flags)!.SetValue(layout, localizer);

        var scope = new List<OrganizationScope> { new OrganizationScope.Organization(1) };
        foreach (var field in new[] { "unitScopes", "procurementScopes", "inventoryScopes", "productionOrderScopes", "qualityScopes", "reportScopes", "orgScopes" })
            typeof(MainLayout).GetField(field, flags)!.SetValue(layout, scope);

        var navItems = (IList<BitNavItem>)typeof(MainLayout).GetProperty("NavigationItems", flags)!.GetValue(layout)!;
        Assert.Equal(
            [
                localizer["Home"].Value,
                localizer["MasterDataNavGroup"].Value,
                localizer["ProcurementNavGroup"].Value,
                localizer["InventoryNavGroup"].Value,
                localizer["ProductionNavGroup"].Value,
                localizer["QualityNavGroup"].Value,
                localizer["ReportsNavGroup"].Value,
                localizer["Settings"].Value
            ],
            navItems.Select(x => x.Text).ToArray());
        Assert.All(navItems.Skip(1), group => Assert.False(group.IsExpanded));
    }
}
