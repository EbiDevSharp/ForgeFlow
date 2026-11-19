using System.Data;
using System.Text;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Setup;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("==================================================================");
        Console.WriteLine("     ForgeFlow - Database Setup & Migration Utility");
        Console.WriteLine("==================================================================");
        Console.WriteLine();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ForgeFlow");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var connArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(connArg))
            {
                connectionString = connArg;
            }
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        }

        var csBuilder = new SqlConnectionStringBuilder(connectionString);
        var targetDatabase = string.IsNullOrWhiteSpace(csBuilder.InitialCatalog) ? "testforgflow" : csBuilder.InitialCatalog;
        var serverName = string.IsNullOrWhiteSpace(csBuilder.DataSource) ? "localhost" : csBuilder.DataSource;

        Console.WriteLine($"[1/5] Checking SQL Server connection at '{serverName}'...");

        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 10
        };

        try
        {
            await using var masterConn = new SqlConnection(masterBuilder.ConnectionString);
            await masterConn.OpenAsync();
            Console.WriteLine(" [OK] SQL Server connection established successfully.");

            Console.WriteLine($"[2/5] Checking database '{targetDatabase}'...");
            await using var checkCmd = masterConn.CreateCommand();
            checkCmd.CommandText = $@"
                IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @dbName)
                BEGIN
                    CREATE DATABASE [{targetDatabase}];
                END";
            checkCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = targetDatabase });
            await checkCmd.ExecuteNonQueryAsync();
            Console.WriteLine($" [OK] Database '{targetDatabase}' is ready.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] Unable to connect to SQL Server:");
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine("\nTroubleshooting tips:");
            Console.WriteLine(" 1. Ensure SQL Server service (MSSQLSERVER or SQLEXPRESS) is started in Windows Services.");
            Console.WriteLine(" 2. If using a named instance (e.g. SQLEXPRESS), set ConnectionStrings__ForgeFlow:");
            Console.WriteLine(@"    set ""ConnectionStrings__ForgeFlow=Server=localhost\SQLEXPRESS;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;""");
            Console.ResetColor();
            return 1;
        }

        var targetConnString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = targetDatabase,
            ConnectTimeout = 15
        }.ConnectionString;

        Console.WriteLine("[3/5] Applying database migrations (3 schemas)...");
        try
        {
            // 1. Diagnostics schema
            Console.Write(" - Applying DiagnosticsDbContext migrations... ");
            var diagOptions = new DbContextOptionsBuilder<DiagnosticsDbContext>()
                .UseSqlServer(targetConnString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "diag"))
                .Options;
            await using (var diagDb = new DiagnosticsDbContext(diagOptions))
            {
                await diagDb.Database.MigrateAsync();
            }
            Console.WriteLine("[OK]");

            // 2. Organization schema
            Console.Write(" - Applying OrganizationDbContext migrations... ");
            var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseSqlServer(targetConnString)
                .Options;
            await using (var orgDb = new OrganizationDbContext(orgOptions))
            {
                await orgDb.Database.MigrateAsync();
            }
            Console.WriteLine("[OK]");

            // 3. Auth schema
            Console.Write(" - Applying AuthDbContext migrations... ");
            var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlServer(targetConnString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))
                .Options;
            await using (var authDb = new AuthDbContext(authOptions))
            {
                await authDb.Database.MigrateAsync();
            }
            Console.WriteLine("[OK]");

            Console.WriteLine(" [OK] All migrations applied successfully.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] Migration execution failed: {ex.Message}");
            Console.ResetColor();
            return 2;
        }

        Console.WriteLine("[4/5] Checking initial system bootstrap state...");
        try
        {
            var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlServer(targetConnString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))
                .Options;
            await using var authDb = new AuthDbContext(authOptions);

            var isInitialized = await authDb.BootstrapStates.AnyAsync(s => s.State == 1);
            if (isInitialized)
            {
                Console.WriteLine(" [OK] System already bootstrapped. Existing accounts and organizations retained.");
            }
            else
            {
                Console.WriteLine(" - Database is fresh. Bootstrapping default organization and admin account...");

                const string adminUser = "admin";
                const string adminPass = "Admin@123456!";
                var consent = HostOperatorConsent.Confirm("INITIALIZE");
                var request = new BootstrapRequest(
                    Guid.NewGuid(),
                    adminUser,
                    "ORG1",
                    "ForgeFlow Industrial Corp",
                    "SITE1",
                    "Central Plant"
                );

                var bootstrapper = new HostBootstrapper(targetConnString);
                var res = await bootstrapper.InitializeAsync(request, adminPass, consent);

                if (res.Code is "Auth.Initialized" or "Auth.AlreadyInitialized")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [OK] Initial bootstrap completed successfully:");
                    Console.WriteLine($"    * Username:     {adminUser}");
                    Console.WriteLine($"    * Password:     {adminPass}");
                    Console.WriteLine("    * Organization: ORG1");
                    Console.WriteLine("    * Site:         SITE1");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($" ! Bootstrap validation result: {res.Code}");
                    Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[ERROR] Bootstrap execution failed: {ex.Message}");
            Console.ResetColor();
            return 3;
        }

        if (args.Any(a => string.Equals(a, "--seed-industrial", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine();
            Console.WriteLine("==================================================================");
            Console.WriteLine("     Running comprehensive industrial seed scenario...");
            Console.WriteLine("==================================================================");
            try
            {
                await IndustrialDemoSeeder.SeedAsync(targetConnString);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Industrial demo seed failed: {ex.Message}");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return 4;
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine("[5/5] Database setup completed successfully!");
        Console.ResetColor();
        return 0;
    }
}
