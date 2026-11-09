# Decisions and ownership

01.3 implementation clarification: existing approved ownership/lifecycle policy is unchanged. Organization list requests require an explicit organization scope and return only that organization; no deployment-wide directory or inferred membership enumeration is added. Site-scoped lists return only the explicitly authorized site; organization-scoped lists require the corresponding organization-level grant. AUTH-01 is responsible for evaluating active membership and permissions for each exact requested scope. SQL additionally matches owner and target IDs. Organization creation alone uses Platform scope plus Organizations.Create. No grant is inferred from an actor ID or a supplied scope. Unexpected adapter errors deny by aborting the operation, never by substituting an actor/grant.

Status: Q1–Q3 APPROVED by the user for increment 01.1. Other proposals remain proposals; approval of ownership/identifier policy does not approve a physical schema, deployment model or audit implementation. Shared established constraints are in ForgeFlow.md FF-A001–A007 and the Slice 01 shared-boundaries entry.

01.2 authorization update: the user separately authorized persistence mappings/SQL constraints and the explicitly specified minimum audit when required by the approved contract. FF-A005's atomic-first-write rule applies, so OrganizationChange persistence is included now. Routine decisions (private EF constructors/field navigation, shadow UTC/actor/rowversion metadata, factory-only operation lifetime, finite object permissions role without users, explicit SQL check expressions) implement that documented model. They do not approve P02 deployment topology, new ownership/lifecycle semantics, runtime authentication or audit-viewer work. SQL execution remains blocked by test connection access, not by a new ownership decision.

## Approved decisions and consequences — increment 01.1

- Q1 approved: Organization is the independent company boundary with Sites; Plant is a synonym, not an entity. Implemented one Organization aggregate owning Sites; no hierarchy, owner reassignment or separate Plant model.
- Q2 approved: the ownership matrix below is authoritative policy. Future editable masters require organization ownership; site applicability/settings require site scope; only immutable reference identifiers/dimensions are global. No future master tables are implemented here.
- Q3 approved: immutable ASCII codes, maximum 32 characters; organization codes deployment-unique, Site codes organization-unique, inactive codes reserved, names separate. Implemented the documented normalization/grammar and in-aggregate Site uniqueness, including inactive Sites. Deployment-wide uniqueness and race-proof SQL uniqueness remain future obligations, not in-memory claims.

The user's separate authorization for 01.1 covers the documented creation/name/lifecycle invariants and narrow actor/scope/permission contracts. It does not approve P02 deployment details or P06 audit schema. The physical design remains a proposal. Ordinary Organization rename requires an active organization, consistent with the documented restriction on ordinary inactive-organization operations. All Site mutations (including no-ops) require an active parent; Organization activation/deactivation alone may operate on an inactive parent.

## Established constraints

Separate organization/site authorization from manufacturing version selection; fail closed on missing scope. Trusted actor and atomic business audit are required on the first write. Preserve layer boundaries, UTC instants, operation-scoped persistence and rowversion. AUTH-01 owns authentication and membership implementation. Existing accepted bilingual behavior is the UI foundation.

## Decision detail (approval is limited to the items recorded above)

P01: Organization is the administrative data owner and security boundary, initially one operating company. It is not a holding-company grouping or an accounting/legal-registration subsystem. Site is a physical operating location inside exactly one organization; Plant is its manufacturing synonym. An organization may have multiple sites; a site cannot be reparented. A production line or warehouse is a later facility resource under a site, not a site synonym. Do not add a SiteType enum without a workflow requiring it.

P02: Initially deploy one database/application with one organization and one site. Permit multiple organizations and sites from the first schema and test at least two of each; no singleton IDs, global selected-tenant variables or database-per-tenant implementation. A selected ID is an untrusted request. Authentication identifies the actor; authorization validates membership, operation and exact resource scope on every use case, including existing Blazor circuits. Database backup/administrator isolation is operational, not guaranteed by row filtering. SQL row-level security is not part of this proposal; composite FKs and scoped repositories supplement Application enforcement, not replace it.

P03: ManufacturingContext remains a future runtime value object for resolver applicability. A SiteId in it is validated against the already authorized organization/site; it confers no permission. Organization-shared BOM definitions can have explicit site applicability later. No null-scope fallback can silently select another site's definition.

## Ownership matrix — approved authoritative policy

These are future module obligations, not tables or behavior implemented in Slice 01. A row's scope is mandatory; a nullable owner must not mean shared with everyone. IDs alone never prove ownership. Future organization-owned references use `(OrganizationId, Id)` candidate keys/FKs, and site-owned references use `(OrganizationId, SiteId, Id)`; reads must apply corresponding predicates. Global references are explicitly typed/allowlisted separately, never discovered by retrying an unscoped lookup.

