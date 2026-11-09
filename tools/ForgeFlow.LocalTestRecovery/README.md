# Fixed local test-account recovery

Explicitly authorized local helper; not referenced by Web. It accepts no server, database, account or password overrides. Only Development, an interactive private terminal, `--execute`, RECOVER attestation and A2's existing host SQL capability permit execution. The fixed Windows-authenticated target is localhost/testforgflow, account ebrahim. Encrypt=True/TrustServerCertificate=True is restricted to that local target. No production recovery controls are modified.

From normal Windows PowerShell, run this compiled helper directly (no PowerShell script or execution-policy change):

```powershell
dotnet run --project D:\Projects\ForgeFlow\tools\ForgeFlow.LocalTestRecovery\ForgeFlow.LocalTestRecovery.csproj -c Release --no-build --no-restore --environment DOTNET_ENVIRONMENT=Development -- --execute
```

Confirm RECOVER after reviewing the printed fixed target/owner responsibility. The installed SDK supports --environment; it sets Development only for the child process. No manual SQL, account ID, password or connection arguments are needed. Do not enable terminal transcription/output capture. The helper's existing private-terminal and owner-capability controls still apply.

Before password generation or reset, the helper takes A2's account lock and resolves committed PasswordResetCompleted evidence for operation `80c2921b-bec8-4200-86af-bd16b522fc3a`. A marker for a different subject or duplicate completion refuses execution. Failure reference `5440c8d5-9637-4bad-87cb-c897227278e6` came from the original CLI's catch block; that correlation is not persisted by the CLI, so it cannot establish an underlying cause. The operation's atomic completion audit is the durable outcome evidence. If prior completion exists, a newly authorized reset still requires a new operation because the previous password cannot be recovered.

Only HostRecovery.ResetAsync mutates data. It retains its owner checks, schema guards, Identity token/password pipeline, transaction, stamp rotation, session revocation and audit. The helper generates a 28-character cryptographically random password in memory after resolving the prior operation. A fresh operation is used without automatic retries. Verification checks the committed audit, new password hash validity, stamp change, prior sessions' revocations/events and unchanged memberships/grants/eligibility. Only after all checks pass does the private terminal display the temporary password once. Passwords, hashes, stamps and reset tokens are never written to files or diagnostic logs.

Uncertain commit is resolved by checking the new operation and password, never replaying a reset automatically. Failed verification can mean a commit occurred: preserve the operation reference; no password is disclosed and no success is claimed. The helper does not migrate, initialize accounts, change grants or clear lockout/disabled states. A locked/disabled account remains ineligible after a successful password reset.
