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
        Console.WriteLine("     ForgeFlow - ابزار آماده‌سازی و راه‌اندازی پایگاه‌داده");
        Console.WriteLine("==================================================================");
        Console.WriteLine();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ForgeFlow");
        if (string.IsNullOrWhiteSpace(connectionString) && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            connectionString = args[0];
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=localhost;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        }

        var csBuilder = new SqlConnectionStringBuilder(connectionString);
        var targetDatabase = string.IsNullOrWhiteSpace(csBuilder.InitialCatalog) ? "testforgflow" : csBuilder.InitialCatalog;
        var serverName = string.IsNullOrWhiteSpace(csBuilder.DataSource) ? "localhost" : csBuilder.DataSource;

        Console.WriteLine($"[1/5] بررسی اتصال به SQL Server در '{serverName}'...");

        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 10
        };

        try
        {
            await using var masterConn = new SqlConnection(masterBuilder.ConnectionString);
            await masterConn.OpenAsync();
            Console.WriteLine(" ✔ اتصال به SQL Server با موفقیت برقرار شد.");

            Console.WriteLine($"[2/5] بررسی وجود پایگاه‌داده '{targetDatabase}'...");
            await using var checkCmd = masterConn.CreateCommand();
            checkCmd.CommandText = $@"
                IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @dbName)
                BEGIN
                    CREATE DATABASE [{targetDatabase}];
                END";
            checkCmd.Parameters.Add(new SqlParameter("@dbName", SqlDbType.NVarChar, 128) { Value = targetDatabase });
            await checkCmd.ExecuteNonQueryAsync();
            Console.WriteLine($" ✔ پایگاه‌داده '{targetDatabase}' آماده است.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[خطا] امکان اتصال به SQL Server وجود ندارد:");
            Console.WriteLine($"پیام خطا: {ex.Message}");
            Console.WriteLine("\nراهنمای رفع مشکل:");
            Console.WriteLine(" ۱. از اجرا بودن سرویس SQL Server (MSSQLSERVER یا SQLEXPRESS) در Windows Services مطمئن شوید.");
            Console.WriteLine(" ۲. در صورتی که نام Instance متفاوتی دارید (مانند SQLEXPRESS)، متغیر محیطی ConnectionStrings__ForgeFlow را تنظیم کنید:");
            Console.WriteLine(@"    set ""ConnectionStrings__ForgeFlow=Server=localhost\SQLEXPRESS;Database=testforgflow;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;""");
            Console.ResetColor();
            return 1;
        }

        var targetConnString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = targetDatabase,
            ConnectTimeout = 15
        }.ConnectionString;

        Console.WriteLine("[3/5] در حال اعمال Migration‌های پایگاه‌داده (۳ اسکیما)...");
        try
        {
            // 1. Diagnostics schema
            Console.Write(" - اعمال اسکیما و مایگریشن‌های لاگ خطا و سیستم (DiagnosticsDbContext)... ");
            var diagOptions = new DbContextOptionsBuilder<DiagnosticsDbContext>()
                .UseSqlServer(targetConnString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "diag"))
                .Options;
            await using (var diagDb = new DiagnosticsDbContext(diagOptions))
            {
                await diagDb.Database.MigrateAsync();
            }
            Console.WriteLine("✔");

            // 2. Auth schema
            Console.Write(" - اعمال اسکیما و مایگریشن‌های احراز هویت و امنیت (AuthDbContext)... ");
            var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlServer(targetConnString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))
                .Options;
            await using (var authDb = new AuthDbContext(authOptions))
            {
                await authDb.Database.MigrateAsync();
            }
            Console.WriteLine("✔");

            // 3. Organization schema
            Console.Write(" - اعمال اسکیما و مایگریشن‌های سازمانی و مدیریت کارخانه (OrganizationDbContext)... ");
            var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>()
                .UseSqlServer(targetConnString)
                .Options;
            await using (var orgDb = new OrganizationDbContext(orgOptions))
            {
                await orgDb.Database.MigrateAsync();
            }
            Console.WriteLine("✔");

            Console.WriteLine(" ✔ تمامی مایگریشن‌ها با موفقیت اعمال شدند.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[خطا] اعمال مایگریشن‌ها با شکست مواجه شد: {ex.Message}");
            Console.ResetColor();
            return 2;
        }

        Console.WriteLine("[4/5] بررسی وضعیت راه‌اندازی اولیه (Bootstrap)...");
        try
        {
            var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlServer(targetConnString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))
                .Options;
            await using var authDb = new AuthDbContext(authOptions);

            var isInitialized = await authDb.BootstrapStates.AnyAsync(s => s.State == 1);
            if (isInitialized)
            {
                Console.WriteLine(" ✔ سامانه قبلاً راه‌اندازی شده است. حساب‌های کاربری و سازمان‌های موجود حفظ شدند.");
            }
            else
            {
                Console.WriteLine(" - پایگاه‌داده تازه است. در حال ایجاد سازمان اولیه و حساب مدیر سیستم (Admin)...");

                const string adminUser = "admin";
                const string adminPass = "Admin@123456!";
                var consent = HostOperatorConsent.Confirm("INITIALIZE");
                var request = new BootstrapRequest(
                    Guid.NewGuid(),
                    adminUser,
                    "ORG1",
                    "شرکت تولیدی و صنعتی فورج‌فلو",
                    "SITE1",
                    "کارخانه مرکزی"
                );

                var bootstrapper = new HostBootstrapper(targetConnString);
                var res = await bootstrapper.InitializeAsync(request, adminPass, consent);

                if (res.Code is "Auth.Initialized" or "Auth.AlreadyInitialized")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" ✔ راه‌اندازی اولیه با موفقیت انجام شد:");
                    Console.WriteLine($"    • نام کاربری مدیر (Username): {adminUser}");
                    Console.WriteLine($"    • رمز عبور (Password):        {adminPass}");
                    Console.WriteLine("    • کد سازمان:                 ORG1");
                    Console.WriteLine("    • کد کارخانه:                SITE1");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($" ! نتیجه اعتبارسنجی راه‌اندازی: {res.Code}");
                    Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[خطا] راه‌اندازی اولیه با شکست مواجه شد: {ex.Message}");
            Console.ResetColor();
            return 3;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine("[5/5] آماده‌سازی پایگاه‌داده با موفقیت به پایان رسید!");
        Console.ResetColor();
        return 0;
    }
}
