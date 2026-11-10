using System.Reflection;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ForgeFlow.Web.Tests;

public class OrganizationReaderRegressionTests
{
    [Fact]
    public void Organization_and_site_routes_have_distinct_component_owners()
    {
        var org = typeof(ForgeFlow.Web.Components.Pages.Organizations).GetCustomAttributes<RouteAttribute>().Select(x => x.Template).ToArray();
        var sites = typeof(Sites).GetCustomAttributes<RouteAttribute>().Select(x => x.Template).ToArray();
        Assert.Contains("/organizations", org);
        Assert.Contains("/sites", sites);
        Assert.DoesNotContain(org, x => x.Contains("sites"));
        Assert.All(sites, x => Assert.Contains("sites", x));
        Assert.Empty(typeof(OrganizationReader).GetCustomAttributes<RouteAttribute>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_uses_page_kind_and_selected_id_and_rejects_disposed_response(bool sites)
    {
        var store = DispatchProxy.Create<IOrganizationOperationsStore, StoreProxy>();
        var proxy = (StoreProxy)(object)store;
        var reader = new OrganizationReader();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(OrganizationReader).GetProperty("IsSites")!.SetValue(reader, sites);
        typeof(OrganizationReader).GetProperty("Queries", flags)!.SetValue(reader,
            new OrganizationUseCases(store, new Actor(), new Permissions()));
        typeof(OrganizationReader).GetField("available", flags)!.SetValue(reader,
            new List<OrganizationScope> { new OrganizationScope.Organization(sites ? 12 : 73) });
        var rowType = typeof(OrganizationReader).GetNestedType("ReadRow", BindingFlags.NonPublic)!;
        var row = Activator.CreateInstance(rowType, 73L, "selected", "Selected", true)!;
        var task = (Task)typeof(OrganizationReader).GetMethod("Detail", flags)!.Invoke(reader, [row])!;
        Assert.Equal(sites ? "GetSite" : "GetOrganization", proxy.Method);
        Assert.Equal(73L, proxy.Id);
        proxy.Complete();
        await task;
        var selected = typeof(OrganizationReader).GetField("selected", flags)!.GetValue(reader)!;
        Assert.Equal(73L, selected.GetType().GetProperty("Id")!.GetValue(selected));
        Assert.Equal(sites ? "SITE" : "ORG", selected.GetType().GetProperty("Code")!.GetValue(selected));
        reader.Dispose();
        task = (Task)typeof(OrganizationReader).GetMethod("Detail", flags)!.Invoke(reader, [row])!;
        await task;
        Assert.Null(typeof(OrganizationReader).GetField("selected", flags)!.GetValue(reader));
        Assert.False((bool)typeof(OrganizationReader).GetField("detailOpen", flags)!.GetValue(reader)!);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Write_sends_expected_version_and_applies_result_without_losing_scope(bool sites, bool toggle)
    {
        var store = DispatchProxy.Create<IOrganizationOperationsStore, WriteProxy>();
        var proxy = (WriteProxy)(object)store;
        var reader = new OrganizationReader();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string field, object? value) => typeof(OrganizationReader).GetField(field, flags)!.SetValue(reader, value);
        typeof(OrganizationReader).GetProperty("IsSites")!.SetValue(reader, sites);
        typeof(OrganizationReader).GetProperty("Queries", flags)!.SetValue(reader,
            new OrganizationUseCases(store, new Actor(), new Permissions()));
        Set("available", new List<OrganizationScope> { new OrganizationScope.Organization(sites ? 12 : 73) });
        var rowType = typeof(OrganizationReader).GetNestedType("ReadRow", BindingFlags.NonPublic)!;
        var row = Activator.CreateInstance(rowType, 73L, "CODE", "Old", true)!;
        var rows = Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        ((System.Collections.IList)rows).Add(row);
        Set("rows", rows); Set("selected", row);
        Set("selectedVersion", Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Set("editName", "  New name ");
        Set("canRename", true); Set("canSetActive", true);
        await (Task)typeof(OrganizationReader).GetMethod("Write", flags)!.Invoke(reader, [!toggle])!;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, proxy.Version);
        Assert.Equal(sites ? "UpdateSite" : "UpdateOrganization", proxy.Method);
        Assert.Equal(toggle ? OrganizationMutation.Deactivate : OrganizationMutation.Rename, proxy.Mutation);
        Assert.Equal(toggle ? null : "  New name ", proxy.Name);
        var selected = typeof(OrganizationReader).GetField("selected", flags)!.GetValue(reader)!;
        Assert.Equal(toggle ? "Old" : "New name", selected.GetType().GetProperty("Name")!.GetValue(selected));
        Assert.Equal(!toggle, selected.GetType().GetProperty("Active")!.GetValue(selected));
        Assert.True((bool)typeof(OrganizationReader).GetField("saved", flags)!.GetValue(reader)!);
        Assert.Null(typeof(OrganizationReader).GetField("writeError", flags)!.GetValue(reader));
        Assert.False((bool)typeof(OrganizationReader).GetField("saving", flags)!.GetValue(reader)!);
    }

    [Theory]
    [InlineData("OrganizationSite.ConcurrencyConflict", "WriteConflict")]
    [InlineData("OrganizationSite.OutcomeUnknown", "WriteOutcomeUnknown")]
    [InlineData("OrganizationSite.AccessDenied", "ReadDenied")]
    [InlineData("Organization.NameInvalid", "WriteNameInvalid")]
    [InlineData("Site.NameInvalid", "WriteNameInvalid")]
    [InlineData("Organization.CodeInvalid", "CreateCodeInvalid")]
    [InlineData("Site.CodeInvalid", "CreateCodeInvalid")]
    [InlineData("Organization.CodeDuplicate", "CreateCodeDuplicate")]
    [InlineData("Site.CodeDuplicate", "CreateCodeDuplicate")]
    [InlineData("Organization.Inactive", "WriteParentInactive")]
    [InlineData("Organization.HasActiveSites", "WriteHasActiveSites")]
    [InlineData("Site.NotFound", "ReadNotFound")]
    [InlineData("OrganizationSite.AuditUnavailable", "WriteUnavailable")]
    [InlineData("Something.Unexpected", "WriteUnavailable")]
    public void Write_failures_map_to_registered_localized_keys(string code, string key)
        => Assert.Equal(key, typeof(OrganizationReader).GetMethod("WriteFailure", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [code]));

    [Fact]
    public async Task Edit_enables_edit_mode_and_loads_version_and_permissions()
    {
        var store = DispatchProxy.Create<IOrganizationOperationsStore, StoreProxy>();
        var proxy = (StoreProxy)(object)store;
        var reader = new OrganizationReader();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(OrganizationReader).GetProperty("IsSites")!.SetValue(reader, true);
        typeof(OrganizationReader).GetProperty("Queries", flags)!.SetValue(reader,
            new OrganizationUseCases(store, new Actor(), new Permissions()));
        typeof(OrganizationReader).GetProperty("Actors", flags)!.SetValue(reader, new Actor());
        typeof(OrganizationReader).GetProperty("Permissions", flags)!.SetValue(reader, new Permissions());
        typeof(OrganizationReader).GetField("available", flags)!.SetValue(reader,
            new List<OrganizationScope> { new OrganizationScope.Organization(12) });
        var rowType = typeof(OrganizationReader).GetNestedType("ReadRow", BindingFlags.NonPublic)!;
        var row = Activator.CreateInstance(rowType, 73L, "SITE", "Site", true)!;
        var task = (Task)typeof(OrganizationReader).GetMethod("Edit", flags)!.Invoke(reader, [row])!;
        proxy.Complete();
        await task;
        Assert.True((bool)typeof(OrganizationReader).GetField("editMode", flags)!.GetValue(reader)!);
        Assert.True((bool)typeof(OrganizationReader).GetField("detailOpen", flags)!.GetValue(reader)!);
        Assert.True((bool)typeof(OrganizationReader).GetField("canRename", flags)!.GetValue(reader)!);
        Assert.True((bool)typeof(OrganizationReader).GetField("canSetActive", flags)!.GetValue(reader)!);
        Assert.Equal("Site", typeof(OrganizationReader).GetField("editName", flags)!.GetValue(reader));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_dispatches_to_platform_or_organization_scope_and_announces_created_code(bool sites)
    {
        var store = DispatchProxy.Create<IOrganizationOperationsStore, CreateProxy>();
        var proxy = (CreateProxy)(object)store;
        var reader = new OrganizationReader();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string field, object? value) => typeof(OrganizationReader).GetField(field, flags)!.SetValue(reader, value);
        typeof(OrganizationReader).GetProperty("IsSites")!.SetValue(reader, sites);
        typeof(OrganizationReader).GetProperty("Queries", flags)!.SetValue(reader,
            new OrganizationUseCases(store, new Actor(), new Permissions()));
        typeof(OrganizationReader).GetProperty("Text", flags)!.SetValue(reader, new DummyLocalizer());
        Set("available", new List<OrganizationScope> { new OrganizationScope.Organization(12) });
        Set("canCreate", true);
        Set("createOpen", true);
        Set("createCode", "PLANT_02");
        Set("createName", "Plant 02");
        await (Task)typeof(OrganizationReader).GetMethod("SubmitCreate", flags)!.Invoke(reader, [])!;
        Assert.Equal(sites ? "CreateSite" : "CreateOrganization", proxy.Method);
        Assert.Equal("PLANT_02", proxy.Code);
        Assert.Equal("Plant 02", proxy.Name);
        Assert.Equal(sites ? 12L : 0L, proxy.OrgId);
        Assert.False((bool)typeof(OrganizationReader).GetField("createOpen", flags)!.GetValue(reader)!);
        Assert.False((bool)typeof(OrganizationReader).GetField("creating", flags)!.GetValue(reader)!);
        Assert.Null(typeof(OrganizationReader).GetField("createError", flags)!.GetValue(reader));
        Assert.Contains("PLANT_02", (string)typeof(OrganizationReader).GetField("pageNotice", flags)!.GetValue(reader)!);
    }

    [Fact]
    public async Task Unsaved_changes_block_dialog_dismissal_and_warn_before_conflict_reload()
    {
        var reader = new OrganizationReader();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string field, object? value) => typeof(OrganizationReader).GetField(field, flags)!.SetValue(reader, value);
        var rowType = typeof(OrganizationReader).GetNestedType("ReadRow", BindingFlags.NonPublic)!;
        var row = Activator.CreateInstance(rowType, 73L, "CODE", "Original", true)!;

        // Clean edit dialog allows dismiss
        Set("editMode", true); Set("selected", row); Set("editName", "Original");
        var cleanArgs = new Bit.BlazorUI.BitDialogDismissArgs(Bit.BlazorUI.BitDialogDismissReason.CloseButton);
        typeof(OrganizationReader).GetMethod("OnEditDismissing", flags)!.Invoke(reader, [cleanArgs]);
        Assert.False(cleanArgs.Cancel);
        Assert.False((bool)typeof(OrganizationReader).GetField("discardConfirm", flags)!.GetValue(reader)!);

        // Dirty edit dialog cancels dismiss and prompts confirmation
        Set("editName", "Changed");
        var dirtyArgs = new Bit.BlazorUI.BitDialogDismissArgs(Bit.BlazorUI.BitDialogDismissReason.CloseButton);
        typeof(OrganizationReader).GetMethod("OnEditDismissing", flags)!.Invoke(reader, [dirtyArgs]);
        Assert.True(dirtyArgs.Cancel);
        Assert.True((bool)typeof(OrganizationReader).GetField("discardConfirm", flags)!.GetValue(reader)!);

        // Conflict reload warns before discarding unsaved edits
        await (Task)typeof(OrganizationReader).GetMethod("RequestReload", flags)!.Invoke(reader, [])!;
        Assert.True((bool)typeof(OrganizationReader).GetField("reloadConfirm", flags)!.GetValue(reader)!);

        // Dirty create dialog cancels dismiss and prompts confirmation
        Set("createCode", "ORG2");
        var createArgs = new Bit.BlazorUI.BitDialogDismissArgs(Bit.BlazorUI.BitDialogDismissReason.CloseButton);
        typeof(OrganizationReader).GetMethod("OnCreateDismissing", flags)!.Invoke(reader, [createArgs]);
        Assert.True(createArgs.Cancel);
        Assert.True((bool)typeof(OrganizationReader).GetField("createDiscardConfirm", flags)!.GetValue(reader)!);
    }

    [Fact]
    public void Draft_scope_isolates_actor_organization_site_and_operation()
    {
        var actor1OrgEdit = OrganizationReader.BuildDraftScope(1, false, 10, null, false);
        var actor2OrgEdit = OrganizationReader.BuildDraftScope(2, false, 10, null, false);
        var actor1Site1Edit = OrganizationReader.BuildDraftScope(1, true, 10, 5, false);
        var actor1Site2Edit = OrganizationReader.BuildDraftScope(1, true, 10, 6, false);
        var actor1SiteCreate = OrganizationReader.BuildDraftScope(1, true, 10, null, true);
        var actor1OrgCreate = OrganizationReader.BuildDraftScope(1, false, null, null, true);
        var all = new[] { actor1OrgEdit, actor2OrgEdit, actor1Site1Edit, actor1Site2Edit, actor1SiteCreate, actor1OrgCreate };
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    public class CreateProxy : DispatchProxy
    {
        public string? Method, Code, Name;
        public long OrgId;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Method = method!.Name;
            if (Method == "CreateSite")
            {
                var req = (CreateSite)args![0]!;
                OrgId = req.OrganizationId; Code = req.Code; Name = req.Name;
                return Task.FromResult(OrganizationResult<SiteDetails>.Success(new(88, OrgId, Code!, Name!, true, "AQIDBAUGBwk=")));
            }
            var orgReq = (CreateOrganization)args![0]!;
            Code = orgReq.Code; Name = orgReq.Name;
            return Task.FromResult(OrganizationResult<OrganizationDetails>.Success(new(99, Code!, Name!, true, "AQIDBAUGBwk=")));
        }
    }

    private sealed class DummyLocalizer : Microsoft.Extensions.Localization.IStringLocalizer<ForgeFlow.Web.Localization.SharedResources>
    {
        public Microsoft.Extensions.Localization.LocalizedString this[string name] => new(name, name);
        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] => new(name, string.Join(":", arguments));
        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    public class WriteProxy : DispatchProxy
    {
        public string? Method, Name;
        public byte[]? Version;
        public OrganizationMutation Mutation;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Method = method!.Name;
            var sites = Method == "UpdateSite";
            var offset = sites ? 1 : 0;
            Version = (byte[])args![offset + 1]!;
            Mutation = (OrganizationMutation)args[offset + 2]!;
            Name = (string?)args[offset + 3];
            var active = Mutation == OrganizationMutation.Activate;
            var name = Mutation == OrganizationMutation.Rename ? Name!.Trim() : "Old";
            var isActive = Mutation == OrganizationMutation.Rename || active;
            return sites
                ? Task.FromResult(OrganizationResult<SiteDetails>.Success(new(73, 12, "CODE", name, isActive, "AQIDBAUGBwk=")))
                : Task.FromResult(OrganizationResult<OrganizationDetails>.Success(new(73, "CODE", name, isActive, "AQIDBAUGBwk=")));
        }
    }
    public class StoreProxy : DispatchProxy
    {
        public string? Method;
        public long Id;
        private readonly TaskCompletionSource<SiteDetails?> site = new();
        private readonly TaskCompletionSource<OrganizationDetails?> org = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Method = method!.Name;
            Id = (long)args![Method == "GetSite" ? 1 : 0]!;
            return Method == "GetSite" ? site.Task : org.Task;
        }
        public void Complete() { site.SetResult(new(Id,12,"SITE","Site",true,"")); org.SetResult(new(Id,"ORG","Organization",true,"")); }
    }
    private sealed class Actor : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult<TrustedActor?>(new(1,ActorProvenance.AuthenticatedSession,Guid.NewGuid(),Guid.NewGuid()));
    }
    private sealed class Permissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct) => ValueTask.FromResult(new PermissionDecision(true));
    }
}

