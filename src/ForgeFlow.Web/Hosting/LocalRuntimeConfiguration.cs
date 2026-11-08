using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace ForgeFlow.Web.Hosting;

public static class LocalRuntimeConfiguration
{
    public const string SmokeEnvironment = "SmokeTest";
    public const string SmokeKeyDirectory = "ForgeFlow:Smoke:KeyDirectory";

    // Keep the returned certificate alive until the host stops. Production never
    // enters these local branches and retains deployment-managed defaults.
    public static IDisposable? Configure(WebApplicationBuilder builder)
    {
        var smokeTest = builder.Environment.IsEnvironment(SmokeEnvironment);
        if (!builder.Environment.IsDevelopment() && !smokeTest)
            return null;

        // Local processes retain errors on their captured console rather than
        // depending on a registered Windows Event Log source.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        // This Windows-specific development fix does not change non-Windows
        // platform defaults. The smoke configuration below is platform-neutral.
        if (!smokeTest && !OperatingSystem.IsWindows())
            return null;

        var protection = builder.Services.AddDataProtection();
        if (!smokeTest && OperatingSystem.IsWindows())
        {
            protection.SetApplicationName("ForgeFlow.Development")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".local", "data-protection")))
                .ProtectKeysWithDpapi(protectToLocalMachine: false);
            return null;
        }

        // One fresh directory and in-memory RSA certificate per smoke host. Full
        // Data Protection and antiforgery stay enabled; key XML is encrypted.
        var keyPath = builder.Configuration[SmokeKeyDirectory];
        if (string.IsNullOrWhiteSpace(keyPath) || !Path.IsPathFullyQualified(keyPath))
            throw new InvalidOperationException("FF-RUNTIME-SmokeKeyDirectoryRequired");
        var directory = new DirectoryInfo(keyPath);
        if (directory.Exists && directory.EnumerateFileSystemInfos().Any())
            throw new InvalidOperationException("FF-RUNTIME-SmokeKeyDirectoryMustBeEmpty");

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=ForgeFlow.SmokeTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        protection.SetApplicationName("ForgeFlow.SmokeTest")
            .PersistKeysToFileSystem(directory)
            .ProtectKeysWithCertificate(certificate);
        builder.WebHost.UseStaticWebAssets();
        return certificate;
    }
}
