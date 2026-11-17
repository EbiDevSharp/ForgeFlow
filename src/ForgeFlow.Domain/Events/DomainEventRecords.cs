using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Events;

public static class EventJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>Raised when a Production Order transitions from Draft to Released.</summary>
public sealed record ProductionOrderReleasedEvent(
    long OrganizationId,
    long? SiteId,
    long ProductionOrderId,
    string OrderNumber,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "ProductionOrder.Released";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new { OrderNumber, ProductionOrderId });
}

/// <summary>Raised when a Production Order is completed with recorded actuals.</summary>
public sealed record ProductionOrderCompletedEvent(
    long OrganizationId,
    long? SiteId,
    long ProductionOrderId,
    string OrderNumber,
    decimal ProducedQuantity,
    decimal ScrapQuantity,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "ProductionOrder.Completed";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new
    {
        OrderNumber,
        ProductionOrderId,
        ActualProducedQuantity = ProducedQuantity,
        ActualScrapQuantity = ScrapQuantity
    });
}

/// <summary>Raised when a Production Order starts execution.</summary>
public sealed record ProductionOrderStartedEvent(
    long OrganizationId,
    long? SiteId,
    long ProductionOrderId,
    string OrderNumber,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "ProductionOrder.Started";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new { OrderNumber, ProductionOrderId });
}

/// <summary>Raised when a Production Order is cancelled.</summary>
public sealed record ProductionOrderCancelledEvent(
    long OrganizationId,
    long? SiteId,
    long ProductionOrderId,
    string OrderNumber,
    string Reason,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "ProductionOrder.Cancelled";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new { OrderNumber, ProductionOrderId, Reason });
}

/// <summary>Raised when a Sales Order is approved.</summary>
public sealed record SalesOrderApprovedEvent(
    long OrganizationId,
    long? SiteId,
    long SalesOrderId,
    string OrderNumber,
    decimal TotalAmount,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "SalesOrder.Approved";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new { OrderNumber, SalesOrderId, TotalAmount });
}

/// <summary>Raised when a Sales Order is partially or fully dispatched via Delivery Order.</summary>
public sealed record SalesOrderDispatchedEvent(
    long OrganizationId,
    long? SiteId,
    long SalesOrderId,
    string OrderNumber,
    long DeliveryOrderId,
    string DeliveryOrderNumber,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "SalesOrder.Dispatched";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new
    {
        OrderNumber,
        SalesOrderId,
        DeliveryOrderId,
        DeliveryOrderNumber
    });
}

/// <summary>Raised when stock is adjusted, issued, or received.</summary>
public sealed record StockAdjustedEvent(
    long OrganizationId,
    long? SiteId,
    long WarehouseId,
    long ItemId,
    decimal DeltaQuantity,
    string DocumentType,
    string DocumentNumber,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "InventoryTransaction.Posted";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new
    {
        WarehouseId,
        ItemId,
        DeltaQuantity,
        DocumentType,
        DocumentNumber
    });
}

/// <summary>Raised when inventory is hard-reserved for an order.</summary>
public sealed record StockReservedEvent(
    long OrganizationId,
    long? SiteId,
    long ItemId,
    decimal ReservedQuantity,
    string ReferenceType,
    long ReferenceId,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "InventoryReservation.Created";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new
    {
        ItemId,
        ReservedQuantity,
        ReferenceType,
        ReferenceId
    });
}

/// <summary>Raised when a Quality Inspection disposition is recorded.</summary>
public sealed record QualityInspectionRecordedEvent(
    long OrganizationId,
    long? SiteId,
    long InspectionId,
    string InspectionNumber,
    string Disposition,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "QualityInspection.Disposed";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new
    {
        InspectionId,
        InspectionNumber,
        Disposition
    });
}

/// <summary>Raised when lot metadata or Certificate of Analysis is issued/updated.</summary>
public sealed record LotCoaIssuedEvent(
    long OrganizationId,
    long? SiteId,
    long ItemId,
    string LotNumber,
    string CoaNumber,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "ItemLot.Updated";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new
    {
        ItemId,
        LotNumber,
        CoaNumber
    });
}

/// <summary>Raised when a BOM version is approved.</summary>
public sealed record BomApprovedEvent(
    long OrganizationId,
    long BomId,
    long BomVersionId,
    int VersionNumber,
    DateTime OccurredOnUtc) : IAuditableDomainEvent
{
    public long? SiteId => null;
    public DateTime OccurredAtUtc => OccurredOnUtc;
    public string EventCode => "BomVersion.StatusChanged";
    public string BuildAuditJson() => EventJsonSerializer.Serialize(new
    {
        BomId,
        BomVersionId,
        VersionNumber,
        Status = "Approved"
    });
}

/// <summary>Raised when permissions assigned to a role are updated.</summary>
public sealed record RolePermissionsUpdatedEvent(
    long OrganizationId,
    long RoleId,
    string RoleName,
    IReadOnlyList<string> Permissions,
    DateTime OccurredOnUtc) : IDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
}

/// <summary>Raised when user membership or role access is modified.</summary>
public sealed record UserAccessModifiedEvent(
    long? OrganizationId,
    long UserId,
    string Action,
    DateTime OccurredOnUtc) : IDomainEvent
{
    public DateTime OccurredAtUtc => OccurredOnUtc;
}
