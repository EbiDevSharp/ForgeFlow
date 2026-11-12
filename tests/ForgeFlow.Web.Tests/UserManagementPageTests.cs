using System.Globalization;
using System.Reflection;
using Bit.BlazorUI;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Users;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Drafts;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UserManagementPageTests
{
    private sealed class ActorStub : ITrustedActorAccessor
    {
        public TrustedActor? Actor = new(10, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(Actor);
    }

    private sealed class AllowOrgAdminGrants(long orgId) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requested, string permission, CancellationToken ct)
            => ValueTask.FromResult(new PermissionDecision(requested is OrganizationScope.Organization o && o.OrganizationId == orgId));
    }

    private sealed class NoOpJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class StoreStub : IUserOperationsStore
    {
        public string? LastPasswordSeen;
        public bool CreateCalled;
        public UserDetails? CurrentDetails;

        public Task<OrganizationPage<UserSummary>> ListOrganizationUsers(long organizationId, UserListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<UserSummary>([], 0, query.Page, query.PageSize));

        public Task<UserDetails?> GetOrganizationUser(long organizationId, long userId, CancellationToken ct)
            => Task.FromResult(CurrentDetails);

        public Task<OrganizationResult<UserDetails>> CreateOrganizationUser(
            CreateOrganizationUser request, UserAccountName canonicalUserName, IReadOnlyList<long> distinctSiteIds, OrganizationWriteAccess access, CancellationToken ct)
        {
            CreateCalled = true;
            LastPasswordSeen = request.InitialPassword;
            var v = Convert.ToBase64String(new byte[8]);
            return Task.FromResult(OrganizationResult<UserDetails>.Success(
                new UserDetails(200, request.OrganizationId, canonicalUserName.Value, true, true, false, null, true, request.AssignOrganizationAdmin, v, v, v, [], false, null, 0, 0)));
        }

        public Task<OrganizationResult<UserDetails>> AttachExistingUserToOrganization(
            AttachExistingUserToOrganization request, UserAccountName canonicalUserName, IReadOnlyList<long> distinctSiteIds, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> SetUserActive(long? organizationId, long userId, bool isActive, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct)
            => Task.FromResult(OrganizationResult<UserDetails>.Success(CurrentDetails! with { AccountIsActive = isActive }));

        public Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActive(long organizationId, long userId, bool isActive, byte[] expectedMembershipVersion, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> AssignSiteMembership(AssignSiteMembership request, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> SetSiteMembershipActive(long organizationId, long userId, long siteId, bool isActive, byte[] expectedMembershipVersion, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> AssignTenantRole(AssignTenantRole request, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> SetTenantRoleActive(long organizationId, long userId, TenantRoleKind role, long? siteId, bool isActive, byte[] expectedRoleVersion, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> UnlockUser(long? organizationId, long userId, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> ResetUserPassword(long? organizationId, long userId, string newPassword, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct, bool requirePasswordChange = true)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> ResetUserTwoFactor(long? organizationId, long userId, string reason, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UserDetails>> RevokeUserSessions(long? organizationId, long userId, byte[] expectedAccountVersion, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class OrgStoreStub : IOrganizationOperationsStore
    {
        public Task<OrganizationPage<OrganizationDetails>> ListOrganizations(long organizationId, OrganizationListQuery query, CancellationToken ct) => Task.FromResult(new OrganizationPage<OrganizationDetails>([], 0, 1, 25));
        public Task<OrganizationDetails?> GetOrganization(long organizationId, CancellationToken ct) => Task.FromResult<OrganizationDetails?>(null);
        public Task<OrganizationPage<SiteDetails>> ListSites(long organizationId, long? siteId, OrganizationListQuery query, CancellationToken ct) => Task.FromResult(new OrganizationPage<SiteDetails>([], 0, 1, 100));
        public Task<SiteDetails?> GetSite(long organizationId, long siteId, CancellationToken ct) => Task.FromResult<SiteDetails?>(null);
        public Task<OrganizationResult<OrganizationDetails>> CreateOrganization(CreateOrganization request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SiteDetails>> CreateSite(CreateSite request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<OrganizationDetails>> UpdateOrganization(long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SiteDetails>> UpdateSite(long organizationId, long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class RoleStoreStub : IRoleOperationsStore
    {
        public Task<OrganizationPage<RoleSummary>> ListOrganizationRoles(long organizationId, RoleListQuery query, CancellationToken ct) => Task.FromResult(new OrganizationPage<RoleSummary>([], 0, 1, 25));
        public Task<RoleDetails?> GetOrganizationRole(long organizationId, long roleId, CancellationToken ct) => Task.FromResult<RoleDetails?>(null);
        public Task<OrganizationResult<RoleDetails>> CreateOrganizationRole(long organizationId, RoleDefinitionName canonicalName, RoleScopeTarget targetScope, IReadOnlyList<string> permissionCodes, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<RoleDetails>> UpdateOrganizationRole(long organizationId, long roleId, RoleDefinitionName canonicalName, bool isActive, IReadOnlyList<string> permissionCodes, byte[] expectedVersion, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<UserPermissionMatrix?> GetUserPermissions(long organizationId, long userId, CancellationToken ct) => Task.FromResult<UserPermissionMatrix?>(null);
        public Task<OrganizationResult<UserPermissionMatrix>> AssignUserRole(AssignUserRole request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<UserPermissionMatrix>> SetUserRoleAssignmentActive(long organizationId, long userId, long assignmentId, bool isActive, byte[] expectedVersion, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<UserPermissionMatrix>> SaveUserOperationGrants(long organizationId, long userId, RoleScopeTarget targetScope, long? siteId, IReadOnlyList<(string PermissionCode, bool IsActive, byte[]? ExpectedVersion)> normalizedChanges, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public void Users_page_declares_all_expected_routes()
    {
        var routes = typeof(Users).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToArray();
        Assert.Contains("/users", routes);
        Assert.Contains("/organizations/{OrganizationId:long}/users", routes);
        Assert.Contains("/organizations/{OrganizationId:long}/users/{UserId:long}", routes);
        Assert.Contains("/users/{UserId:long}", routes);
    }

    [Fact]
    public void Every_user_management_error_maps_to_non_missing_localized_resource()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<SharedResources>>();

        var codes = typeof(UserManagementErrors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetValue(null)!)
            .Concat([OrganizationSiteErrors.OrganizationNotFound, OrganizationSiteErrors.SiteNotFound, OrganizationSiteErrors.OrganizationInactive]);

        foreach (var culture in CulturePolicy.Supported)
        {
            var prev = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                foreach (var code in codes)
                {
                    var key = Users.FailureKey(code);
                    var text = localizer[key];
                    Assert.False(text.ResourceNotFound, $"Missing resource '{key}' for error '{code}' in '{culture}'.");
                    Assert.False(string.IsNullOrWhiteSpace(text.Value));
                }
            }
            finally
            {
                CultureInfo.CurrentUICulture = prev;
            }
        }
    }

    [Theory]
    [InlineData("fa-IR", "کاربران")]
    [InlineData("en-US", "Users")]
    public async Task Users_page_renders_bilingual_denied_state_when_actor_unavailable(string culture, string expectedHeading)
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
            services.AddSingleton<ForgeFlow.Web.Localization.DisplayFormatter>();
            services.AddScoped<DraftCoordinator>();
            services.AddDbContext<AuthDbContext>(o => o.UseSqlServer("Server=invalid.invalid;Database=missing;Integrated Security=True;Encrypt=True;Connect Timeout=1"));
            var nullActor = new ActorStub { Actor = null };
            var grants = new AllowOrgAdminGrants(1);
            services.AddSingleton<ITrustedActorAccessor>(nullActor);
            services.AddSingleton<IOrganizationPermissionEvaluator>(grants);
            services.AddSingleton<IUserOperationsStore>(new StoreStub());
            services.AddSingleton<IRoleOperationsStore>(new RoleStoreStub());
            services.AddSingleton<IOrganizationOperationsStore>(new OrgStoreStub());
            services.AddScoped<UserUseCases>();
            services.AddScoped<RoleUseCases>();
            services.AddScoped<OrganizationUseCases>();
            services.AddScoped<ReadScopes>();

            await using var provider = services.BuildServiceProvider();
            var localizer = provider.GetRequiredService<IStringLocalizer<SharedResources>>();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var (html, deniedText) = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                CultureInfo.CurrentCulture = info;
                CultureInfo.CurrentUICulture = info;
                var root = await renderer.RenderComponentAsync<Users>(ParameterView.Empty);
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
    public async Task Create_user_submission_clears_sensitive_password_and_draft_scope_excludes_passwords()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = new Users();
        var store = new StoreStub();
        var actors = new ActorStub();
        var grants = new AllowOrgAdminGrants(1);

        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider();
        var localizer = services.GetRequiredService<IStringLocalizer<SharedResources>>();

        typeof(Users).GetProperty("UsersService", flags)!.SetValue(page, new UserUseCases(store, actors, grants));
        typeof(Users).GetProperty("Text", flags)!.SetValue(page, localizer);
        typeof(Users).GetField("available", flags)!.SetValue(page, new List<OrganizationScope> { new OrganizationScope.Organization(1) });
        typeof(Users).GetField("scopeCanCreate", flags)!.SetValue(page, true);
        typeof(Users).GetField("currentActorId", flags)!.SetValue(page, 10L);
        typeof(Users).GetField("createUserName", flags)!.SetValue(page, "new.operator");
        typeof(Users).GetField("createPassword", flags)!.SetValue(page, "StrongPassword!123");

        typeof(Users).GetMethod("OpenCreate", flags)!.Invoke(page, null);
        var pendingDraft = (DraftDefinition)typeof(Users).GetField("pendingDraft", flags)!.GetValue(page)!;
        Assert.Equal("actor:10:org:1:create-user", pendingDraft.Scope);
        var field = Assert.Single(pendingDraft.Fields);
        Assert.Equal("UserName", field.Name);

        typeof(Users).GetField("createUserName", flags)!.SetValue(page, "new.operator");
        typeof(Users).GetField("createPassword", flags)!.SetValue(page, "StrongPassword!123");
        await (Task)typeof(Users).GetMethod("SubmitCreate", flags)!.Invoke(page, null)!;

        Assert.True(store.CreateCalled);
        Assert.Equal("StrongPassword!123", store.LastPasswordSeen);
        Assert.Equal(string.Empty, (string?)typeof(Users).GetField("createPassword", flags)!.GetValue(page));
        Assert.False((bool)typeof(Users).GetField("creating", flags)!.GetValue(page)!);
        Assert.False((bool)typeof(Users).GetField("createOpen", flags)!.GetValue(page)!);
    }

    [Fact]
    public async Task User_mutation_clears_saving_flag_and_updates_selected_state()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = new Users();
        var v = Convert.ToBase64String(new byte[8]);
        var store = new StoreStub
        {
            CurrentDetails = new UserDetails(77, 1, "worker", true, true, false, null, true, false, null, v, v, [], false, null, 0, 0)
        };
        var actors = new ActorStub();
        var grants = new AllowOrgAdminGrants(1);

        typeof(Users).GetProperty("UsersService", flags)!.SetValue(page, new UserUseCases(store, actors, grants));
        typeof(Users).GetField("available", flags)!.SetValue(page, new List<OrganizationScope> { new OrganizationScope.Organization(1) });
        typeof(Users).GetField("selected", flags)!.SetValue(page, store.CurrentDetails);

        await (Task)typeof(Users).GetMethod("OpenUserDialog", flags)!.Invoke(page, [77L, true])!;
        Assert.True((bool)typeof(Users).GetField("detailOpen", flags)!.GetValue(page)!);
        Assert.True((bool)typeof(Users).GetField("editMode", flags)!.GetValue(page)!);

        var confirmKind = typeof(Users).GetNestedType("ConfirmActionKind", BindingFlags.NonPublic)!;
        var disableAccount = Enum.Parse(confirmKind, "DisableAccount");
        typeof(Users).GetMethod("AskConfirm", flags)!.Invoke(page, [disableAccount]);
        await (Task)typeof(Users).GetMethod("ExecuteConfirm", flags)!.Invoke(page, null)!;

        Assert.False((bool)typeof(Users).GetField("saving", flags)!.GetValue(page)!);
        Assert.True((bool)typeof(Users).GetField("saved", flags)!.GetValue(page)!);
        var updated = (UserDetails)typeof(Users).GetField("selected", flags)!.GetValue(page)!;
        Assert.False(updated.AccountIsActive);
    }

    [Theory]
    [InlineData("fa-IR", "تغییر رمز عبور")]
    [InlineData("en-US", "Change password")]
    public async Task Change_password_page_renders_localized_inputs_with_reveal_buttons(string culture, string expectedTitle)
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

            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                CultureInfo.CurrentCulture = info;
                CultureInfo.CurrentUICulture = info;
                var root = await renderer.RenderComponentAsync<ChangePassword>(ParameterView.Empty);
                return System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
            });

            Assert.Contains(expectedTitle, html);
            Assert.Contains("action=\"/account/change-password-submit\"", html);
            Assert.Contains("name=\"currentPassword\"", html);
            Assert.Contains("name=\"newPassword\"", html);
            Assert.Contains("name=\"confirmPassword\"", html);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }
}
