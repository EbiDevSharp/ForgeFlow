using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Identity.Configurations.AuthModelConventions;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal sealed class ApplicationAccountConfiguration : IEntityTypeConfiguration<ApplicationAccount>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<ApplicationAccount> user)
    {
        user.ToTable("User", Schema, t =>
        {
            t.HasCheckConstraint("CK_User_Name", "DATALENGTH([UserName]) BETWEEN 2 AND 256 AND DATALENGTH([UserName]) = 2 * LEN([UserName]) AND [UserName] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Za-z0-9._@+-]%' AND DATALENGTH([NormalizedUserName]) = DATALENGTH([UserName]) AND [NormalizedUserName] COLLATE Latin1_General_100_BIN2 = UPPER([UserName] COLLATE Latin1_General_100_BIN2)");
            t.HasCheckConstraint("CK_User_Culture", "[PreferredCulture] IS NULL OR (DATALENGTH([PreferredCulture]) = 5 AND [PreferredCulture] COLLATE Latin1_General_100_BIN2 IN ('fa-IR','en-US'))");
            t.HasCheckConstraint("CK_User_Avatar", "([AvatarContentType] IS NULL AND [AvatarData] IS NULL) OR ([AvatarContentType] IN ('image/png','image/jpeg','image/webp') AND [AvatarData] IS NOT NULL AND DATALENGTH([AvatarData]) BETWEEN 1 AND 262144)");
            t.HasCheckConstraint("CK_User_Metadata", "[AccessFailedCount] >= 0 AND ([UpdatedAtUtc] IS NULL OR [UpdatedAtUtc] >= [CreatedAtUtc]) AND ([LockoutEnd] IS NULL OR DATEPART(TZOFFSET,[LockoutEnd]) = 0)");
        });
        user.Property(u => u.Id).UseIdentityColumn();
        user.Property(u => u.UserName).HasMaxLength(128).IsRequired();
        user.Property(u => u.NormalizedUserName).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        user.HasIndex(u => u.NormalizedUserName).IsUnique().HasFilter(null).HasDatabaseName("UX_User_NormalizedUserName");
        user.Property(u => u.Email).HasMaxLength(256);
        user.Property(u => u.NormalizedEmail).HasMaxLength(256);
        user.Property(u => u.PhoneNumber).HasMaxLength(64);
        user.Property(u => u.DisplayName).HasMaxLength(128);
        user.Property(u => u.AvatarContentType).HasMaxLength(32).IsUnicode(false);
        user.Property(u => u.AvatarData).HasColumnType("varbinary(max)");
        user.Property(u => u.LockoutEnd).HasColumnType("datetimeoffset(7)");
        user.Property(u => u.PasswordHash).IsRequired();
        user.Property(u => u.SecurityStamp).HasMaxLength(256).IsRequired();
        user.Property(u => u.ConcurrencyStamp).HasMaxLength(256).IsRequired().IsConcurrencyToken();
        user.Property(u => u.PreferredCulture).HasMaxLength(5).IsUnicode(false);
        user.Property(u => u.CreatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        user.Property(u => u.UpdatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        user.Property(u => u.PasswordChangedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        user.Property(u => u.TwoFactorEnabledAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        Version(user);
    }
}

internal sealed class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<long>>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<long>> claim)
    {
        claim.ToTable("UserClaim", Schema);
        claim.Property(c => c.ClaimType).HasMaxLength(256);
    }
}

internal sealed class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<long>>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<long>> login)
    {
        login.ToTable("UserLogin", Schema);
        login.Property(c => c.LoginProvider).HasMaxLength(128);
        login.Property(c => c.ProviderKey).HasMaxLength(128);
        login.Property(c => c.ProviderDisplayName).HasMaxLength(256);
    }
}

internal sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<long>>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<long>> token)
    {
        token.ToTable("UserToken", Schema);
        token.Property(c => c.LoginProvider).HasMaxLength(128);
        token.Property(c => c.Name).HasMaxLength(128);
    }
}
