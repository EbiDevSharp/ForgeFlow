using ForgeFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class Phase2EncryptionTests
{
    [Fact]
    public void DataProtectionPersonalDataProtector_roundtrips_is_idempotent_and_reads_legacy_plaintext()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = new DataProtectionPersonalDataProtector(provider);

        const string secret = "JBSWY3DPEHPK3PXP";
        var protectedValue = protector.Protect(secret);

        Assert.StartsWith("ff-dp:v1:", protectedValue, StringComparison.Ordinal);
        Assert.NotEqual(secret, protectedValue);
        Assert.Equal(protectedValue, protector.Protect(protectedValue));
        Assert.Equal(secret, protector.Unprotect(protectedValue));
        Assert.Equal(secret, protector.Unprotect(secret));
    }

    [Fact]
    public void AuthDbContext_has_no_pending_model_changes_with_personal_data_protector()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddRuntimeIdentity(o => o.UseSqlServer("Server=localhost;Database=ForgeFlow_ModelCheck;Integrated Security=true;TrustServerCertificate=True"));
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void AuthDbContext_registers_ValueConverter_on_UserToken_Value_when_protector_is_provided()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddRuntimeIdentity(o => o.UseSqlServer("Server=localhost;Database=ForgeFlow_ModelCheck;Integrated Security=true;TrustServerCertificate=True"));
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var tokenValueProp = db.Model.FindEntityType(typeof(IdentityUserToken<long>))?.FindProperty(nameof(IdentityUserToken<long>.Value));
        Assert.NotNull(tokenValueProp);
        var converter = tokenValueProp!.GetValueConverter();
        Assert.NotNull(converter);

        const string rawSecret = "OTP-SECRET-123456";
        var dbPayload = converter!.ConvertToProvider(rawSecret) as string;
        Assert.NotNull(dbPayload);
        Assert.StartsWith("ff-dp:v1:", dbPayload!, StringComparison.Ordinal);
        Assert.Equal(rawSecret, converter.ConvertFromProvider(dbPayload));
        Assert.Equal(rawSecret, converter.ConvertFromProvider(rawSecret));
    }
}
