using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Identity.Configurations.AuthModelConventions;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UserPreference> preference)
    {
        preference.ToTable("UserPreference", Schema, t =>
        {
            t.HasCheckConstraint("CK_UserPreference_Language", "DATALENGTH([Language]) = 5 AND [Language] COLLATE Latin1_General_100_BIN2 IN ('fa-IR','en-US')");
            t.HasCheckConstraint("CK_UserPreference_Theme", "[Theme] COLLATE Latin1_General_100_BIN2 IN ('light','dark','system')");
            t.HasCheckConstraint("CK_UserPreference_Calendar", "[CalendarSystem] COLLATE Latin1_General_100_BIN2 IN ('persian','gregorian')");
            t.HasCheckConstraint("CK_UserPreference_TimeZone", "LEN([TimeZoneId]) BETWEEN 1 AND 64");
            t.HasCheckConstraint("CK_UserPreference_ActiveScope", "([ActiveSiteId] IS NULL OR [ActiveOrganizationId] IS NOT NULL)");
            t.HasCheckConstraint("CK_UserPreference_IdleTimeout", "[IdleTimeoutMinutes] IS NULL OR [IdleTimeoutMinutes] IN (15,30,60)");
            t.HasCheckConstraint("CK_UserPreference_Metadata", "[UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        preference.HasKey(p => p.UserId);
        preference.Property(p => p.Language).HasMaxLength(5).IsUnicode(false).IsRequired();
        preference.Property(p => p.Theme).HasMaxLength(16).IsUnicode(false).IsRequired();
        preference.Property(p => p.CalendarSystem).HasMaxLength(16).IsUnicode(false).IsRequired();
        preference.Property(p => p.TimeZoneId).HasMaxLength(64).IsUnicode(false).IsRequired();
        preference.Property(p => p.TwoFactorReminderSnoozedUntilUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        preference.Property(p => p.TwoFactorOneTimeNotifiedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        preference.Property(p => p.CreatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        preference.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        preference.HasOne<ApplicationAccount>().WithOne().HasForeignKey<UserPreference>(p => p.UserId);
        preference.HasOne<Organization>().WithMany().HasForeignKey(p => p.ActiveOrganizationId);
        preference.HasOne<Site>().WithMany().HasForeignKey(p => new { p.ActiveOrganizationId, p.ActiveSiteId }).HasPrincipalKey(s => new { s.OrganizationId, s.Id });
        Version(preference);
    }
}

internal sealed class UserNotificationPreferenceConfiguration : IEntityTypeConfiguration<UserNotificationPreference>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UserNotificationPreference> notifPref)
    {
        notifPref.ToTable("UserNotificationPreference", Schema, t =>
        {
            t.HasCheckConstraint("CK_UserNotificationPreference_EventCode", "DATALENGTH([EventCode])=LEN([EventCode]) AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempt','RoleOrAccessChanged','MyProductionOrders')");
            t.HasCheckConstraint("CK_UserNotificationPreference_Metadata", "[UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        notifPref.HasKey(p => new { p.UserId, p.EventCode });
        notifPref.Property(p => p.EventCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        notifPref.Property(p => p.CreatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        notifPref.Property(p => p.UpdatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        notifPref.HasOne<ApplicationAccount>().WithMany().HasForeignKey(p => p.UserId);
        Version(notifPref);
    }
}

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UserNotification> notification)
    {
        notification.ToTable("UserNotification", Schema, t =>
        {
            t.HasCheckConstraint("CK_UserNotification_Category", "[Category] IN (1, 2)");
            t.HasCheckConstraint("CK_UserNotification_Severity", "[Severity] IN (1, 2, 3, 4)");
            t.HasCheckConstraint("CK_UserNotification_Template", "DATALENGTH([TemplateCode])=LEN([TemplateCode]) AND [TemplateCode] COLLATE Latin1_General_100_BIN2 IN ('NewDeviceSignIn','FailedSignInAttempts','RoleOrAccessChanged','MyProductionOrderStatusChanged','PasswordChangedAlert','TwoFactorReminder','TwoFactorResetAlert')");
            t.HasCheckConstraint("CK_UserNotification_Params", "ISJSON([ParamsJson]) = 1");
            t.HasCheckConstraint("CK_UserNotification_ReadAt", "[ReadAtUtc] IS NULL OR [ReadAtUtc] >= [CreatedAtUtc]");
        });
        notification.HasKey(n => n.Id);
        notification.Property(n => n.Id).UseIdentityColumn();
        notification.Property(n => n.TemplateCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        notification.Property(n => n.ParamsJson).HasMaxLength(1000).IsRequired();
        notification.Property(n => n.LinkUrl).HasMaxLength(256);
        notification.Property(n => n.CreatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        notification.Property(n => n.ReadAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        notification.HasOne<ApplicationAccount>().WithMany().HasForeignKey(n => n.UserId);
        notification.HasIndex(n => new { n.UserId, n.ReadAtUtc, n.CreatedAtUtc, n.Id });
        Version(notification);
    }
}
