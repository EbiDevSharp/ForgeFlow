namespace ForgeFlow.Infrastructure.Organizations;

internal static class OrganizationModel
{
    internal const string Schema = "organization";
    internal const string CodeCollation = "Latin1_General_100_BIN2";

    internal const string OrganizationChangeEventSql =
        "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ("
        + "'Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated',"
        + "'Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated',"
        + "'UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated',"
        + "'Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated',"
        + "'Item.Created','Item.Updated','Item.Activated','Item.Deactivated',"
        + "'ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated',"
        + "'ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated',"
        + "'AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated',"
        + "'ItemAttributeValue.Updated',"
        + "'ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated',"
        + "'Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated',"
        + "'BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved',"
        + "'ItemLot.Updated','QualityParameter.Created','QualityParameter.Updated','QualityParameter.Activated','QualityParameter.Deactivated',"
        + "'QualitySpec.Created','QualitySpec.Updated','QualitySpec.Activated','QualitySpec.Deactivated',"
        + "'Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated',"
        + "'Customer.Created','Customer.Updated','Customer.Activated','Customer.Deactivated',"
        + "'BusinessPartner.Created','BusinessPartner.Updated','BusinessPartner.Activated','BusinessPartner.Deactivated',"
        + "'DocumentSequence.Created','DocumentSequence.Updated')) "
        + "OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','InventoryReservation.Created','InventoryReservation.Consumed','InventoryReservation.Released','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled','WorkCenter.Created','WorkCenter.Updated','WorkCenter.Activated','WorkCenter.Deactivated','Routing.Created','Routing.Updated','Routing.OperationsSaved','Routing.Activated','Routing.Obsoleted','ProductionOrderOperation.Started','ProductionOrderOperation.Paused','ProductionOrderOperation.Completed','ProductionOrderOperation.Skipped','SalesOrder.Created','SalesOrder.Updated','SalesOrder.Approved','SalesOrder.Dispatched','SalesOrder.Closed','SalesOrder.Cancelled','DeliveryOrder.Posted','DeliveryOrder.Reversed')))";
}
