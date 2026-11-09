# AUTH-01 decisions

Status: A1 Identity persistence and controlled initialization explicitly authorized and VERIFIED by separate user-executed SQL/native evidence plus agent non-SQL evidence. The three decisions below are approved; other workflows remain scoped to their separate increments. AUTH-01 is not accepted.

## Approved A1 decisions

1. The deployment owner/user is the initial host operator for supervised bootstrap and recovery. This is an operational responsibility, not a hardcoded Windows name/SID, app user ID or automatic app permission. The command requires explicit interactive attestation plus a dedicated provisioning SQL connection with owner capability and no runtime-role membership. Actual host identity is recorded as provenance only. No host grants/security settings are changed by the program.
2. First administrator receives company-only OrganizationAdministrator authority. A1 accepts no deployment-grant flag and creates no platform assignment. DeploymentProvisioner remains a separate fixed role for later explicitly authorized assignment, never inherited from ownership of a company or the host.
3. Remote self-service recovery is out of scope. The supervised recovery contract is preserved, but A1 implements no reset/recovery command or HTTP endpoint.

Operator control is deliberately capability-based: possession of the separately protected provisioning credentials and responsible owner's explicit attestation are required. The software does not infer the responsible human from an OS name or automatically trust the web-process identity. Deployment must keep owner-level credentials away from the web process. This boundary is not a new runtime authorization provider.

## Established constraints

ASP.NET Core Identity, long actor IDs, Domain independence, default-deny server scope checks, positive trusted actor attribution, atomic business audit, HTTP/antiforgery cookie boundaries and current session/permission revalidation follow FF-A003–A006, FF-A015/016 and FF-A018 in ForgeFlow.md. Organization/Site ownership/lifecycle stays unchanged. Password/session cryptography is framework-owned. No self-registration or fake runtime principals.

## Proposed minimum policy

P-A1: Use a user-only Identity EF context/store, not a role administration subsystem. Framework claims/login/token support tables are not grant sources or external-login features. Use three immutable code-owned fixed roles; assignments are scoped and stored in SQL. No direct permission overrides or editable role permissions. This follows the shared role-as-permission-set rule without enabling full role management.

| Fixed role | Required assignment scope | Exact permissions |
|---|---|---|
| DeploymentProvisioner | Platform | Organizations.Create only |
| OrganizationAdministrator | Organization membership | Organizations.Read, Organizations.Update, Organizations.SetActive; Sites.Read, Sites.Create, Sites.Update, Sites.SetActive |
| SiteOperator | Site membership plus parent organization membership | Sites.Read, Sites.Update, Sites.SetActive at that site only |

A platform grant does not imply organization access; company administration does not imply tenant creation. An organization-level grant applies to its own sites for the listed operation; a site grant cannot grant organization-wide management, create sites or access siblings. Membership alone grants nothing. Multiple matching active role assignments union their fixed permissions; there are no deny/allow overrides. Unknown role, permission, scope or membership denies. Under the approved A1 decision the first administrator gets only OrganizationAdministrator; A1 provides no optional DeploymentProvisioner bootstrap choice.

P-A2: Eligibility is current SQL state, not stale claims. Require protected Identity ticket, positive user ID and matching session GUID; current active user; nonexpired/nonrevoked session belonging to that user; current security stamp matching the ticket/session. Proposed lockout policy denies new sign-ins and current protected operations while lockout is in effect. No valid membership is required just to sign in/read one's own preference/logout, but every Organization/Site operation requires its specific membership/grant. Scope evaluator verifies Site.OrganizationId against SQL; it never infers ownership from route IDs.

For normal writes require active organization/parent Site policy. Reads may access inactive records only under explicit matching read grants (history/lifecycle). Organizations.SetActive on the organization is the explicit inactive-company lifecycle exception; Sites.SetActive may target inactive Site only with an active parent and matching grant. These exceptions do not reactivate memberships or broaden other permissions.

P-A3: Fresh operation-scoped Identity contexts for eligibility reads, never circuit-held DbContexts. Every ITrustedActorAccessor call rechecks eligibility; IOrganizationPermissionEvaluator rechecks it again with membership/grant state. No positive authorization cache initially. Missing DB/adapter/session denies, with sanitized availability feedback. The circuit adapter reads server AuthenticationStateProvider state and resolves it against SQL, not user JSON or a stale captured HttpContext. Session GUID maps to ProvenanceReference; only the controlled host bootstrap path mints ControlledBootstrap provenance. Unknown claims cannot create host/bootstrap authority.

