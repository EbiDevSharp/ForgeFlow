# Slice 11 — Test Plan (`11-work-centers-routings`)

| Test ID | Scope | Scenario |
|---|---|---|
| `W01` | Domain | `WorkCenter`, `Routing`, `RoutingOperation`, `ProductionOrderOperation` invariants, positive capacities/hours, valid transitions (`Draft -> Active -> Obsolete`, `Pending -> Ready -> InProgress -> Paused -> Completed/Skipped`), duplicate sequence rejection. |
| `W02` | Application | RBAC (`Routings.Read`, `Routings.Manage`, `Operations.Execute`), site isolation, manufacturable-only item check on routing, planned duration calculation `(Setup + RunPerUnit * Qty)` & work center capacity load summary. |
| `S01` | SQL E2E | Create/update/activate Work Center; create Routing with sequential operations (`10`, `20`), activate & set default; enforce unique active default & unique `(SiteId, Code)`. |
| `S02` | SQL E2E | Create Draft Production Order auto-resolving active default Routing -> release transitions operations to `Ready` -> start, pause, resume, report progress/scrap, and complete operations -> verify Work Center load calculation & `OrganizationChange` audit trail. |
| `S03` | Localization | `ResourceContractTests` and `HardcodedDisplayTextScannerTests` pass across `en-US` and `fa-IR`. |
