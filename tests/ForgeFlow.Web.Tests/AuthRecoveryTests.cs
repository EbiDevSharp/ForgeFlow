using System.Globalization;
using ForgeFlow.Bootstrap;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthRecoveryTests
{
    [Fact]
    public async Task Real_reset_provider_binds_private_token_to_subject_stamp_and_operation_provider()
    {
        using var provider = HostRecovery.AddRecoveryIdentity(new ServiceCollection(), o => o.UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=True;Encrypt=True")).BuildServiceProvider();
        using var scope = provider.CreateScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>();
        var user = new ApplicationAccount { Id = 42, UserName = "synthetic.owner", SecurityStamp = Guid.NewGuid().ToString() };
        var originalStamp = user.SecurityStamp;
        var token = await manager.GeneratePasswordResetTokenAsync(user);
        Assert.True(await manager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, UserManager<ApplicationAccount>.ResetPasswordTokenPurpose, token));
        user.SecurityStamp = Guid.NewGuid().ToString();
        Assert.False(await manager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, UserManager<ApplicationAccount>.ResetPasswordTokenPurpose, token));
        user.SecurityStamp = originalStamp; user.Id = 43;
        Assert.False(await manager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, UserManager<ApplicationAccount>.ResetPasswordTokenPurpose, token));
        // Token generation/validation only; password persistence is proven by the SQL suite.
    }
    [Fact]
    public async Task Recovery_requires_its_own_explicit_host_attestation_before_connection()
    {
        Assert.Null(RecoveryOperatorConsent.Confirm(null));
        Assert.Null(RecoveryOperatorConsent.Confirm("INITIALIZE"));
        Assert.Null(RecoveryOperatorConsent.Confirm("recover"));
        Assert.NotNull(RecoveryOperatorConsent.Confirm("RECOVER"));
        Assert.Equal("Auth.OperatorRequired", (await new HostRecovery("invalid").ResetAsync(new(Guid.NewGuid(), 1), AuthBootstrapTests.Password(), null)).Code);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("subject")]
    [InlineData("short")]
    [InlineData("long")]
    public async Task Invalid_recovery_inputs_are_rejected_without_sql(string invalid)
    {
        var password = invalid == "short" ? "" : invalid == "long" ? new string('x',129) : AuthBootstrapTests.Password();
        var request = new RecoveryRequest(invalid == "operation" ? Guid.Empty : Guid.NewGuid(), invalid == "subject" ? 0 : 1);
        Assert.Equal("Auth.ResetRejected", (await new HostRecovery("invalid").ResetAsync(request, password, RecoveryOperatorConsent.Confirm("RECOVER"))).Code);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    public void Recovery_prompts_are_localized(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var provider = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
            var text = provider.GetRequiredService<IStringLocalizer<BootstrapText>>();
            foreach (var key in new[] { "RecoveryConnectionRequired", "RecoveryResponsibility", "RecoveryConfirmation", "RecoveryUserId" })
            { Assert.False(text[key].ResourceNotFound); Assert.DoesNotContain("?", text[key].Value); }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void Guard_allows_identity_updates_with_unchanged_username_and_blocks_username_changes()
    {
        var guard = typeof(AuthDbContext).GetMethod("Guard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        using var db = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer("Server=invalid.invalid;Database=DesignOnly;Integrated Security=True;Encrypt=True").Options);
        var user = new ApplicationAccount { Id = 42, UserName = "ebrahim", NormalizedUserName = "EBRAHIM", PasswordHash = "h", SecurityStamp = "s", ConcurrencyStamp = "c" };
        db.Attach(user);
        // Simulate Identity UserOnlyStore.UpdateAsync marking all properties modified without changing UserName.
        db.Entry(user).State = EntityState.Modified;
        user.AccessFailedCount = 1;
        guard.Invoke(db, null);

        user.UserName = "other";
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => guard.Invoke(db, null));
        Assert.Equal("Auth.ImmutableHistory", ex.InnerException!.Message);
    }
}
