# Slice 01 acceptance plan

## Increment 01.3 verification and repeatable SQL command

Reconciliation: the user subsequently ran the documented combined OrganizationUseCaseSqlTests/OrganizationSqlServerTests command in normal Windows PowerShell and pasted the result: **12 passed, 0 failed, 0 skipped**. Mark the six new SQL service/race/retry cases and six affected regressions PASS (USER-EXECUTED); 01.3 is verified for its stated gates. The earlier agent counts/skips below remain historical, not a new execution. No unchanged tests were rerun. Local TrustServerCertificate remains local-session-only evidence, not production TLS validation. The remaining AUTH-01 and authenticated UI gates are not covered by that SQL run.

Agent-executed Release build passed all five projects, zero warnings/errors. Fresh focused tests: 23 passed (16 use-case/gateway cases, 7 affected persistence-boundary cases), 6 SQL cases skipped, 0 failed. Test doubles are defined only under tests. Missing actor/permission/scope, revoked permission, changed session, wrong owner/guessed IDs, per-operation permissions, deployment-level creation, paging/site predicates, expected-version shape and lazy fail-closed registration are covered without a database. These assertions are not SQL isolation or AUTH-01 integration proof.

The six `OrganizationUseCaseSqlTests` now PASS on the user-executed run: scoped SQL queries/duplicates/stale updates/no-ops/audit actor; atomic rollback and narrow error mapping; barrier-synchronized Site creation vs Organization deactivation; Site activation vs Organization deactivation; configured retry with rollback/no duplicated effects and revoked grants on retry; lost commit acknowledgement without replay. Fault injectors exist only inside tests. No InMemory/SQLite proof is used. The disposable harness selects no existing database. The agent did not repeat SQL connection probes or tests for this reconciliation. The separate earlier 01.2 six-pass evidence is preserved; the new combined 12-pass run covers both sets.

In normal Windows PowerShell, privately set FORGEFLOW_TEST_SQLSERVER for an authorized disposable SQL Server session, then run:

```powershell
dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1
dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~OrganizationUseCaseSqlTests|FullyQualifiedName~OrganizationSqlServerTests' --logger 'console;verbosity=normal'
```

Reported outcome: all 12 SQL cases executed, zero skipped/failed. The six 01.2 cases were included because the attributed-save transaction path changed in 01.3. Use only generated disposable databases and remove the test-session variable afterward if repeating affected checks later. Any local TrustServerCertificate exception is not production TLS validation. Broader AUTH-01/revocation/circuit and bilingual management UI acceptance remain required later.

01.1 result: 35 Domain/contract test cases passed against fresh Release binaries (OrganizationDomainTests and OrganizationAccessContractTests in the existing test project). D01 and the in-memory parts of D02/D03 are covered, including active-parent restrictions and inactive Site code reservation. Deployment-wide organization uniqueness, SQL/race behavior and A01–A06 runtime authorization/use-case behavior remain unverified; actor/scope constructor guards and a default-deny decision are not permission enforcement. S01–S10 and B01–B07 remain planned, not executed. Exact commands/results are in ForgeFlow.md.

01.2 actual result: Release build passed; 42 passed (the 35 existing focused cases plus 7 offline persistence/model/factory/materialization cases), 6 SQL tests skipped, 0 failed. Snapshot consistency and script generation pass offline. No SQL behavior is PASS. `OrganizationSqlServerTests` covers fresh apply/empty rollback/reapply, code scopes and inactive reservation, bad FKs/code checks/referenced deletion, rowversion competition, audit rollback/Unicode payload sizing and runtime-role denials. It creates only `ForgeFlow_Slice01_Test_<guid>` databases; overrides supplied InitialCatalog with master for CREATE/DROP and with its generated name for all application SQL. No existing user database is selected. S05 synchronized lifecycle races and other broader secured use-case scenarios remain future acceptance work even after these six tests run.

Prerequisite: set `FORGEFLOW_TEST_SQLSERVER` privately in a normal authorized execution environment to a SQL Server connection with working authentication/TLS, CREATE/DROP DATABASE rights and owner rights inside the newly created disposable databases. Do not alter host security or provision the sandbox SID to run these tests. Do not commit the value. Run the fresh Release build, then:

```powershell
dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~OrganizationSqlServerTests' --logger 'console;verbosity=normal'
```

Acceptance requires all six actually executed, zero skipped. With the variable absent they deliberately skip; that result is BLOCKED, not PASS. This run's read-only local sqlcmd probe failed with `Encryption not supported on the client` and `SSL Provider: No credentials are available in the security package`. The recorded probe used sqlcmd -C; no such certificate-validation exception is configured in runtime code/tests. No alternate credential, host setting change or user-database write was attempted. Evidence: `verification/organization-persistence-01-2/results.json` and generated forward/rollback SQL.

Slice 00 results remain valid for unaffected features and were not rerun as a general audit. Later integration tests must use synthetic organizations ALPHA/BETA, sites A1/A2 and B1/B2, separate granted actors, and deterministic clocks where appropriate. Store no credentials, personal data, tokens, keys or connection strings in artifacts.

## Domain and Application

