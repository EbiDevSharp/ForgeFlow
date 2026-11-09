using ForgeFlow.LocalTestRecovery;
using Microsoft.Data.SqlClient;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class LocalTestRecoveryTests
{
    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void Helper_feedback_is_localized_without_missing_resource_keys(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
            var localizer = services.GetRequiredService<IStringLocalizer<RecoveryText>>();
            string[] keys = ["ExecutionRequired", "Target", "Consent", "Cancelled", "PreviousCommitted", "PreviousNotCommitted", "Operation", "Result", "Verified", "TemporaryPassword", "Unconfirmed", "Failure"];
            Assert.All(keys, key => { Assert.False(localizer[key].ResourceNotFound); Assert.False(string.IsNullOrWhiteSpace(localizer[key].Value)); });
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
    [Fact]
    public void Execution_rejects_production_redirection_and_all_target_overrides()
    {
        Assert.True(LocalTestRecoveryProgram.ExecutionAllowed(["--execute"], "Development", false, false));
        Assert.False(LocalTestRecoveryProgram.ExecutionAllowed([], "Development", false, false));
        Assert.False(LocalTestRecoveryProgram.ExecutionAllowed(["--execute"], "Production", false, false));
        Assert.False(LocalTestRecoveryProgram.ExecutionAllowed(["--execute"], "Development", true, false));
        Assert.False(LocalTestRecoveryProgram.ExecutionAllowed(["--execute"], "Development", false, true));
        Assert.False(LocalTestRecoveryProgram.ExecutionAllowed(["--execute", "--server", "other"], "Development", false, false));
        var connection = new SqlConnectionStringBuilder(LocalTestRecoveryProgram.Connection);
        Assert.Equal("localhost", connection.DataSource);
        Assert.Equal("testforgflow", connection.InitialCatalog);
        Assert.True(connection.IntegratedSecurity);
        Assert.True(connection.TrustServerCertificate);
    }

    [Fact]
    public void Temporary_passwords_have_all_required_character_groups_and_fresh_entropy()
    {
        var generated = Enumerable.Range(0, 64).Select(_ => LocalTestRecoveryProgram.GenerateTemporaryPassword()).ToArray();
        Assert.Equal(generated.Length, generated.Distinct().Count());
        Assert.All(generated, password =>
        {
            Assert.Equal(28, password.Length);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, character => !char.IsLetterOrDigit(character));
        });
    }
}
