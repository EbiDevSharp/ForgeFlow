# ForgeFlow Roadmap (Completed & Future Slices)

This document tracks the implemented baseline of **ForgeFlow** and the proposed roadmap for **Slice 14+** so new sessions can start directly from a shared plan.

---

## 1. Completed & Accepted Baseline (`AUTH-01` through `Slice 13`)

| Area | Slice / Increment | Status | Key Capabilities |
|------|-------------------|--------|------------------|
| **Platform & Security** | `AUTH-01` | Accepted | Cookie auth (`__Host-`), login/logout, lockout, session stamp validation, rate limiting |
| **Platform & Security** | `01-organization-sites` | Accepted | Organizations, Sites, scope switcher, bilingual shell (`fa-IR`/`en-US`, RTL/LTR) |
| **Platform & Security** | `02-users-memberships` | Accepted | Scoped users, org/site memberships, admin password reset, self change-password |
| **Platform & Security** | `03-roles-permissions` | Accepted | Dynamic role builder, permission catalog, user direct/role permission matrix |
| **Platform & Security** | `04-audit-viewer` | Accepted | Security/audit event viewer, export, diagnostics error log viewer |
| **Master Data & Engineering** | `05-item-catalog` | Accepted | Units of measure & conversions, currencies, item catalog, classifications, attributes, multi-version BOMs |
| **Manufacturing & Inventory** | `06-production-orders` | Accepted | Production orders, material issue/consumption, finished goods receipt |
| **Manufacturing & Inventory** | `07-inventory` | Accepted | Stock balances, inventory ledger transactions, lot tracking & adjustments |
| **Analytics** | `08-reports-variance` | Accepted | Operational reports, material/production variance analysis |
| **Quality & Traceability** | `09-quality-traceability` | Accepted | QC inspections, quarantine holds, lot expiry, multi-level forward/backward traceability |
| **Procurement** | `10-procurement` | Accepted | Suppliers, purchase orders (`PO`), goods receipt into inventory/lots |
| **Shop-Floor Engineering** | `11-work-centers-routings` | Accepted | Work centers, routings, production order operations (planned vs. actual minutes) |
| **Operations & Reservations** | `12-reservations-dashboard` | Accepted | Stock reservations, auto-consume/release lifecycle, executive operations dashboard |
| **Laboratory & COA** | `13-lab-specs-coa` | Accepted | Dynamic lab parameters, item/classification inspection specs, lot Certificate of Analysis (`COA`) |
| **Cross-Cutting UX & 2FA** | Post-13 Increments | Implemented | Preferences (theme/calendar/timezone), active sessions, my activity, notifications, TOTP 2FA + recovery codes, Passkeys (WebAuthn), Admin 2FA reset, grouped accordion permissions modal, Persian HTML/PDF guides |

---

## 2. Proposed Future Slices (`Slice 14+`)

### Slice 14 — Sales, Customers & Dispatch (`14-sales-dispatch`) *(Recommended Next)*
- **Goal:** Complete the **Order-to-Cash** loop alongside existing Procurement (**Procure-to-Pay**) and Manufacturing (**Plan-to-Produce**).
- **Scope:**
  - Customer master data (organization-scoped, contacts, shipping addresses, active/inactive status).
  - Sales Orders (`SO`) with line items, requested delivery dates, and stock reservation integration (`Slice 12`).
  - Dispatch / Shipment notes (`DeliveryOrder`) picking from approved/non-quarantined lots (`Slice 07`/`09`) with automatic attachment/reference to Lot `COA` (`Slice 13`).
  - Sales & Dispatch permissions (`Sales.Read`, `Sales.ManageCustomers`, `Sales.ManageOrders`, `Sales.Dispatch`).

### Slice 15 — MRP & Capacity Scheduling (`15-mrp-scheduling`)
- **Goal:** Proactive material & capacity planning before shortages occur.
- **Scope:**
  - Item `ReorderPoint`, `SafetyStock`, and `LeadTimeDays` settings.
  - Net-requirements MRP run across open Sales Orders, Production Orders, BOM explosion (`Slice 05`), free/reserved stock (`Slice 12`), and open Purchase Orders (`Slice 10`).
  - One-click generation of draft Purchase Orders or draft Production Orders from MRP suggestions.
  - Work Center capacity calendar and load vs. capacity view (`Slice 11`).

### Slice 16 — Inventory Valuation & Manufacturing Costing (`16-costing-valuation`)
- **Goal:** Financial visibility into stock value and production unit cost.
- **Scope:**
  - Inventory valuation methods (`FIFO` or Moving Average) on stock ledger transactions (`Slice 07`) using existing Currencies (`Slice 05`).
  - Work Center hourly/minute labor & overhead rates (`Slice 11`).
  - Actual vs. Standard Production Order cost rollup (raw material cost + operation/overhead cost + scrap variance).

### Slice 17 — Equipment Maintenance & Downtime / OEE (`17-cmms-oee`)
- **Goal:** Track machine reliability and Overall Equipment Effectiveness (`OEE`).
- **Scope:**
  - Equipment/Asset registry linked to `WorkCenter` (`Slice 11`).
  - Preventive Maintenance (`PM`) schedules and Corrective Maintenance (`CM`) work orders.
  - Production operation downtime reason codes and automated `OEE` (`Availability × Performance × Quality`) reporting.

### Slice 18 — Shop-Floor Kiosk, Bin Locations & Barcode/QR (`18-shopfloor-barcode`)
- **Goal:** Fast, touch-friendly execution on the factory floor and warehouse.
- **Scope:**
  - Warehouse/Bin Location hierarchy per Site (`Slice 01`/`07`).
  - Printable QR/Barcode labels for Lots, Items, Production Orders, and Bins.
  - Simplified tablet/kiosk mode for operators to start/pause/complete operations, record scrap, and scan lots.

### Slice 19 — Scheduled Alerts, Official Printouts & External Integrations (`19-alerts-integrations`)
- **Goal:** Automation, printable official documents, and ERP/accounting connectivity.
- **Scope:**
  - Background scheduled jobs for expiring lots (`Slice 09`), low-stock/reorder alerts, and expired session cleanup.
  - Standardized printable/PDF templates (Goods Receipt, Dispatch Note, Production Order Traveler, COA).
  - Outbound Webhooks / REST API keys for external accounting/ERP sync.

---

## 3. Working Agreement Reminder
- Each slice starts with a `XX.0` design package under `docs/slices/<slice-name>/` (`README.md`, `decisions.md`, `physical-erd.md`, `test-plan.md`).
- Implement in small, verified increments, keeping `ForgeFlow.md` and `docs/` (`deployment-guide-fa.html`, `admin-guide-fa.html`) synchronized.
