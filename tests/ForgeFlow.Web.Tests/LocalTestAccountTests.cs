using System.Globalization;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.LocalTestAccount;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class LocalTestAccountTests
{
    [Fact]
    public async Task Missing_host_consent_denies_creation_before_sql()
    {
        Assert.Null(LocalTestAccountConsent.Confirm("RECOVER"));
        Assert.Null(LocalTestAccountConsent.Confirm("INITIALIZE"));
        var result = await new HostLocalTestAccountCreator().CreateAsync(Guid.NewGuid(), "", null);
        Assert.Equal("Auth.OperatorRequired", result.Code);
        Assert.Null(result.UserId);
    }

    [Fact]
    public async Task Invalid_requests_deny_before_sql_and_target_is_fixed_local()
    {
        var consent = LocalTestAccountConsent.Confirm("CREATE TEST ACCOUNT");
        Assert.NotNull(consent);
        Assert.Equal("Auth.IdentityRejected", (await new HostLocalTestAccountCreator().CreateAsync(Guid.Empty, "", consent)).Code);
        var target = new SqlConnectionStringBuilder(HostLocalTestAccountCreator.Connection);
        Assert.Equal("localhost", target.DataSource); Assert.Equal("testforgflow", target.InitialCatalog);
        Assert.True(target.IntegratedSecurity); Assert.True(target.TrustServerCertificate);
    }

    [Fact]
    public void Account_names_are_stable_per_operation_unique_across_operations_and_identity_compatible()
    {
        var operation = Guid.NewGuid();
        var name = HostLocalTestAccountCreator.UserNameFor(operation);
        Assert.Equal(name, HostLocalTestAccountCreator.UserNameFor(operation));
        Assert.NotEqual(name, HostLocalTestAccountCreator.UserNameFor(Guid.NewGuid()));
        Assert.Matches("^localtest-[a-f0-9]{32}$", name);
        Assert.NotEqual("ebrahim", name);
        Assert.InRange(name.Length, 1, 128);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    public void Account_helper_resources_are_complete(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
            var text = services.GetRequiredService<IStringLocalizer<AccountText>>();
            string[] keys = ["ExecutionRequired", "Target", "Consent", "Cancelled", "Operation", "Result", "Verified", "UserName", "Password", "Unconfirmed", "Failure"];
            Assert.All(keys, key => Assert.False(text[key].ResourceNotFound));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