| Data | Owner / visibility | Uniqueness and reference policy for future slices |
|---|---|---|
| Global reference data | Immutable shipped technical/reference definitions: unit dimensions, ISO currency identifiers; no tenant-editable business masters | Versioned stable identifiers; no organization writes; not authorization grants |
| Items | Organization-shared | Normalized item code unique within organization; sites reference the same item identity; no site clone or automatic cross-org sharing |
| Classification sets and nodes | Organization-shared | Set code per organization; node code per organization/set; parent must share organization/set |
| Attribute groups/definitions and allowed values | Organization-shared | Definition code per organization; allowed value code per definition; classification applicability stays in same organization |
| Item attribute values | Organization-shared, owned by item | `(OrganizationId, ItemId, DefinitionId)` (plus future explicit multiplicity if required); definition/allowed option/item same organization; no hidden site override |
| Units | Organization-shared master data, including local copies of seeded standard units | Unit code per organization; dimension refers to immutable global definition; seeding does not create mutable cross-org shared rows |
| Global/generic conversions | Organization-shared; “global” means independent of an item, not shared across organizations | Directed unit pair per organization; both endpoints same organization; no implicit cross-org graph edges |
| Item-specific conversions | Organization-shared, owned by item | Item/unit pair or directed edge per future conversion contract; item and unit endpoints same organization; no implicit site-specific factors |
| BOM definitions | Organization-shared | BOM code per organization; output/input items and units same organization |
| BOM versions | Organization-shared, owned by BOM | Version number per organization/BOM; revisions/history remain in owner organization; applicability cannot change owner |
| Site applicability and later site operational settings | Site-specific, not a separate BOM owner | Site must belong to BOM organization; only permitted sites exposed; site A grant cannot read/manage site B applicability or operations |

There are no site-specific master duplicates in this proposal. Future warehouses, line capacity, local inventory and execution records will be site-specific, with separate contracts; no such tables are designed here. Moving or copying data between organizations is an explicit future import/transfer use case, never an ID edit. Ownership changes require a contract amendment before later master-data work.

P04: Codes are language-independent: trim leading/trailing ASCII spaces, convert ASCII a–z to A–Z, then require 1–32 ASCII characters `[A-Z0-9][A-Z0-9_-]*`. Reject other whitespace, embedded spaces, Persian digits/letters, Unicode lookalikes and overlength canonical values; no transliteration or culture-sensitive case conversion. Persist only canonical Code. Organization codes are deployment-wide unique; Site codes are unique per organization. Deactivated codes remain reserved. Codes and parent ownership are immutable after create. Names are trimmed Unicode, 1–200 characters, may be Persian or English, and need not be unique; reject whitespace-only/control characters, preserve meaningful script/spacing without Persian/Arabic substitution. No uniqueness inferred from display names.

P05: Create active by default; deactivate only explicitly. Organization deactivation is refused while any Site is active; no cascading status changes. Inactive records remain readable for authorized lifecycle/history operations, but cannot receive new operational references. Site reactivation requires active parent; activating parent does not activate children. Initial minimum one organization/site is deployment intent, not a deletion rule or global count constraint. Last-admin/access protection is AUTH-01 work. Hard deletion, key/code reuse and reparenting have no use case; deny them at database privilege level. Later referenced inactive records remain valid historical references.

P06: Proposed minimal module-owned append-only audit table accompanies Organization/Site. It is required support for FF-A005, not an audit browsing module. ActorId is a positive logical long from the trusted AUTH-01 actor contract, without a speculative Identity FK/table; preserve actor IDs even after account deactivation. System bootstrap requires an explicit audited principal from AUTH-01, never 0. The exact table and constraints are specified only in physical-erd.md.

## Resolved material questions

The user answered Yes to Q1–Q3 and authorized 01.1. The original questions and consequences below retain traceability; they are no longer blockers for 01.1. SQL/provider/deployment/audit design and security integration are not approved implicitly.

| Question | Recommended answer | Consequence / alternative impact |
|---|---|---|
| Q1. Is Organization the independent company/data boundary, and is Site one physical operating location with Plant as its manufacturing name? | Yes; no separate Plant entity or group hierarchy | Approves Organization 1:N Site. If one plant spans independently scoped locations or companies share a tenant, relationship/security semantics need redesign before physical work |
| Q2. May sites in an organization share Items, classification/attributes, units/conversions and BOM definitions, with explicit site applicability? | Yes, as the matrix specifies; generic conversions are organization-shared, not deployment-wide | Approves owner scopes and future composite reference contracts. Site-private masters/BOM ownership or centrally editable cross-company units change uniqueness and reference strategy; identify exceptions now |
| Q3. May codes be immutable ASCII identifiers (32 chars), with organization codes deployment-unique and site codes organization-unique, and may deactivated identifiers remain reserved? | Yes; business names accept Persian Unicode | Fixes indexes, normalization and lifecycle. Persian codes, code renames, code reuse or cross-organization duplicate organization codes require another canonicalization/key contract |

Security dependency is an established constraint rather than an optional shortcut: full Slice 01 UI acceptance waits for a trusted AUTH-01 adapter. SQL instance/version/collation, existing schema history, backup and disposable-test database details are implementation prerequisites to discover at 01.2; no machine-specific connection string or permission grant is designed here.
