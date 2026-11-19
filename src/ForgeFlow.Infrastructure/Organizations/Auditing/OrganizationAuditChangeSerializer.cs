using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ForgeFlow.Infrastructure.Organizations;

internal static class OrganizationAuditChangeSerializer
{
    private static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal sealed record PendingChange(EntityEntry Entry, string EventCode, string Json);

    internal static long ResolveEntityOrganizationId(object entity) => entity switch
    {
        Organization org => org.Id,
        Site site => site.OrganizationId,
        Unit unit => unit.OrganizationId,
        UnitConversion conv => conv.OrganizationId,
        Currency cur => cur.OrganizationId,
        ClassificationSet cs => cs.OrganizationId,
        Classification c => c.OrganizationId,
        Item i => i.OrganizationId,
        ItemClassification ic => ic.OrganizationId,
        ItemUnitConversion icv => icv.OrganizationId,
        AttributeDefinition ad => ad.OrganizationId,
        ItemAttributeValue av => av.OrganizationId,
        ContextDimension cd => cd.OrganizationId,
        Bom b => b.OrganizationId,
        BomVersion bv => bv.OrganizationId,
        BomInput bi => bi.OrganizationId,
        BomOutput bo => bo.OrganizationId,
        BomApplicability ba => ba.OrganizationId,
        BomApplicabilityCondition bc => bc.OrganizationId,
        ProductionOrder po => po.OrganizationId,
        ProductionOrderInput poi => poi.OrganizationId,
        ProductionOrderOutput poo => poo.OrganizationId,
        ProductionOrderContext poc => poc.OrganizationId,
        Warehouse wh => wh.OrganizationId,
        InventoryTransaction txn => txn.OrganizationId,
        InventoryLedgerEntry ile => ile.OrganizationId,
        InventoryBalance bal => bal.OrganizationId,
        StockReservation sr => sr.OrganizationId,
        ItemLot lot => lot.OrganizationId,
        QualityInspection qi => qi.OrganizationId,
        QualityInspectionLine qil => qil.OrganizationId,
        QualityParameter qp => qp.OrganizationId,
        QualitySpec qs => qs.OrganizationId,
        QualitySpecLine qsl => qsl.OrganizationId,
        Supplier sup => sup.OrganizationId,
        PurchaseOrder pur => pur.OrganizationId,
        PurchaseOrderLine pul => pul.OrganizationId,
        WorkCenter wc => wc.OrganizationId,
        Routing r => r.OrganizationId,
        RoutingOperation ro => ro.OrganizationId,
        ProductionOrderOperation poo => poo.OrganizationId,
        Customer cust => cust.OrganizationId,
        SalesOrder so => so.OrganizationId,
        SalesOrderLine sol => sol.OrganizationId,
        DeliveryOrder dlo => dlo.OrganizationId,
        DeliveryOrderLine dlol => dlol.OrganizationId,
        DocumentSequence ds => ds.OrganizationId,
        BusinessPartner bp => bp.OrganizationId,
        BusinessPartnerRoleAssignment bpra => bpra.OrganizationId,
        _ => throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied)
    };

    internal static long? ResolveEntitySiteId(OrganizationDbContext db, object entity) => entity switch
    {
        Site site => site.Id,
        ProductionOrder po => po.SiteId,
        ProductionOrderInput poi => db.ChangeTracker.Entries<ProductionOrder>().FirstOrDefault(p => p.Entity.Id == poi.ProductionOrderId)?.Entity.SiteId,
        ProductionOrderOutput poo => db.ChangeTracker.Entries<ProductionOrder>().FirstOrDefault(p => p.Entity.Id == poo.ProductionOrderId)?.Entity.SiteId,
        ProductionOrderContext poc => db.ChangeTracker.Entries<ProductionOrder>().FirstOrDefault(p => p.Entity.Id == poc.ProductionOrderId)?.Entity.SiteId,
        Warehouse wh => wh.SiteId,
        InventoryTransaction txn => txn.SiteId,
        InventoryLedgerEntry ile => ile.SiteId,
        InventoryBalance bal => bal.SiteId,
        StockReservation sr => sr.SiteId,
        QualityInspection qi => qi.SiteId,
        QualityInspectionLine qil => db.ChangeTracker.Entries<QualityInspection>().FirstOrDefault(q => q.Entity.Id == qil.QualityInspectionId)?.Entity.SiteId,
        PurchaseOrder pur => pur.SiteId,
        PurchaseOrderLine pul => db.ChangeTracker.Entries<PurchaseOrder>().FirstOrDefault(p => p.Entity.Id == pul.PurchaseOrderId)?.Entity.SiteId
            ?? db.PurchaseOrders.AsNoTracking().Where(p => p.OrganizationId == pul.OrganizationId && p.Id == pul.PurchaseOrderId).Select(p => (long?)p.SiteId).FirstOrDefault(),
        WorkCenter wc => wc.SiteId,
        Routing r => r.SiteId,
        RoutingOperation ro => ro.SiteId,
        ProductionOrderOperation poo => poo.SiteId,
        SalesOrder so => so.SiteId,
        SalesOrderLine sol => db.ChangeTracker.Entries<SalesOrder>().FirstOrDefault(s => s.Entity.Id == sol.SalesOrderId)?.Entity.SiteId
            ?? db.SalesOrders.AsNoTracking().Where(s => s.OrganizationId == sol.OrganizationId && s.Id == sol.SalesOrderId).Select(s => (long?)s.SiteId).FirstOrDefault(),
        DeliveryOrder dlo => dlo.SiteId,
        DeliveryOrderLine dlol => db.ChangeTracker.Entries<DeliveryOrder>().FirstOrDefault(d => d.Entity.Id == dlol.DeliveryOrderId)?.Entity.SiteId
            ?? db.DeliveryOrders.AsNoTracking().Where(d => d.OrganizationId == dlol.OrganizationId && d.Id == dlol.DeliveryOrderId).Select(d => (long?)d.SiteId).FirstOrDefault(),
        _ => null
    };

    internal static IEnumerable<PendingChange> CaptureAllChanges(EntityEntry[] entries, HashSet<string>? dispatchedCodes = null)
    {
        var seenBomLinesVersions = new HashSet<long>();
        var seenApplicabilityIds = new HashSet<long>();
        var ordersWithHeaderEvent = entries
            .Where(e => e.Entity is ProductionOrder)
            .Select(e => ((ProductionOrder)e.Entity).Id)
            .ToHashSet();
        var seenRefreshedOrders = new HashSet<long>();
        var headersWithQualityEvent = entries
            .Where(e => e.Entity is QualityInspection)
            .Select(e => ((QualityInspection)e.Entity).Id)
            .ToHashSet();
        var seenQualityLinesInspections = new HashSet<long>();
        var headersWithSpecEvent = entries
            .Where(e => e.Entity is QualitySpec)
            .Select(e => ((QualitySpec)e.Entity).Id)
            .ToHashSet();
        var seenSpecLinesSpecs = new HashSet<long>();
        var headersWithPurchaseEvent = entries
            .Where(e => e.Entity is PurchaseOrder)
            .Select(e => ((PurchaseOrder)e.Entity).Id)
            .ToHashSet();
        var seenPurchaseLinesOrders = new HashSet<long>();
        var headersWithSalesEvent = entries
            .Where(e => e.Entity is SalesOrder)
            .Select(e => ((SalesOrder)e.Entity).Id)
            .ToHashSet();
        var seenSalesLinesOrders = new HashSet<long>();
        var headersWithDeliveryEvent = entries
            .Where(e => e.Entity is DeliveryOrder)
            .Select(e => ((DeliveryOrder)e.Entity).Id)
            .ToHashSet();
        var seenDeliveryLinesOrders = new HashSet<long>();
        var seenRoutingOperationRoutings = new HashSet<long>();

        foreach (var entry in entries)
        {
            if (entry.Entity is DeliveryOrderLine dLine)
            {
                if (!headersWithDeliveryEvent.Contains(dLine.DeliveryOrderId) && seenDeliveryLinesOrders.Add(dLine.DeliveryOrderId))
                {
                    yield return new PendingChange(entry, "DeliveryOrder.Posted",
                        JsonSerializer.Serialize(new { DeliveryOrderId = dLine.DeliveryOrderId }, AuditJson));
                }
                continue;
            }
            if (entry.Entity is SalesOrderLine soLine)
            {
                if (!headersWithSalesEvent.Contains(soLine.SalesOrderId) && seenSalesLinesOrders.Add(soLine.SalesOrderId))
                {
                    var code = entry.State == EntityState.Modified && entry.Property("DispatchedBaseQuantity").IsModified
                        ? "SalesOrder.Dispatched"
                        : "SalesOrder.Updated";
                    yield return new PendingChange(entry, code,
                        JsonSerializer.Serialize(new { SalesOrderId = soLine.SalesOrderId }, AuditJson));
                }
                continue;
            }
            if (entry.Entity is PurchaseOrderLine pLine)
            {
                if (!headersWithPurchaseEvent.Contains(pLine.PurchaseOrderId) && seenPurchaseLinesOrders.Add(pLine.PurchaseOrderId))
                {
                    var code = entry.State == EntityState.Modified && entry.Property("ReceivedBaseQuantity").IsModified
                        ? "PurchaseOrder.Received"
                        : "PurchaseOrder.Updated";
                    yield return new PendingChange(entry, code,
                        JsonSerializer.Serialize(new { PurchaseOrderId = pLine.PurchaseOrderId }, AuditJson));
                }
                continue;
            }
            if (entry.Entity is QualityInspectionLine qLine)
            {
                if (!headersWithQualityEvent.Contains(qLine.QualityInspectionId) && seenQualityLinesInspections.Add(qLine.QualityInspectionId))
                {
                    yield return new PendingChange(entry, "QualityInspection.Updated",
                        JsonSerializer.Serialize(new { QualityInspectionId = qLine.QualityInspectionId }, AuditJson));
                }
                continue;
            }
            if (entry.Entity is QualitySpecLine sLine)
            {
                if (!headersWithSpecEvent.Contains(sLine.QualitySpecId) && seenSpecLinesSpecs.Add(sLine.QualitySpecId))
                {
                    yield return new PendingChange(entry, "QualitySpec.Updated",
                        JsonSerializer.Serialize(new { QualitySpecId = sLine.QualitySpecId }, AuditJson));
                }
                continue;
            }
            if (entry.Entity is BomInput or BomOutput)
            {
                var verId = entry.Entity is BomInput bi ? bi.BomVersionId : ((BomOutput)entry.Entity).BomVersionId;
                if (seenBomLinesVersions.Add(verId))
                {
                    yield return new PendingChange(entry, "BomVersion.LinesSaved",
                        JsonSerializer.Serialize(new { BomVersionId = verId }, AuditJson));
                }
                continue;
            }

            if (entry.Entity is BomApplicabilityCondition cond)
            {
                if (seenApplicabilityIds.Add(cond.BomApplicabilityId))
                {
                    yield return new PendingChange(entry, "BomVersion.ApplicabilitySaved",
                        JsonSerializer.Serialize(new { cond.BomApplicabilityId }, AuditJson));
                }
                continue;
            }

            if (entry.Entity is ProductionOrderInput or ProductionOrderOutput or ProductionOrderContext)
            {
                var poId = entry.Entity switch
                {
                    ProductionOrderInput pi => pi.ProductionOrderId,
                    ProductionOrderOutput po => po.ProductionOrderId,
                    ProductionOrderContext pc => pc.ProductionOrderId,
                    _ => 0L
                };
                if (poId > 0 && !ordersWithHeaderEvent.Contains(poId) && seenRefreshedOrders.Add(poId))
                {
                    yield return new PendingChange(entry, "ProductionOrder.Refreshed",
                        JsonSerializer.Serialize(new { ProductionOrderId = poId }, AuditJson));
                }
                continue;
            }

            if (entry.Entity is RoutingOperation rOp)
            {
                if (seenRoutingOperationRoutings.Add(rOp.RoutingId))
                {
                    yield return new PendingChange(entry, "Routing.OperationsSaved",
                        JsonSerializer.Serialize(new { RoutingId = rOp.RoutingId }, AuditJson));
                }
                continue;
            }

            if (entry.Entity is ProductionOrderOperation poOp && entry.State != EntityState.Modified)
            {
                if (!ordersWithHeaderEvent.Contains(poOp.ProductionOrderId) && seenRefreshedOrders.Add(poOp.ProductionOrderId))
                {
                    yield return new PendingChange(entry, "ProductionOrder.Refreshed",
                        JsonSerializer.Serialize(new { ProductionOrderId = poOp.ProductionOrderId }, AuditJson));
                }
                continue;
            }

            if (entry.Entity is InventoryLedgerEntry or InventoryBalance)
                continue;

            foreach (var change in CaptureChanges(entry, dispatchedCodes))
                yield return change;
        }
    }

    private static IEnumerable<PendingChange> CaptureChanges(EntityEntry entry, HashSet<string>? dispatchedCodes = null)
    {
        if (entry.Entity is WorkCenter wc)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "WorkCenter.Created",
                    JsonSerializer.Serialize(new { wc.SiteId, wc.Code, wc.Name, Type = wc.WorkCenterType.ToString(), wc.CapacityUnitsPerHour, wc.DailyAvailableHours }, AuditJson));
                yield break;
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, wc.IsActive ? "WorkCenter.Activated" : "WorkCenter.Deactivated",
                    JsonSerializer.Serialize(new { wc.Code, wc.IsActive }, AuditJson));
            }
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("IsActive" or "UpdatedAtUtc" or "UpdatedByActorId")))
            {
                yield return new PendingChange(entry, "WorkCenter.Updated",
                    JsonSerializer.Serialize(new { wc.Code, wc.Name, Type = wc.WorkCenterType.ToString(), wc.CapacityUnitsPerHour, wc.DailyAvailableHours }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is Routing rt)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Routing.Created",
                    JsonSerializer.Serialize(new { rt.SiteId, rt.Code, rt.Name, rt.ItemId, rt.VersionNumber, rt.IsDefault }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified)
            {
                var ev = rt.Status switch
                {
                    RoutingStatus.Active => "Routing.Activated",
                    RoutingStatus.Obsolete => "Routing.Obsoleted",
                    _ => "Routing.Updated"
                };
                yield return new PendingChange(entry, ev,
                    JsonSerializer.Serialize(new { rt.Code, rt.VersionNumber, Status = rt.Status.ToString(), rt.IsDefault }, AuditJson));
                yield break;
            }
            yield return new PendingChange(entry, "Routing.Updated",
                JsonSerializer.Serialize(new { rt.Code, rt.VersionNumber, rt.Name, rt.BomVersionId, rt.IsDefault }, AuditJson));
            yield break;
        }

        if (entry.Entity is ProductionOrderOperation poOp && entry.State == EntityState.Modified)
        {
            var ev = poOp.Status switch
            {
                ProductionOrderOperationStatus.InProgress => "ProductionOrderOperation.Started",
                ProductionOrderOperationStatus.Paused => "ProductionOrderOperation.Paused",
                ProductionOrderOperationStatus.Completed => "ProductionOrderOperation.Completed",
                ProductionOrderOperationStatus.Skipped => "ProductionOrderOperation.Skipped",
                _ => null
            };
            if (ev is not null)
            {
                yield return new PendingChange(entry, ev,
                    JsonSerializer.Serialize(new
                    {
                        poOp.ProductionOrderId,
                        poOp.Sequence,
                        poOp.OperationCode,
                        poOp.WorkCenterId,
                        Status = poOp.Status.ToString(),
                        poOp.CompletedQuantity,
                        poOp.ScrapQuantity
                    }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is StockReservation sr)
        {
            if (entry.State == EntityState.Added)
            {
                if (dispatchedCodes?.Contains("InventoryReservation.Created") == true)
                    yield break;
                yield return new PendingChange(entry, "InventoryReservation.Created",
                    JsonSerializer.Serialize(new
                    {
                        sr.ProductionOrderId,
                        sr.WarehouseId,
                        sr.ItemId,
                        sr.LotCode,
                        sr.ReservedBaseQuantity
                    }, AuditJson));
                yield break;
            }
            if (entry.State == EntityState.Modified && entry.Property("Status").IsModified)
            {
                var ev = sr.Status == ReservationStatus.Released
                    ? "InventoryReservation.Released"
                    : "InventoryReservation.Consumed";
                yield return new PendingChange(entry, ev,
                    JsonSerializer.Serialize(new
                    {
                        sr.ProductionOrderId,
                        sr.WarehouseId,
                        sr.ItemId,
                        sr.LotCode,
                        sr.ReservedBaseQuantity,
                        sr.ConsumedBaseQuantity,
                        Status = sr.Status.ToString()
                    }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is Supplier sup)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Supplier.Created",
                    JsonSerializer.Serialize(new { sup.Code, sup.Name, sup.IsActive }, AuditJson));
                yield break;
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, sup.IsActive ? "Supplier.Activated" : "Supplier.Deactivated",
                    JsonSerializer.Serialize(new { sup.Code, sup.IsActive }, AuditJson));
            }
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("IsActive" or "UpdatedAtUtc" or "UpdatedByActorId")))
            {
                yield return new PendingChange(entry, "Supplier.Updated",
                    JsonSerializer.Serialize(new { sup.Code, sup.Name, sup.ContactName, sup.Phone, sup.Email }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is PurchaseOrder pur)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "PurchaseOrder.Created",
                    JsonSerializer.Serialize(new { pur.OrderNumber, pur.SiteId, pur.SupplierId, pur.OrderDate, pur.CurrencyCode }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified)
            {
                var evCode = pur.Status switch
                {
                    PurchaseOrderStatus.Approved => "PurchaseOrder.Approved",
                    PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received => "PurchaseOrder.Received",
                    PurchaseOrderStatus.Closed => "PurchaseOrder.Closed",
                    PurchaseOrderStatus.Cancelled => "PurchaseOrder.Cancelled",
                    _ => "PurchaseOrder.Updated"
                };
                yield return new PendingChange(entry, evCode,
                    JsonSerializer.Serialize(new { pur.OrderNumber, Status = pur.Status.ToString(), pur.SupplierId }, AuditJson));
                yield break;
            }
            yield return new PendingChange(entry, "PurchaseOrder.Updated",
                JsonSerializer.Serialize(new { pur.OrderNumber, pur.SupplierId, pur.OrderDate, pur.ExpectedDeliveryDate }, AuditJson));
            yield break;
        }

        if (entry.Entity is Customer cust)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Customer.Created",
                    JsonSerializer.Serialize(new { cust.Code, cust.Name, cust.IsActive }, AuditJson));
                yield break;
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, cust.IsActive ? "Customer.Activated" : "Customer.Deactivated",
                    JsonSerializer.Serialize(new { cust.Code, cust.IsActive }, AuditJson));
            }
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("IsActive" or "UpdatedAtUtc" or "UpdatedByActorId")))
            {
                yield return new PendingChange(entry, "Customer.Updated",
                    JsonSerializer.Serialize(new { cust.Code, cust.Name, cust.ContactName, cust.Phone, cust.Email }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is BusinessPartner bp)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "BusinessPartner.Created",
                    JsonSerializer.Serialize(new { bp.Code, bp.Name, PersonTypeName = bp.PersonType.ToString(), bp.IsActive }, AuditJson));
                yield break;
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, bp.IsActive ? "BusinessPartner.Activated" : "BusinessPartner.Deactivated",
                    JsonSerializer.Serialize(new { bp.Code, bp.IsActive }, AuditJson));
            }
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("IsActive" or "UpdatedAtUtc" or "UpdatedByActorId")))
            {
                yield return new PendingChange(entry, "BusinessPartner.Updated",
                    JsonSerializer.Serialize(new { bp.Code, bp.Name, bp.Mobile, bp.Phone, bp.Email }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is SalesOrder so)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "SalesOrder.Created",
                    JsonSerializer.Serialize(new { so.OrderNumber, so.SiteId, so.CustomerId, so.OrderDate, so.CurrencyCode }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified)
            {
                var evCode = so.Status switch
                {
                    SalesOrderStatus.Approved => "SalesOrder.Approved",
                    SalesOrderStatus.PartiallyDispatched or SalesOrderStatus.Dispatched => "SalesOrder.Dispatched",
                    SalesOrderStatus.Closed => "SalesOrder.Closed",
                    SalesOrderStatus.Cancelled => "SalesOrder.Cancelled",
                    _ => "SalesOrder.Updated"
                };
                if (dispatchedCodes?.Contains(evCode) == true)
                    yield break;
                yield return new PendingChange(entry, evCode,
                    JsonSerializer.Serialize(new { so.OrderNumber, Status = so.Status.ToString(), so.CustomerId }, AuditJson));
                yield break;
            }
            yield return new PendingChange(entry, "SalesOrder.Updated",
                JsonSerializer.Serialize(new { so.OrderNumber, so.CustomerId, so.OrderDate, so.RequestedDeliveryDate }, AuditJson));
            yield break;
        }

        if (entry.Entity is DeliveryOrder dlo)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "DeliveryOrder.Posted",
                    JsonSerializer.Serialize(new { dlo.DispatchNumber, dlo.SiteId, dlo.SalesOrderId, dlo.WarehouseId, dlo.DispatchDate }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified && dlo.Status == DeliveryOrderStatus.Reversed)
            {
                yield return new PendingChange(entry, "DeliveryOrder.Reversed",
                    JsonSerializer.Serialize(new { dlo.DispatchNumber, dlo.SalesOrderId, dlo.ReversalReason }, AuditJson));
                yield break;
            }
        }

        if (entry.Entity is DocumentSequence seq)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "DocumentSequence.Created",
                    JsonSerializer.Serialize(new { DocumentType = seq.DocumentType.ToString(), seq.Prefix, seq.NextNumber }, AuditJson));
                yield break;
            }
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is "Prefix" or "Separator" or "YearMode" or "PaddingDigits" or "AllowManualOverride" or "IsActive"))
            {
                yield return new PendingChange(entry, "DocumentSequence.Updated",
                    JsonSerializer.Serialize(new { DocumentType = seq.DocumentType.ToString(), seq.Prefix, seq.Separator, YearMode = seq.YearMode.ToString(), seq.PaddingDigits, seq.NextNumber, seq.AllowManualOverride }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ItemLot lot)
        {
            yield return new PendingChange(entry, "ItemLot.Updated",
                JsonSerializer.Serialize(new { lot.ItemId, lot.LotCode, lot.ManufactureDate, lot.ExpirationDate }, AuditJson));
            yield break;
        }

        if (entry.Entity is QualityInspection qi)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "QualityInspection.Created",
                    JsonSerializer.Serialize(new { qi.InspectionNumber, qi.SiteId, qi.WarehouseId, qi.ItemId, qi.LotCode, qi.InspectedBaseQuantity, SourceType = qi.SourceType.ToString() }, AuditJson));
                yield break;
            }
            if (entry.Property("AcceptedBaseQuantity").IsModified || entry.Property("RejectedBaseQuantity").IsModified)
            {
                if (dispatchedCodes?.Contains("QualityInspection.Disposed") == true)
                    yield break;
                yield return new PendingChange(entry, "QualityInspection.Disposed",
                    JsonSerializer.Serialize(new { qi.InspectionNumber, qi.AcceptedBaseQuantity, qi.RejectedBaseQuantity, qi.RemainingBlockedBaseQuantity, Status = qi.Status.ToString(), qi.DefectReasonCode }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified && qi.Status == QualityInspectionStatus.Cancelled)
            {
                yield return new PendingChange(entry, "QualityInspection.Cancelled",
                    JsonSerializer.Serialize(new { qi.InspectionNumber, qi.Notes }, AuditJson));
                yield break;
            }
            yield return new PendingChange(entry, "QualityInspection.Updated",
                JsonSerializer.Serialize(new { qi.InspectionNumber, qi.DefectReasonCode, Status = qi.Status.ToString() }, AuditJson));
            yield break;
        }

        if (entry.Entity is QualityParameter qp)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "QualityParameter.Created",
                    JsonSerializer.Serialize(new { qp.Code, qp.Name, qp.Symbol, qp.UnitSymbol, DataType = qp.DataType.ToString() }, AuditJson));
                yield break;
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, qp.IsActive ? "QualityParameter.Activated" : "QualityParameter.Deactivated",
                    JsonSerializer.Serialize(new { qp.Code, qp.Name, qp.IsActive }, AuditJson));
                yield break;
            }
            yield return new PendingChange(entry, "QualityParameter.Updated",
                JsonSerializer.Serialize(new { qp.Code, qp.Name, qp.Symbol, qp.UnitSymbol, qp.DecimalPlaces, qp.DefaultTestMethod }, AuditJson));
            yield break;
        }

        if (entry.Entity is QualitySpec qs)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "QualitySpec.Created",
                    JsonSerializer.Serialize(new { qs.Code, qs.Name, TargetKind = qs.TargetKind.ToString(), qs.ItemId, qs.ClassificationId, Checkpoint = qs.Checkpoint.ToString() }, AuditJson));
                yield break;
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, qs.IsActive ? "QualitySpec.Activated" : "QualitySpec.Deactivated",
                    JsonSerializer.Serialize(new { qs.Code, qs.Name, qs.IsActive }, AuditJson));
                yield break;
            }
            yield return new PendingChange(entry, "QualitySpec.Updated",
                JsonSerializer.Serialize(new { qs.Code, qs.Name, Checkpoint = qs.Checkpoint.ToString(), qs.Version }, AuditJson));
            yield break;
        }

        if (entry.Entity is Warehouse wh)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Warehouse.Created",
                    JsonSerializer.Serialize(new { wh.Code, wh.Name, wh.SiteId, wh.IsDefaultForSite, wh.IsActive }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("IsDefaultForSite").IsModified)
            {
                yield return new PendingChange(entry, "Warehouse.Updated",
                    JsonSerializer.Serialize(new { wh.Code, wh.Name, wh.IsDefaultForSite }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, wh.IsActive ? "Warehouse.Activated" : "Warehouse.Deactivated",
                    JsonSerializer.Serialize(new { wh.Code, wh.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is InventoryTransaction txn)
        {
            if (entry.State == EntityState.Added)
            {
                var ev = txn.TransactionType == InventoryTransactionType.Reversal
                    ? "InventoryTransaction.Reversed"
                    : "InventoryTransaction.Posted";
                yield return new PendingChange(entry, ev,
                    JsonSerializer.Serialize(new
                    {
                        txn.DocumentNumber,
                        TransactionType = txn.TransactionType.ToString(),
                        txn.SiteId,
                        txn.WarehouseId,
                        txn.DestinationWarehouseId,
                        txn.ProductionOrderId,
                        txn.ReversedTransactionId
                    }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ProductionOrder poOrder)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "ProductionOrder.Created",
                    JsonSerializer.Serialize(new { poOrder.OrderNumber, poOrder.SiteId, poOrder.ItemId, poOrder.RequestedQuantity, poOrder.BomVersionId }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified)
            {
                var evCode = poOrder.Status switch
                {
                    ProductionOrderStatus.Released => "ProductionOrder.Released",
                    ProductionOrderStatus.InProgress => "ProductionOrder.Started",
                    ProductionOrderStatus.Completed => "ProductionOrder.Completed",
                    ProductionOrderStatus.Cancelled => "ProductionOrder.Cancelled",
                    _ => "ProductionOrder.Updated"
                };
                if (dispatchedCodes?.Contains(evCode) == true)
                    yield break;
                yield return new PendingChange(entry, evCode,
                    JsonSerializer.Serialize(new
                    {
                        poOrder.OrderNumber,
                        Status = poOrder.Status.ToString(),
                        poOrder.BomVersionId,
                        poOrder.ActualProducedQuantity,
                        poOrder.ActualScrapQuantity
                    }, AuditJson));
                yield break;
            }
            yield return new PendingChange(entry, "ProductionOrder.Updated",
                JsonSerializer.Serialize(new
                {
                    poOrder.OrderNumber,
                    poOrder.RequestedQuantity,
                    poOrder.BomVersionId,
                    poOrder.ActualProducedQuantity,
                    poOrder.ActualScrapQuantity
                }, AuditJson));
            yield break;
        }

        if (entry.Entity is ContextDimension cd)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "ContextDimension.Created",
                    JsonSerializer.Serialize(new { cd.Code, cd.Name, cd.IsActive }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified)
            {
                yield return new PendingChange(entry, "ContextDimension.Updated",
                    JsonSerializer.Serialize(new { cd.Code, cd.Name }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "ContextDimension.Activated" : "ContextDimension.Deactivated",
                    JsonSerializer.Serialize(new { cd.Code, cd.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is Bom bom)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Bom.Created",
                    JsonSerializer.Serialize(new { bom.Code, bom.Name, bom.PrimaryOutputItemId }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified)
            {
                yield return new PendingChange(entry, "Bom.Renamed",
                    JsonSerializer.Serialize(new { bom.Code, bom.Name }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Bom.Activated" : "Bom.Deactivated",
                    JsonSerializer.Serialize(new { bom.Code, bom.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is BomVersion bv)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "BomVersion.Created",
                    JsonSerializer.Serialize(new { bv.BomId, bv.VersionNumber, Status = bv.Status.ToString(), bv.BaseOutputQuantity, bv.BaseOutputUnitId }, AuditJson));
                yield break;
            }
            if (entry.Property("Status").IsModified)
            {
                if (dispatchedCodes?.Contains("BomVersion.StatusChanged") == true)
                    yield break;
                yield return new PendingChange(entry, "BomVersion.StatusChanged",
                    JsonSerializer.Serialize(new
                    {
                        bv.BomId,
                        bv.VersionNumber,
                        Before = entry.Property("Status").OriginalValue?.ToString(),
                        After = bv.Status.ToString()
                    }, AuditJson));
            }
            if (entry.Property("BaseOutputQuantity").IsModified || entry.Property("BaseOutputUnitId").IsModified
                || entry.Property("EffectiveFromDate").IsModified || entry.Property("EffectiveToDate").IsModified
                || entry.Property("Notes").IsModified)
            {
                yield return new PendingChange(entry, "BomVersion.Updated",
                    JsonSerializer.Serialize(new { bv.BomId, bv.VersionNumber, bv.BaseOutputQuantity, bv.BaseOutputUnitId }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is BomApplicability ba)
        {
            yield return new PendingChange(entry, "BomVersion.ApplicabilitySaved",
                JsonSerializer.Serialize(new { ba.BomVersionId, ba.Name, ba.IsActive }, AuditJson));
            yield break;
        }

        if (entry.Entity is Item item)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Item.Created", JsonSerializer.Serialize(new
                {
                    Before = (object?)null,
                    After = new { item.Code, item.Name, item.BaseUnitId, item.CanBePurchased, item.CanBeManufactured, item.CanBeSold, item.CanBeConsumed }
                }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("Description").IsModified || entry.Property("BaseUnitId").IsModified
                || entry.Property("CanBePurchased").IsModified || entry.Property("CanBeManufactured").IsModified
                || entry.Property("CanBeSold").IsModified || entry.Property("CanBeConsumed").IsModified)
            {
                yield return new PendingChange(entry, "Item.Updated", JsonSerializer.Serialize(new
                {
                    item.Code,
                    Before = new { Name = entry.Property("Name").OriginalValue, BaseUnitId = entry.Property("BaseUnitId").OriginalValue },
                    After = new { item.Name, item.BaseUnitId, item.CanBePurchased, item.CanBeManufactured, item.CanBeSold, item.CanBeConsumed }
                }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Item.Activated" : "Item.Deactivated",
                    JsonSerializer.Serialize(new { item.Code, IsActive = item.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ClassificationSet cs)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "ClassificationSet.Created",
                    JsonSerializer.Serialize(new { cs.Code, cs.Name, cs.IsMandatory, cs.AllowMultiple }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is Classification cls)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Classification.Created",
                    JsonSerializer.Serialize(new { cls.ClassificationSetId, cls.ParentClassificationId, cls.Code, cls.Name }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("ParentClassificationId").IsModified)
            {
                yield return new PendingChange(entry, "Classification.Updated",
                    JsonSerializer.Serialize(new { cls.Code, cls.Name, cls.ParentClassificationId }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Classification.Activated" : "Classification.Deactivated",
                    JsonSerializer.Serialize(new { cls.Code, cls.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ItemClassification ic)
        {
            yield return new PendingChange(entry, "ItemClassification.Updated",
                JsonSerializer.Serialize(new { ic.ItemId, ic.ClassificationId, Action = entry.State == EntityState.Deleted ? "Removed" : "Assigned" }, AuditJson));
            yield break;
        }

        if (entry.Entity is ItemUnitConversion icv)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "ItemUnitConversion.Created",
                    JsonSerializer.Serialize(new { icv.ItemId, icv.FromUnitId, icv.ToUnitId, icv.Factor }, AuditJson));
                yield break;
            }
            if (entry.Property("Factor").IsModified)
            {
                yield return new PendingChange(entry, "ItemUnitConversion.Updated",
                    JsonSerializer.Serialize(new { icv.ItemId, icv.FromUnitId, icv.ToUnitId, Factor = icv.Factor }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "ItemUnitConversion.Activated" : "ItemUnitConversion.Deactivated",
                    JsonSerializer.Serialize(new { icv.ItemId, icv.FromUnitId, icv.ToUnitId, icv.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is AttributeDefinition ad)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "AttributeDefinition.Created",
                    JsonSerializer.Serialize(new { ad.Code, ad.Name, DataType = ad.DataType.ToString(), ad.UnitId, ad.IsRequired }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("UnitId").IsModified || entry.Property("IsRequired").IsModified)
            {
                yield return new PendingChange(entry, "AttributeDefinition.Updated",
                    JsonSerializer.Serialize(new { ad.Code, ad.Name, ad.UnitId, ad.IsRequired }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "AttributeDefinition.Activated" : "AttributeDefinition.Deactivated",
                    JsonSerializer.Serialize(new { ad.Code, ad.IsActive }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is ItemAttributeValue av)
        {
            yield return new PendingChange(entry, "ItemAttributeValue.Updated",
                JsonSerializer.Serialize(new { av.ItemId, av.AttributeDefinitionId, Action = entry.State == EntityState.Deleted ? "Cleared" : "Set" }, AuditJson));
            yield break;
        }

        if (entry.Entity is Unit unit)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Unit.Created", JsonSerializer.Serialize(new
                {
                    Before = (object?)null,
                    After = new { unit.Code, unit.Name, unit.Symbol, Dimension = unit.Dimension.ToString(), unit.PrecisionScale, unit.IsActive }
                }, AuditJson));
                yield break;
            }
            if (entry.Property("Name").IsModified || entry.Property("Symbol").IsModified || entry.Property("PrecisionScale").IsModified)
            {
                yield return new PendingChange(entry, "Unit.Updated", JsonSerializer.Serialize(new
                {
                    Code = unit.Code,
                    Before = new
                    {
                        Name = entry.Property("Name").OriginalValue,
                        Symbol = entry.Property("Symbol").OriginalValue,
                        PrecisionScale = entry.Property("PrecisionScale").OriginalValue
                    },
                    After = new
                    {
                        Name = entry.Property("Name").CurrentValue,
                        Symbol = entry.Property("Symbol").CurrentValue,
                        PrecisionScale = entry.Property("PrecisionScale").CurrentValue
                    }
                }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Unit.Activated" : "Unit.Deactivated",
                    JsonSerializer.Serialize(new
                    {
                        Code = unit.Code,
                        Before = new { IsActive = entry.Property("IsActive").OriginalValue },
                        After = new { IsActive = entry.Property("IsActive").CurrentValue }
                    }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is UnitConversion conversion)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "UnitConversion.Created", JsonSerializer.Serialize(new
                {
                    Before = (object?)null,
                    After = new { conversion.FromUnitId, conversion.ToUnitId, conversion.Factor, conversion.IsActive }
                }, AuditJson));
                yield break;
            }
            if (entry.Property("Factor").IsModified)
            {
                yield return new PendingChange(entry, "UnitConversion.Updated", JsonSerializer.Serialize(new
                {
                    conversion.FromUnitId,
                    conversion.ToUnitId,
                    Before = new { Factor = entry.Property("Factor").OriginalValue },
                    After = new { Factor = entry.Property("Factor").CurrentValue }
                }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "UnitConversion.Activated" : "UnitConversion.Deactivated",
                    JsonSerializer.Serialize(new
                    {
                        conversion.FromUnitId,
                        conversion.ToUnitId,
                        Before = new { IsActive = entry.Property("IsActive").OriginalValue },
                        After = new { IsActive = entry.Property("IsActive").CurrentValue }
                    }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is Currency cur)
        {
            if (entry.State == EntityState.Added)
            {
                yield return new PendingChange(entry, "Currency.Created", JsonSerializer.Serialize(new
                {
                    Before = (object?)null,
                    After = new { cur.Code, cur.Name, cur.Symbol, cur.PrecisionScale, cur.IsActive }
                }, AuditJson));
                yield break;
            }
            if (entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("IsActive" or "UpdatedAtUtc" or "UpdatedByActorId")))
            {
                yield return new PendingChange(entry, "Currency.Updated", JsonSerializer.Serialize(new
                {
                    cur.Code,
                    After = new { cur.Name, cur.Symbol, cur.PrecisionScale }
                }, AuditJson));
            }
            if (entry.Property("IsActive").IsModified)
            {
                yield return new PendingChange(entry, (bool)entry.Property("IsActive").CurrentValue! ? "Currency.Activated" : "Currency.Deactivated",
                    JsonSerializer.Serialize(new
                    {
                        cur.Code,
                        Before = new { IsActive = entry.Property("IsActive").OriginalValue },
                        After = new { IsActive = entry.Property("IsActive").CurrentValue }
                    }, AuditJson));
            }
            yield break;
        }

        if (entry.Entity is not (Organization or Site))
        {
            yield break;
        }

        var prefix = entry.Entity is Organization ? "Organization" : "Site";
        if (entry.State == EntityState.Added)
        {
            var after = new { Code = entry.Property("Code").CurrentValue, Name = entry.Property("Name").CurrentValue,
                IsActive = entry.Property("IsActive").CurrentValue };
            yield return new PendingChange(entry, prefix + ".Created", JsonSerializer.Serialize(new { Before = (object?)null, After = after }, AuditJson));
            yield break;
        }
        if (entry.Property("Name").IsModified)
            yield return new PendingChange(entry, prefix + ".Renamed", JsonSerializer.Serialize(new {
                Before = new { Name = entry.Property("Name").OriginalValue }, After = new { Name = entry.Property("Name").CurrentValue } }, AuditJson));
        if (entry.Property("IsActive").IsModified)
            yield return new PendingChange(entry, prefix + ((bool)entry.Property("IsActive").CurrentValue! ? ".Activated" : ".Deactivated"),
                JsonSerializer.Serialize(new { Before = new { IsActive = entry.Property("IsActive").OriginalValue },
                    After = new { IsActive = entry.Property("IsActive").CurrentValue } }, AuditJson));
    }
}
