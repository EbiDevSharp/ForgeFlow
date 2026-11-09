# Create one local ORG1 test login

This is account creation, not recovery or initialization. It never modifies ebrahim or calls HostRecovery/HostBootstrapper. It reuses the existing Identity registration, UserManager.CreateAsync/CheckPasswordAsync, fixed company role and local helper password generator. No Web endpoint or runtime allow-all adapter is added.

Run this one command in a private normal Windows PowerShell terminal:

```powershell
dotnet run --project D:\Projects\ForgeFlow\tools\ForgeFlow.LocalTestAccount\ForgeFlow.LocalTestAccount.csproj -c Release --no-build --no-restore --environment DOTNET_ENVIRONMENT=Development -- --execute
```

Review the target and type CREATE TEST ACCOUNT. No manual SQL, existing account password, username, numeric ID or connection input is needed. Do not enable transcription or redirect output. Credentials appear once, only after commit and fresh verification. On failure no credential is disclosed; preserve the operation reference because a lost commit acknowledgement may still mean creation occurred.

Fixed target: localhost/testforgflow with Windows authentication, Encrypt=True and local-only TrustServerCertificate=True. The compiled host-only service accepts no target overrides. The executable requires explicit --execute, Development and an interactive private terminal. SQL must grant the real host operator dbo/db_owner capability and exclude the two runtime roles; no permissions are auto-granted to the operator.

An active existing ORG1 and completed initialization/schema are required. The transaction takes an update/serializable lock on ORG1, creates a GUID-derived unique username with a fresh 28-character cryptographic password through Identity, then creates exactly one active OrganizationMembership and one active company OrganizationAdministrator assignment (ScopeKind=1, SiteId=NULL). It adds AccountCreated/MembershipChanged/ScopedRoleChanged host-attributed audit events in the same transaction. The new subject ID supplies the existing host-provisioning membership metadata convention; the host audit explicitly records actual operator attribution. No platform authority, site membership, deployment role, organization/site creation, migration or existing-account write occurs. Existing accounts are never adopted on name collision.

Fresh verification requires active account, Identity password success, active ORG1, exactly the intended membership and grant, no site membership/extra grants, and all three committed audit records. A lost commit response is checked without replaying creation. The console prints no raw exceptions, passwords, hashes, tokens or connection secrets in diagnostics. Actual HTTP cookie sign-in is a separate live gate at https://localhost:5001/account/login; never put the generated password into arguments or evidence.
