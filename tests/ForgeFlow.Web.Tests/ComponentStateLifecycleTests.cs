using System.Reflection;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Sales;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Components.Pages.BomSections;
using ForgeFlow.Web.Components.Pages.QualitySections;
using ForgeFlow.Web.Components.Pages.SalesSections;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ComponentStateLifecycleTests
{
    private static string FindSourcesDirectory()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "Sources");
        if (Directory.Exists(direct)) return direct;
        var appSources = Path.Combine(AppContext.BaseDirectory, "ApplicationSources", "Components");
        if (Directory.Exists(appSources)) return appSources;
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ForgeFlow.Web", "Components"));
        if (Directory.Exists(repoRoot)) return repoRoot;
        throw new DirectoryNotFoundException("Could not locate Components directory.");
    }

    [Fact]
    public void ImportsRazor_Includes_Virtualization_Namespace()
    {
        var compDir = FindSourcesDirectory();
        var importsPath = Path.Combine(compDir, "_Imports.razor");
        Assert.True(File.Exists(importsPath), $"_Imports.razor not found at {importsPath}");
        var content = File.ReadAllText(importsPath);
        Assert.Contains("Microsoft.AspNetCore.Components.Web.Virtualization", content);
    }

    [Theory]
    [InlineData("Pages/Audit.razor", "SelectedDetails.ParsedFields")]
    [InlineData("Pages/Traceability.razor", "report.BackwardEdges")]
    [InlineData("Pages/Traceability.razor", "report.ForwardEdges")]
    [InlineData("Pages/Traceability.razor", "root.Movements")]
    [InlineData("Pages/Reports.razor", "selectedShortageItem.Orders")]
    [InlineData("Pages/Reports.razor", "selectedVarianceDetail.Inputs")]
    [InlineData("Pages/Reports.razor", "selectedVarianceDetail.Outputs")]
    [InlineData("Pages/Boms.razor", "simExplosion.ConsumedInputs")]
    [InlineData("Pages/Boms.razor", "simExplosion.ProducedOutputs")]
    [InlineData("Pages/BomSections/BomVersionWorkbenchModal.razor", "selectedVersion.Inputs")]
    [InlineData("Pages/BomSections/BomVersionWorkbenchModal.razor", "selectedVersion.Outputs")]
    [InlineData("Pages/Inventory.razor", "selectedTxn.Entries")]
    [InlineData("Pages/SalesSections/SalesOrderDetailModal.razor", "Order.Lines")]
    [InlineData("Pages/SalesSections/SalesOrderDetailModal.razor", "Reservations")]
    [InlineData("Pages/ProductionOrders.razor", "selectedOrder.Inputs")]
    [InlineData("Pages/ProductionOrders.razor", "orderReservations")]
    [InlineData("Pages/PurchaseOrders.razor", "selectedOrder.Lines")]
    [InlineData("Pages/PurchaseOrders.razor", "selectedOrder.Receipts")]
    [InlineData("Pages/QualitySections/QualityInspectionDetailsModal.razor", "selectedInspection.Lines")]
    public void Razor_Table_Collections_Use_Virtualize_With_Tr_SpacerElement(string relativePath, string expectedCollection)
    {
        var compDir = FindSourcesDirectory();
        var fullPath = Path.Combine(compDir, relativePath);
        Assert.True(File.Exists(fullPath), $"Razor file not found: {fullPath}");
        var content = File.ReadAllText(fullPath);

        Assert.Contains("<Virtualize", content);
        Assert.Contains(expectedCollection, content);
        Assert.Contains("SpacerElement=\"tr\"", content);
    }

    [Theory]
    [InlineData("Pages/Audit.razor", "@bind-IsOpen:after=\"OnDetailsOpenChanged\"")]
    [InlineData("Pages/Errors.razor", "@bind-IsOpen:after=\"OnDetailsOpenChanged\"")]
    [InlineData("Pages/Reports.razor", "@bind-IsOpen:after=\"OnShortageModalOpenChanged\"")]
    [InlineData("Pages/Reports.razor", "@bind-IsOpen:after=\"OnVarianceModalOpenChanged\"")]
    [InlineData("Pages/Boms.razor", "@bind-IsOpen:after=\"OnBomVersionsOpenChanged\"")]
    [InlineData("Pages/Items.razor", "@bind-IsOpen:after=\"OnItemModalOpenChanged\"")]
    [InlineData("Pages/Items.razor", "@bind-IsOpen:after=\"OnClsModalOpenChanged\"")]
    [InlineData("Pages/Items.razor", "@bind-IsOpen:after=\"OnAttrModalOpenChanged\"")]
    [InlineData("Pages/Inventory.razor", "@bind-IsOpen:after=\"OnDetailsOpenChanged\"")]
    [InlineData("Pages/Inventory.razor", "@bind-IsOpen:after=\"OnPostOpenChanged\"")]
    [InlineData("Pages/Sales.razor", "@bind-IsOpen:after=\"OnOrderDetailOpenChanged\"")]
    [InlineData("Pages/Sales.razor", "@bind-IsOpen:after=\"OnReserveModalOpenChanged\"")]
    [InlineData("Pages/ProductionOrders.razor", "@bind-IsOpen:after=\"OnWorkbenchOpenChanged\"")]
    [InlineData("Pages/ProductionOrders.razor", "@bind-IsOpen:after=\"OnCreateOpenChanged\"")]
    [InlineData("Pages/PurchaseOrders.razor", "@bind-IsOpen:after=\"OnDetailModalOpenChanged\"")]
    [InlineData("Pages/PurchaseOrders.razor", "@bind-IsOpen:after=\"OnEditorModalOpenChanged\"")]
    [InlineData("Pages/Users.razor", "@bind-IsOpen:after=\"OnDetailOpenChanged\"")]
    [InlineData("Pages/Users.razor", "@bind-IsOpen:after=\"OnCreateOpenChanged\"")]
    [InlineData("Pages/Users.razor", "@bind-IsOpen:after=\"OnPermOpenChanged\"")]
    public void Razor_Modals_Have_BindIsOpenAfter_Cleanup_Hooks(string relativePath, string expectedHook)
    {
        var compDir = FindSourcesDirectory();
        var fullPath = Path.Combine(compDir, relativePath);
        Assert.True(File.Exists(fullPath), $"Razor file not found: {fullPath}");
        var content = File.ReadAllText(fullPath);
        Assert.Contains(expectedHook, content);
    }

    [Fact]
    public void Boms_Dispose_And_TabSwitch_Cleans_Simulator_And_Selected_State()
    {
        var boms = new Boms();
        var selectedBomField = typeof(Boms).GetField("selectedBom", BindingFlags.NonPublic | BindingFlags.Instance);
        var simExplosionField = typeof(Boms).GetField("simExplosion", BindingFlags.NonPublic | BindingFlags.Instance);
        var simResolutionField = typeof(Boms).GetField("simResolution", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(selectedBomField);
        Assert.NotNull(simExplosionField);
        Assert.NotNull(simResolutionField);

        // Populate dummy state
        var dummyExplosion = new BomExplosionResultDto(1, "ITEM", "Item Name", 10m, 1, "EA", "EA", 1, 1, [], []);
        simExplosionField.SetValue(boms, dummyExplosion);
        Assert.NotNull(simExplosionField.GetValue(boms));

        // Call Dispose
        boms.Dispose();

        Assert.Null(selectedBomField.GetValue(boms));
        Assert.Null(simExplosionField.GetValue(boms));
        Assert.Null(simResolutionField.GetValue(boms));
    }

    [Fact]
    public void Inventory_Dispose_Cleans_Transaction_And_Lines_State()
    {
        var inv = new Inventory();
        var selectedTxnField = typeof(Inventory).GetField("selectedTxn", BindingFlags.NonPublic | BindingFlags.Instance);
        var postLinesField = typeof(Inventory).GetField("postLines", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(selectedTxnField);
        Assert.NotNull(postLinesField);

        // Populate dummy state
        var postLines = (System.Collections.IList)postLinesField.GetValue(inv)!;
        Assert.NotNull(postLines);

        // Call Dispose
        inv.Dispose();

        Assert.Null(selectedTxnField.GetValue(inv));
        Assert.Empty(postLines);
    }

    [Fact]
    public void ProductionOrders_Dispose_Cleans_Order_And_Context_State()
    {
        var po = new ProductionOrders();
        var selectedOrderField = typeof(ProductionOrders).GetField("selectedOrder", BindingFlags.NonPublic | BindingFlags.Instance);
        var editContextPairsField = typeof(ProductionOrders).GetField("editContextPairs", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(selectedOrderField);
        Assert.NotNull(editContextPairsField);

        po.Dispose();

        Assert.Null(selectedOrderField.GetValue(po));
    }

    [Fact]
    public void PurchaseOrders_Dispose_Cleans_DraftLines_And_SelectedOrder()
    {
        var po = new PurchaseOrders();
        var selectedOrderField = typeof(PurchaseOrders).GetField("selectedOrder", BindingFlags.NonPublic | BindingFlags.Instance);
        var draftLinesField = typeof(PurchaseOrders).GetField("draftLines", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(selectedOrderField);
        Assert.NotNull(draftLinesField);

        po.Dispose();

        Assert.Null(selectedOrderField.GetValue(po));
    }

    [Fact]
    public void Users_Dispose_Cleans_Perm_And_SelectedUser_State()
    {
        var users = new Users();
        var selectedField = typeof(Users).GetField("selected", BindingFlags.NonPublic | BindingFlags.Instance);
        var permSnapshotField = typeof(Users).GetField("permSnapshot", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(selectedField);
        Assert.NotNull(permSnapshotField);

        users.Dispose();

        Assert.Null(selectedField.GetValue(users));
        Assert.Null(permSnapshotField.GetValue(users));
    }

    [Fact]
    public void Quality_Dispose_Cleans_SpecLines_And_Parameters()
    {
        var q = new Quality();
        var editingSpecField = typeof(Quality).GetField("editingSpec", BindingFlags.NonPublic | BindingFlags.Instance);
        var specLinesField = typeof(Quality).GetField("specLines", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(editingSpecField);
        Assert.NotNull(specLinesField);

        q.Dispose();

        Assert.Null(editingSpecField.GetValue(q));
    }

    [Fact]
    public void Modals_Implement_IDisposable_For_Circuit_State_Hygiene()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(BomVersionWorkbenchModal)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(PostDispatchModal)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(CreateEditSalesOrderModal)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(QualityInspectionDetailsModal)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(Traceability)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(Reports)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(Audit)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(Errors)));
    }
}
