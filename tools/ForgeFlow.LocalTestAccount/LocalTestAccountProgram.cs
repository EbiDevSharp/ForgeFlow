using System.Globalization;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.LocalTestRecovery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.LocalTestAccount;

public sealed class AccountText;

public static class LocalTestAccountProgram
{
    public static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        using var catalogs = new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
        var text = catalogs.GetRequiredService<IStringLocalizer<AccountText>>();
        if (!LocalTestRecoveryProgram.ExecutionAllowed(args, Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), Console.IsInputRedirected, Console.IsOutputRedirected))
        { Console.Error.WriteLine(text["ExecutionRequired"]); return 2; }
        Console.WriteLine(text["Target"]); Console.Write(text["Consent"]);
        var consent = LocalTestAccountConsent.Confirm(Console.ReadLine());
        if (consent is null) { Console.Error.WriteLine(text["Cancelled"]); return 2; }
        var operation = Guid.NewGuid(); Console.WriteLine(text["Operation", operation]);
        var stage = "create-local-test-account";
        try
        {
            var password = LocalTestRecoveryProgram.GenerateTemporaryPassword(); // Reuse generator, never invoke recovery.
            var created = await new HostLocalTestAccountCreator().CreateAsync(operation, password, consent);
            Console.WriteLine(text["Result", created.Code]);
            if (created.Code is not ("Auth.LocalTestAccountCreated" or "Auth.LocalTestAccountOutcomeUnknown")) return 1;
            stage = "verify-committed-account";
            using var services = new ServiceCollection().AddBootstrapIdentity(options => options.UseSqlServer(HostLocalTestAccountCreator.Connection,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))).BuildServiceProvider();
            await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var account = await db.Users.SingleAsync(user => user.Id == created.UserId && user.UserName == created.UserName);
            var organization = await db.Set<Organization>().AsNoTracking().SingleAsync(org => org.Id == created.OrganizationId && org.Code == "ORG1");
            var memberships = await db.OrganizationMemberships.AsNoTracking().Where(member => member.UserId == account.Id).ToListAsync();
            var grants = await db.ScopedRoleAssignments.AsNoTracking().Where(grant => grant.UserId == account.Id).ToListAsync();
            var events = await db.SecurityEvents.AsNoTracking().Where(audit => audit.SubjectUserId == account.Id && audit.CorrelationId == operation).ToListAsync();
            var verified = account.IsActive && account.LockoutEnd is null && account.AccessFailedCount == 0 && organization.IsActive
                && await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationAccount>>().CheckPasswordAsync(account, password)
                && memberships.Count == 1 && memberships[0].OrganizationId == organization.Id && memberships[0].IsActive
                && grants.Count == 1 && grants[0].OrganizationId == organization.Id && grants[0].SiteId is null
                && grants[0].ScopeKind == 1 && grants[0].RoleId == FixedAccessRoles.OrganizationAdministrator && grants[0].IsActive
                && !await db.SiteMemberships.AnyAsync(member => member.UserId == account.Id)
                && events.Count == 3 && events.All(audit => audit.ActorKind == 2 && audit.OrganizationId == organization.Id)
                && new[] { "AccountCreated", "MembershipChanged", "ScopedRoleChanged" }.All(code => events.Count(audit => audit.EventCode == code) == 1);
            if (!verified) { Console.Error.WriteLine(text["Unconfirmed", operation]); return 1; }
            Console.WriteLine(text["Verified"]);
            Console.WriteLine(text["UserName", account.UserName!]);
            Console.WriteLine(text["Password", password]); // Sole private-terminal credential disclosure, after verified commit.
            return 0;
        }
        catch (Exception exception)
        { Console.Error.WriteLine(text["Failure", stage, operation, exception.GetType().Name]); return 1; }
    }
}
