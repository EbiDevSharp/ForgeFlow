using System.Globalization;
using System.Text;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.Bootstrap;

public sealed class BootstrapText;

public static class BootstrapProgram
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        using var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
        var text = services.GetRequiredService<IStringLocalizer<BootstrapText>>();
        if (args.Length != 1 || args[0] is not ("initialize-deployment" or "reset-password")) { Console.Error.WriteLine(text["Usage"]); return 2; }
        var recovery = args[0] == "reset-password";
        if (Console.IsInputRedirected || Console.IsOutputRedirected) { Console.Error.WriteLine(text["InteractiveRequired"]); return 2; }
        var connection = Environment.GetEnvironmentVariable(recovery ? "FORGEFLOW_RECOVERY_SQLSERVER" : "FORGEFLOW_BOOTSTRAP_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection)) { Console.Error.WriteLine(text[recovery ? "RecoveryConnectionRequired" : "ConnectionRequired"]); return 2; }
        var correlation = Guid.NewGuid();
        try
        {
            var target = new SqlConnectionStringBuilder(connection);
            Console.WriteLine(text["Target", target.DataSource, target.InitialCatalog]);
            Console.WriteLine(text[recovery ? "RecoveryResponsibility" : "OperatorResponsibility"]);
            Console.Write(text[recovery ? "RecoveryConfirmation" : "Confirmation"]);
            var attestation = Console.ReadLine();
            var consent = recovery ? null : HostOperatorConsent.Confirm(attestation);
            var recoveryConsent = recovery ? RecoveryOperatorConsent.Confirm(attestation) : null;
            if (consent is null && recoveryConsent is null) { Console.Error.WriteLine(text["Cancelled"]); return 2; }
            string Read(string key) { Console.Write(text[key]); return Console.ReadLine() ?? ""; }
            var operationRaw = Read("OperationId");
            var operationId = operationRaw.Length == 0 ? Guid.NewGuid() : Guid.Parse(operationRaw);
            Console.WriteLine(text["OperationReference", operationId]);
            var userId = recovery ? long.Parse(Read("RecoveryUserId"), CultureInfo.InvariantCulture) : 0;
            var request = recovery ? null : new BootstrapRequest(operationId, Read("UserName"), Read("OrganizationCode"), Read("OrganizationName"), Read("SiteCode"), Read("SiteName"));
            Console.Write(text["Password"]); var password = ReadHidden();
            Console.Write(text["PasswordAgain"]); var confirmation = ReadHidden();
            if (password != confirmation) { Console.Error.WriteLine(text["PasswordMismatch"]); return 2; }
            if (recovery)
            {
                var recovered = await new HostRecovery(connection).ResetAsync(new(operationId, userId), password, recoveryConsent);
                Console.WriteLine(text["Result", recovered.Code, recovered.OperationId]);
                return recovered.Code is "Auth.PasswordResetCompleted" or "Auth.AlreadyRecovered" ? 0 : 1;
            }
            var result = await new HostBootstrapper(connection).InitializeAsync(request!, password, consent);
            Console.WriteLine(text["Result", result.Code, result.OperationId]);
            if (result.IdentityErrorCodes is not null)
                foreach (var code in result.IdentityErrorCodes) Console.Error.WriteLine(code); // Safe technical codes only.
            return result.Code is "Auth.Initialized" or "Auth.AlreadyInitialized" ? 0 : 1;
        }
        catch (Exception)
        {
            // SQL/Identity exceptions may contain private data. Never print raw messages, args or credentials.
            Console.Error.WriteLine(text["Failure", correlation]); return 1;
        }
    }

    private static string ReadHidden()
    {
        var buffer = new StringBuilder(128);
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; continue; }
            if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException();
            if (!char.IsControl(key.KeyChar))
            {
                if (buffer.Length >= 128) throw new InvalidOperationException("Auth.PasswordTooLong");
                buffer.Append(key.KeyChar);
            }
        }
    }
}
