using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace ForgeFlow.Web.Tests;

internal static class TestAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var existing = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
        if (!string.IsNullOrWhiteSpace(existing))
            return;

        // Auto-detect available local SQL Server instance
        var candidates = new[]
        {
            "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=True;Connect Timeout=2;",
            "Server=(localdb)\\mssqllocaldb;Database=master;Integrated Security=true;TrustServerCertificate=True;Connect Timeout=2;"
        };

        foreach (var candidate in candidates)
        {
            try
            {
                using var conn = new SqlConnection(candidate);
                conn.Open();
                Environment.SetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER", candidate);
                break;
            }
            catch
            {
                // candidate not available, continue to next
            }
        }
    }
}