| ID | Scenario and required result |
|---|---|
| D01 | Codes ` a-1 ` and `A-1` canonicalize identically; ASCII case independent of fa-IR/en-US. Length 1/32 accepted; 0/33 rejected; Persian characters/digits, Unicode lookalikes, controls, embedded/non-ASCII spaces rejected. Names accept Persian/English Unicode but reject blank, controls and >200 characters |
| D02 | Code/parent immutable; create active; status and rename only by invariant-preserving commands. Inactive codes stay reserved. No delete/transfer command |
| D03 | Deactivate organization with active site refused; after all sites inactive succeeds; inactive parent refuses site create/rename/activation; parent reactivation leaves sites inactive |
| A01 | Missing/untrusted actor, missing scope, no membership, expired/revoked grants and wrong operation permission deny before data mutation; no default organization and no anonymous fallback |
| A02 | Site A1 reader cannot list/get A2 or B1; organization-shared master permission is distinct from site-specific access; cross-org/nonexistent IDs have indistinguishable not-found results after authorization. Filters/search/paging/counts cannot leak unauthorized rows |
| A03 | Organization-wide reader sees only that organization; platform create grant does not imply platform-wide data read. Lifecycle grant can activate an inactive organization without granting ordinary operations on it |
| A04 | DTO actor/OrganizationId/SiteId tampering cannot change server-established scope; membership alone cannot grant all operations. Unavailable trusted adapter fails closed |
| A05 | Stable error codes/typed safe parameters; no translated Domain strings; duplicate/conflict mapping scoped correctly, no SQL errors or forbidden entity names in responses |
| A06 | Repeated create yields one row/event and CodeDuplicate; stale update returns conflict; matching-version no-op makes no audit; ambiguous commit is not blindly retried. Failed operations leave no business audit success event |

Future Items/units/BOM isolation is a contract obligation for their owning slices, not a claim of tested tables here. Test only ownership/scoping contracts and existing Organization/Site references in Slice 01; do not invent Item/BOM fixtures backed by placeholder production tables.

## Real SQL Server integration

| ID | Scenario and required result |
|---|---|
| S01 | Fresh baseline migration reproducible; exact SQL types/nullability/checks/indexes/FKs/rowversion verified in SQL metadata; no out-of-scope tables. Existing DB inventory branch documented before migration |
| S02 | Concurrent duplicate organization creates: one success/one scoped duplicate; sites with same Code in A/B allowed, duplicates inside A denied including inactive records; direct noncanonical code writes rejected |
| S03 | FK rejects nonexistent organization and audit SiteId belonging to another organization; delete parent/referenced site denied; application SQL principal cannot delete business rows or update/delete audit rows |
| S04 | Two contexts edit same rowversion: one success, one concurrency conflict, one matching audit change. Same exercise for rename vs deactivate and two different sites; no lost updates |
| S05 | Concurrent parent deactivate vs site create/activate, synchronized with a barrier: serialize correctly and never commit active child under inactive parent; include rollback and simultaneous deactivate cases. Verify isolation/locking on real SQL, not mocks |
| S06 | Audit insert failure rolls back create/update/status; business write failure leaves no success audit; audit values and trusted actor/correlation correct, append-only privileges proven under runtime SQL identity |
| S07 | List/detail/update queries always scope OrganizationId and authorized SiteIds, including inactive filters, paging/counts and guessed IDs; log capture excludes secrets and forbidden resource information |
| S08 | UTC field mapping round trips and updated-field pairing; expected rowversion mandatory; operation-scoped contexts do not share concurrent circuit access; cancelled command rolls back |
| S09 | Failure before commit vs unknown commit outcome distinguished; do not auto-retry a successful-but-unacknowledged write. Reload can establish outcome without reapplying mutation |
| S10 | Migration upgrade/rollback on disposable SQL with representative data: documented backup/restore or forward recovery preserves business/audit records; no destructive downgrade disguised as rollback |

## AUTH-01 integration and eventual real browser

Test actors are sufficient for Application isolation tests, not for UI/authenticated acceptance. AUTH-01 must supply real authenticated sessions before these gates run. The AUTH-01-owned profile/logout/Identity-provider tests remain in that slice; Slice 01 consumes its trusted adapter and proves organization/site access at the use-case/UI boundary.

| ID | Scenario and required result |
|---|---|
| B01 | Authorized organization/site list, detail, create, rename, deactivate/reactivate; denied users never gain access through direct URL or direct command. Narrow site membership cannot read/manage another site |
| B02 | Full workflows in fa-IR/RTL and en-US/LTR at desktop and 320px, no horizontal overflow; codes unchanged, user names preserved, translated field/help/validation/status/dialog/notification/accessibility text; resource/placeholder/scanner checks pass |
| B03 | Keyboard-only forms/lists/dialogs, meaningful accessible names, initial focus, focus containment, Escape, restoration, validation focus and live success/error announcement; loading/empty/forbidden/not-found/network-failure states visible |
| B04 | FA -> EN -> FA preserves unsaved values, dirty state and scope; refresh restores authorized per-tab draft. Save/discard clears draft. Switching organization, actor or losing permission cannot restore another scope's draft; two tabs remain isolated |
| B05 | Two real sessions edit same entity; loser sees localized conflict without silent overwrite, can explicitly reload; server-stale status changes and duplicate codes show localized recoverable errors |
| B06 | Revoked membership/session in an already-open circuit denies the next read/write; a stale UI scope selection grants nothing. UI caches/drafts are cleared or hidden on denial before showing protected values |
| B07 | Controlled bootstrap establishes first organization/site through AUTH-01-approved authority; anonymous requests cannot create/manage records before or after bootstrap. No static/default credentials |

## Acceptance record required later

Release build through ForgeFlow.slnx with unchanged pinned dependencies; affected Domain/Application/SQL/Web tests against fresh binaries; real SQL privilege/isolation/concurrency evidence; browser results/screenshots for both cultures/widths and real authorization. Keep native Development and isolated SmokeTest evidence distinct. Record gaps as OPEN/BLOCKED with precise cause, never PASS from mocks or historical outputs. Design approval, code completion and Slice 01 acceptance are separate statuses. No Slice 01 acceptance until every applicable gate above passes, including the UI and AUTH-01 adapter dependency.
