using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Identity.Configurations.AuthModelConventions;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal sealed class UserPasskeyConfiguration : IEntityTypeConfiguration<UserPasskey>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UserPasskey> passkey)
    {
        passkey.ToTable("UserPasskey", Schema, t =>
        {
            t.HasCheckConstraint("CK_UserPasskey_Algo", "[Algorithm] IN (-7, -257)");
            t.HasCheckConstraint("CK_UserPasskey_SignCount", "[SignCount] >= 0");
            t.HasCheckConstraint("CK_UserPasskey_Name", "LEN([DisplayName]) BETWEEN 1 AND 100");
        });
        passkey.HasKey(p => p.Id);
        passkey.Property(p => p.Id).UseIdentityColumn();
        passkey.Property(p => p.CredentialId).HasMaxLength(512).IsRequired();
        passkey.Property(p => p.PublicKeyCose).HasMaxLength(1024).IsRequired();
        passkey.Property(p => p.DisplayName).HasMaxLength(100).IsRequired();
        passkey.Property(p => p.CreatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        passkey.Property(p => p.LastUsedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        passkey.Property(p => p.RevokedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        passkey.HasOne<ApplicationAccount>().WithMany().HasForeignKey(p => p.UserId);
        passkey.HasIndex(p => p.CredentialId).IsUnique().HasDatabaseName("UX_UserPasskey_CredentialId");
        passkey.HasIndex(p => new { p.UserId, p.RevokedAtUtc });
        Version(passkey);
    }
}
