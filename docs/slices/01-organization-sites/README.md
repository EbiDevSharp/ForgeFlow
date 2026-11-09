# Slice 01 — Organization and sites

Status: 01.1–01.3 VERIFIED for their stated gates. The combined 01.3 SQL run is USER-EXECUTED from normal Windows PowerShell, pasted result 12 passed/0 failed/0 skipped; prior agent evidence is separate. Slice 01 INCOMPLETE, not accepted pending real authentication and UI. [AUTH-01](../auth-01/README.md) is a design proposal only. Later implementation is not approved by implication. Slice 00 remains accepted; ForgeFlow.md owns the current receipts.

## Contract ownership

Shared constraints and the slice index live in [ForgeFlow.md](../../../ForgeFlow.md#slice-index-and-shared-boundaries--01-design). Slice-specific decisions, including the sole ownership matrix, live in [decisions.md](decisions.md). Tables and constraints live only in [physical-erd.md](physical-erd.md); executable acceptance scenarios are specified in [test-plan.md](test-plan.md). Historical Day 1–10 entities are business context, not code present in this checkout.

## Scope and completion

Deliver Organization and Site lifecycle, scoped reads, authorized writes, atomic change audit and their bilingual management UI. Plant is the UI/business synonym for a manufacturing Site, not a second entity. No Item, BOM, unit, classification, Identity, membership, warehouse, line or ManufacturingContext tables are created here. No organization transfers, mergers, hard delete, imports, generic administration framework or public organization directory.

Current solution: Domain contains Organization/Site invariants. Application contains explicit request/result DTOs, actor/scope/permission contracts, OrganizationUseCases and its specialized persistence port. Infrastructure implements scoped SQL operations and atomic business/audit transactions. Composition registers the operation context factory and a scoped use-case service with optional real adapters; absence of adapters denies before persistence resolution. No production test doubles or AUTH-01 adapter are registered. No management endpoints or UI are exposed. Dependencies, mappings and migrations are unchanged in 01.3.

Design may proceed before AUTH-01. Domain, persistence and Application contracts can be implemented and tested with explicit test actors before authentication exists. Production management routes and UI cannot be enabled, and Slice 01 cannot be accepted, until AUTH-01 supplies a trusted actor, membership and permission adapter. AUTH-01 can depend on the narrow scope contracts from increment 01.1; it must not depend on Slice 01 UI acceptance. This phased dependency replaces any historical interpretation requiring completed Slice 01 before authentication work can begin.

## Application use cases

Inputs are typed DTOs. Every use case receives a server-derived actor and explicitly requested scope; payload actor IDs never establish authority. Lists return a page (default 25, maximum 100), stable code/ID ordering, and only authorized rows. Include inactive records only on an explicit filter. Detail/edit results carry rowversion as opaque Base64. Validation returns stable codes and typed parameters, never translated domain messages.

| Use case | Input/output | Required permission and scope |
|---|---|---|
| List/Get Organization | Filter or ID -> authorized summary/detail | Organizations.Read in that organization; no implicit platform-wide list |
| Create Organization | Code, Name -> ID, rowversion | Organizations.Create with explicit platform authority; ordinary organization membership cannot create a new tenant |
| Rename Organization | ID, Name, expected version -> version | Organizations.Update, organization scope |
| Activate/Deactivate Organization | ID, expected version -> version | Organizations.SetActive, organization scope; activation of an inactive organization requires an explicit lifecycle grant checked outside the active-organization gate |
| List/Get Site | OrganizationId, optional site/filter -> scoped page/detail | Sites.Read for the organization or explicitly granted site IDs; a site member sees only permitted sites |
| Create Site | OrganizationId, Code, Name -> ID, version | Sites.Create at organization scope; active organization |
| Rename/Activate/Deactivate Site | OrganizationId, SiteId, expected version, change -> version | Sites.Update or Sites.SetActive at organization scope or an explicitly granted site; parent must be active |

All writes require a valid actor, current grants and a fresh expected version except creates. Cross-scope and nonexistent IDs return the same not-found result after scope validation. No management use case falls back to anonymous/system/organization 1. A normal user cannot grant permissions or memberships in this slice.

Stable codes: `Organization.CodeInvalid`, `Organization.NameInvalid`, `Organization.CodeDuplicate`, `Organization.NotFound`, `Organization.Inactive`, `Organization.HasActiveSites`; `Site.CodeInvalid`, `Site.NameInvalid`, `Site.CodeDuplicate`, `Site.NotFound`; `OrganizationSite.ConcurrencyConflict`, `OrganizationSite.ScopeRequired`, `OrganizationSite.AccessDenied`, `OrganizationSite.ActorRequired`, `OrganizationSite.AuditUnavailable`, `OrganizationSite.PersistenceUnavailable`, `OrganizationSite.OutcomeUnknown`. Parameters contain only safe field names, limits or authorized identifiers. Forbidden resource IDs must not appear in parameters. Web maps these codes to explicit fa-IR/en-US resource entries; logs use correlation IDs and sanitized diagnostics.

Each mutating command uses one operation-scoped DbContext and one SQL transaction for the aggregate update and audit insert. Audit failure rolls everything back. No external side effect. Parent lifecycle and site writes serialize on the same Organization row (parent update/read lock held until commit); deactivate checks for active sites while holding that lock. All site write paths use the same lock protocol, preventing a concurrent create/activate from bypassing the rule. Parent lock precedes site lock to avoid reversed ordering. No-op status/name commands with a matching version return unchanged state with no mutation/audit; a stale version is always a conflict. Duplicate creates return CodeDuplicate and never add an audit event. No automatic retry of an ambiguously committed command: return OutcomeUnknown with correlation ID and let the authorized caller reload. Replay after successful rename/status change with the old version conflicts. This is not an exactly-once command ledger.

Audit event names: `Organization.Created`, `.Renamed`, `.Activated`, `.Deactivated`, and equivalent `Site.*`. Persist an allowlisted before/after change and trusted actor in the same transaction. Access denials generate sanitized security events through the future AUTH-01 security sink outside a failed business transaction. If that sink fails, keep the denial, emit a sanitized technical alert and never broaden access. Do not persist credentials, tokens or request bodies. SQL application credentials must not update/delete the audit table. The audit table is a supporting part of Slice 01; an audit viewer remains future work.

## Eventual UI workflows

Proposed routes `/organizations`, `/organizations/{id}`, `/organizations/{id}/sites`, and `/organizations/{id}/sites/{siteId}` are enabled only after the server authorization adapter exists. Show only allowed organizations/sites; use an explicit scope selector, never an unfiltered picker. Create buttons require the corresponding grant. Organization and site forms expose Code (create only), Name, status and version; code becomes read-only after creation. Business names are user data, not resource messages. No automatic translation of names or identifiers.

Lists support search, paging and active/inactive filters. Create/edit use localized field help, required/invalid/duplicate validation, save/cancel and unsaved-change confirmation. Status changes require a localized confirmation naming the entity. Display loading, empty, forbidden, not-found, save failure and concurrency conflict states; conflict offers reload with explicit warning before discarding unsaved values. Successful save announces localized feedback and refreshes the authorized row. No silent overwrites, optimistic success before commit or delete button.

Reuse accepted centralized culture/direction/formatting and per-tab drafts. Draft scope includes actor, OrganizationId, optional SiteId, form ID and schema; clear on save/discard, AUTH-01 identity change and lost access. Restored drafts never bypass current authorization or rowversion checks. UI labels, validation, dialog/notification and accessibility text use explicit .resx catalogs. Require Persian/English, RTL/LTR, keyboard/focus, desktop and 320px layouts, with no overflow. UI implementation is a separate task and remains mandatory for completion.

## Small increments and exits

| Increment | Dependency | Exit evidence |
|---|---|---|
| 01.0 Decision closure for Domain increment | User answers Q1–Q3 in decisions | Q1–Q3 approved; physical/deployment/audit proposals remain separate prerequisites for their increments |
| 01.1 Domain and Application boundary | Approved Q1–Q3 and explicit 01.1 authorization | COMPLETE: code/name/lifecycle invariants, narrow scope/actor/permission contracts and 35 passing tests; no endpoints or persistence |
| 01.2 SQL persistence and atomic audit | 01.1; explicit 01.2 authorization | VERIFIED for stated gates: offline agent evidence plus user-executed six SQL tests, 6 passed/0 failed/0 skipped; local test TLS exception is not production TLS proof |
| 01.3 Secured use cases | 01.2 | VERIFIED: prior 23 non-SQL passes plus user-executed 12-pass combined SQL run; no trusted adapter still means denial |
| AUTH-01 adapter handoff | 01.1 contracts; AUTH-01 separately authorized | Real actor/membership/permission, session invalidation and controlled bootstrap established; no temporary anonymous management routes |
| 01.4 Bilingual management UI | 01.3 and AUTH-01 adapter | End-to-end authorized CRUD/status workflows and affected browser/resource/scanner checks pass in both cultures/widths |
| 01.5 Acceptance receipt | All above | Actual Release/SQL/browser evidence, isolation and concurrency gates pass; no required gate deferred or inferred from mocks |

Initial organization/site provisioning is controlled, authenticated platform/bootstrap work owned by AUTH-01 integration; never hardcoded credentials, anonymous seeds or default tenant scope. Database creation and app-data provisioning are separate actions. Existing database reconciliation is required before any migration; greenfield repository does not prove an empty deployed database. No destructive migration or automatic data deletion is authorized by this design.