P-A4: Fixed eight-hour server session expiry, no sliding renewal or remember-me initially; nonpersistent browser cookie. HTTPS Secure/HttpOnly, SameSite=Lax, host-only Path=/ cookie (proposed `__Host-ForgeFlow.Auth`). Production requires valid TLS and durable protected Data Protection keys shared only by trusted instances. Local test TrustServerCertificate=True is not production TLS evidence. Cookie validation checks SQL each authenticated request. Circuit revalidation interval proposed 30 seconds while executing, plus resume/reconnect and every protected operation. Revocation affects the next operation even before visible UI cleanup.

P-A5: Cookie has framework user/security-stamp claims plus opaque session ID; no authoritative role/member list. Logout revokes current session. Disable, entering account lockout, password reset or global stamp rotation revokes all sessions; lifting lockout alone cannot make a revoked ticket usable. Membership or assignment revocation denies the next matching operation immediately without relying on a stamp refresh; proactive circuit checks clear the invalid selected scope and its drafts (other valid scopes may remain usable). Whole-account/session invalidation clears the principal. Once a scope is revoked, regrant does not silently restore its cleared unsaved draft.

Reserved user/session/stamp claims must come only from the ticket issuer; arbitrary UserClaim rows cannot override them. Reject missing, malformed or duplicate reserved references. Ignore role/permission claims for scope authorization even if an old ticket contains them.

P-A6: Identity username login, 1–128 ASCII characters from letters/digits and `._-@+`, normalized by the framework invariant normalizer and unique. Email is optional contact data, not a login/recovery authority; no email-confirmation dependency with supervised recovery. Display/business names are separate. Proposed password policy: 12–128 characters, passphrases allowed, no truncation, explicitly configured Identity validators and built-in password hasher; do not write a custom hash algorithm. Lockout: five failed attempts, fifteen minutes. Account lockout plus bounded sign-in endpoint throttling are tested with a deterministic clock; throttling is not an authorization grant. All numeric policy values are proposals, not claims about framework defaults.

P-A7: Security events and access mutations commit atomically where applicable. Session issuance/revocation, bootstrap, password reset and membership/assignment changes must persist their required events or fail the mutation. Unknown-user/failed sign-in events use nullable actor/subject, never fake user 0. If logging failed attempts/denials is unavailable, deny/return a generic availability failure and sanitized technical alert; do not permit a request to compensate. Logout still clears the browser cookie even if persistence fails and reports limited revocation. No password/hash/stamp/token/cookie/full request or raw attempted username in events. Audit/event visibility and retention administration are future features; automatic purge is not implemented here.

P-A8: Identity/security metadata uses its own minimum append-only SecurityEvent table. Never insert Identity event names into OrganizationChange's finite Organization/Site event set. Business audit keeps the actual user ActorId and correlation; session/host provenance is in a corresponding security event when relevant. Bootstrap shares the existing business audit transaction; ordinary OrganizationUseCases continue owning their transaction and audit. No modifications to OrganizationChange's schema or Domain Identity FK are proposed.

## Resolved input decisions (original alternatives retained for traceability)

Q-A1 and Q-A2 are answered by the approved A1 decisions above. No additional ownership or authority question blocks A1. The alternative suggestions in this table are historical design context, not an enabled command option.

| Decision | Recommendation | Consequence |
|---|---|---|
| Q-A1: Is a trusted host deployment operator available for initialization and supervised password recovery, including verification of the account owner's identity? | Yes: name the responsible deployment/operator group; restrict the host-only tool to that authority and protected provisioning credentials | Supports first-admin recovery without public setup/reset endpoints or email infrastructure. If remote self-service recovery is essential, its verified channel/provider and token delivery require a revised contract before implementation |
| Q-A2: Should the first company administrator also receive persistent deployment-level Organization creation authority? | No by default; grant OrganizationAdministrator only, with a separately explicit bootstrap option for a named DeploymentProvisioner | Keeps company and deployment authority distinct. If enabled, that user can create additional Organizations but still needs separate membership/role assignments to manage them; the grant is independently audited |

Other mapping/policy proposals are reviewable defaults, not additional business questions or implementation authorization. Deployment target/real schema inventory, protected operator identity and valid production TLS/key-storage configuration must be established before their implementation increments. No host ACL or production security settings are changed by this design.

## Framework references (primary sources)

- [Identity model customization](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0) documents the user-only Identity context option and configurable key model. The schema below is a proposal to map that store, not a custom password store.
- [Blazor authentication](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?view=aspnetcore-10.0) describes server authentication state and revalidation. Our proposed per-operation SQL checks and 30-second UI cadence are application policy, not framework defaults.
- [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) supports the HTTP form boundary. Login, logout and preference mutations must validate antiforgery even when no account is yet authenticated.
