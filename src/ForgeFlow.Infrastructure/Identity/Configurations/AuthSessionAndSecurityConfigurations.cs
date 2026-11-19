using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Identity.Configurations.AuthModelConventions;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal sealed class AccountSessionConfiguration : IEntityTypeConfiguration<AccountSession>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<AccountSession> session)
    {
        session.ToTable("Session", Schema, t => t.HasCheckConstraint("CK_Session_Lifecycle", "[ExpiresAtUtc] > [IssuedAtUtc] AND [LastActivityAtUtc] >= [IssuedAtUtc] AND (([RevokedAtUtc] IS NULL AND [RevokeReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedAtUtc] >= [IssuedAtUtc] AND [RevokeReason] IS NOT NULL AND DATALENGTH([RevokeReason]) = LEN([RevokeReason]) AND [RevokeReason] COLLATE Latin1_General_100_BIN2 IN ('Logout','PasswordReset','AccountDisabled','AccountLocked','StampChanged','SessionRevokedByUser','IdleTimeout','TwoFactorReset','AdminRevoked')))"));
        session.HasKey(s => s.Id);
        session.Property(s => s.Id).ValueGeneratedNever();
        session.Property(s => s.SecurityStampSnapshot).HasMaxLength(256).IsRequired();
        session.Property(s => s.IpAddress).HasMaxLength(64).IsUnicode(false);
        session.Property(s => s.DeviceSummary).HasMaxLength(128);
        session.Property(s => s.DeviceTokenHash).HasMaxLength(64).IsUnicode(false);
        session.Property(s => s.RevokeReason).HasMaxLength(32).IsUnicode(false);
        session.Property(s => s.IssuedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        session.Property(s => s.LastActivityAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        session.Property(s => s.ExpiresAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        session.Property(s => s.RevokedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        session.HasOne<ApplicationAccount>().WithMany().HasForeignKey(s => s.UserId);
        session.HasIndex(s => new { s.UserId, s.RevokedAtUtc, s.ExpiresAtUtc });
        session.HasIndex(s => new { s.UserId, s.DeviceTokenHash });
        Version(session);
    }
}

internal sealed class BootstrapStateConfiguration : IEntityTypeConfiguration<BootstrapState>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<BootstrapState> state)
    {
        state.ToTable("BootstrapState", Schema, t =>
        {
            t.HasCheckConstraint("CK_Bootstrap_Singleton", "[Id] = 1");
            t.HasCheckConstraint("CK_Bootstrap_State", "([State]=0 AND [OperationId] IS NULL AND [CompletedAtUtc] IS NULL AND [FirstUserId] IS NULL AND [OrganizationId] IS NULL AND [SiteId] IS NULL AND [HostOperatorReference] IS NULL) OR ([State]=1 AND [OperationId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [FirstUserId] IS NOT NULL AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL AND [HostOperatorReference] IS NOT NULL)");
        });
        state.HasKey(s => s.Id);
        state.Property(s => s.Id).ValueGeneratedNever();
        state.Property(s => s.HostOperatorReference).HasMaxLength(256);
        state.Property(s => s.CompletedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        state.HasOne<ApplicationAccount>().WithMany().HasForeignKey(s => s.FirstUserId);
        state.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId);
        state.HasOne<Site>().WithMany().HasForeignKey(s => new { s.OrganizationId, s.SiteId }).HasPrincipalKey(s => new { s.OrganizationId, s.Id });
        state.HasIndex(s => s.OperationId).IsUnique().HasFilter("[OperationId] IS NOT NULL");
        state.HasData(new { Id = (byte)1, State = (byte)0 });
        Version(state);
    }
}

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<SecurityEvent> security)
    {
        security.ToTable("SecurityEvent", Schema, t =>
        {
            t.HasCheckConstraint("CK_SecurityEvent_Actor", "([ActorKind]=0 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=1 AND [ActorUserId] IS NOT NULL AND [HostOperatorReference] IS NULL) OR ([ActorKind]=2 AND [ActorUserId] IS NULL AND [HostOperatorReference] IS NOT NULL AND LEN([HostOperatorReference]) > 0)");
            t.HasCheckConstraint("CK_SecurityEvent_Data", "ISJSON([DataJson])=1 AND [Outcome] IN (0,1,2) AND ([SiteId] IS NULL OR [OrganizationId] IS NOT NULL)");
            t.HasCheckConstraint("CK_SecurityEvent_Code", "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('BootstrapCompleted','AccountCreated','SignInFailed','AccountLocked','SessionIssued','SessionRevoked','AccessDenied','PasswordResetCompleted','AccountEligibilityChanged','MembershipChanged','ScopedRoleChanged','RoleDefinitionChanged','DirectPermissionChanged','CulturePreferenceChanged','PreferencesUpdated','ProfileUpdated','SessionRevokedByUser','OtherSessionsRevoked','ActiveSiteSwitched','TwoFactorEnabled','TwoFactorDisabled','RecoveryCodesGenerated','RecoveryCodeRedeemed','IdleTimeoutUpdated','DeviceTrusted','PasskeyRegistered','PasskeyRemoved','PasskeySignIn','TwoFactorReset','SessionsRevokedByAdmin','AccountLockoutCleared')");
        });
        security.HasKey(e => e.Id);
        security.Property(e => e.Id).UseIdentityColumn();
        security.Property(e => e.EventCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        security.Property(e => e.HostOperatorReference).HasMaxLength(256);
        security.Property(e => e.DataJson).HasMaxLength(2000).IsRequired();
        security.Property(e => e.OccurredAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        security.HasOne<ApplicationAccount>().WithMany().HasForeignKey(e => e.ActorUserId);
        security.HasOne<ApplicationAccount>().WithMany().HasForeignKey(e => e.SubjectUserId);
        security.HasOne<AccountSession>().WithMany().HasForeignKey(e => e.SessionId);
        security.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId);
        security.HasOne<Site>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.SiteId }).HasPrincipalKey(s => new { s.OrganizationId, s.Id });
        security.HasIndex(e => new { e.OccurredAtUtc, e.Id });
        security.HasIndex(e => new { e.SubjectUserId, e.OccurredAtUtc, e.Id });
        security.HasIndex(e => e.CorrelationId);
        security.HasIndex(e => new { e.OrganizationId, e.OccurredAtUtc, e.Id });
    }
}
