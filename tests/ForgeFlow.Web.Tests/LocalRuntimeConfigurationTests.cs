using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using ForgeFlow.Web.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class LocalRuntimeConfigurationTests
{
    [Fact]
    public async Task Smoke_keys_are_encrypted_and_can_be_read_by_another_provider_with_the_same_certificate()
    {
        using var directory = new TestDirectory();
        var builder = CreateBuilder(LocalRuntimeConfiguration.SmokeEnvironment, directory.Path);
        builder.Services.AddAntiforgery();
        using var setup = LocalRuntimeConfiguration.Configure(builder);
        var certificate = Assert.IsType<X509Certificate2>(setup);
        Assert.True(certificate.HasPrivateKey);
        using var rsa = certificate.GetRSAPrivateKey();
        Assert.NotNull(rsa);
        Assert.True(rsa.KeySize >= 3072);
        await using var app = builder.Build();
        var keys = app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<CertificateXmlEncryptor>(keys.XmlEncryptor);
        Assert.Equal(directory.Path, Assert.IsType<FileSystemXmlRepository>(keys.XmlRepository).Directory.FullName);
        var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("RuntimeTest");
        var protectedValue = protector.Protect("round-trip-fixture");
        Assert.Equal("round-trip-fixture", protector.Unprotect(protectedValue));
        Assert.Throws<CryptographicException>(() => protector.Unprotect("invalid-fixture"));

        var keyFile = Assert.Single(Directory.GetFiles(directory.Path, "key-*.xml"));
        var keyXml = XDocument.Load(keyFile);
        XNamespace protectionNamespace = "http://schemas.asp.net/2015/03/dataProtection";
        Assert.Single(keyXml.Descendants(protectionNamespace + "encryptedSecret"));
        Assert.DoesNotContain(keyXml.Descendants(), element => element.Name.LocalName == "masterKey");
        Assert.Contains(keyXml.Descendants(), element => element.Name.LocalName == "EncryptedData");

        var restoredServices = new ServiceCollection().AddLogging();
        restoredServices.AddDataProtection().SetApplicationName("ForgeFlow.SmokeTest")
            .PersistKeysToFileSystem(new DirectoryInfo(directory.Path))
            .ProtectKeysWithCertificate(certificate);
        using var restored = restoredServices.BuildServiceProvider();
        var restoredProtector = restored.GetRequiredService<IDataProtectionProvider>().CreateProtector("RuntimeTest");
        Assert.Equal("round-trip-fixture", restoredProtector.Unprotect(protectedValue));

        var context = new DefaultHttpContext { RequestServices = app.Services };
        var tokens = app.Services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        Assert.False(string.IsNullOrEmpty(tokens.RequestToken));
        Assert.False(string.IsNullOrEmpty(tokens.CookieToken));
        Assert.True(context.Response.Headers.SetCookie.Count > 0);
        Assert.True(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RuntimeTest").IsEnabled(LogLevel.Error));
        Assert.DoesNotContain(app.Services.GetServices<ILoggerProvider>(), provider => provider.GetType().Name.Contains("EventLog", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Independent_smoke_runs_do_not_share_keys_or_protected_payloads()
    {
        using var firstDirectory = new TestDirectory();
        using var secondDirectory = new TestDirectory();
        var firstBuilder = CreateBuilder(LocalRuntimeConfiguration.SmokeEnvironment, firstDirectory.Path);
        var secondBuilder = CreateBuilder(LocalRuntimeConfiguration.SmokeEnvironment, secondDirectory.Path);
        using var firstSetup = LocalRuntimeConfiguration.Configure(firstBuilder);
        using var secondSetup = LocalRuntimeConfiguration.Configure(secondBuilder);
        await using var first = firstBuilder.Build();
        await using var second = secondBuilder.Build();
        var firstProtector = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("RuntimeTest");
        var secondProtector = second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("RuntimeTest");
        var payload = firstProtector.Protect("isolated-fixture");
        Assert.Throws<CryptographicException>(() => secondProtector.Unprotect(payload));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative-keys")]
    public void Smoke_configuration_fails_closed_without_an_absolute_key_directory(string? path)
    {
        var builder = CreateBuilder(LocalRuntimeConfiguration.SmokeEnvironment, path);
        var error = Assert.Throws<InvalidOperationException>(() => LocalRuntimeConfiguration.Configure(builder));
        Assert.Equal("FF-RUNTIME-SmokeKeyDirectoryRequired", error.Message);
    }

    [Fact]
    public void Smoke_configuration_rejects_an_existing_nonempty_key_ring()
    {
        using var directory = new TestDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "fixture.txt"), "existing-fixture");
        var builder = CreateBuilder(LocalRuntimeConfiguration.SmokeEnvironment, directory.Path);
        var error = Assert.Throws<InvalidOperationException>(() => LocalRuntimeConfiguration.Configure(builder));
        Assert.Equal("FF-RUNTIME-SmokeKeyDirectoryMustBeEmpty", error.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Deployment_environments_ignore_local_smoke_configuration(string environment)
    {
        var builder = CreateBuilder(environment, "invalid-fixture");
        var originalServices = builder.Services.ToArray();
        Assert.Null(LocalRuntimeConfiguration.Configure(builder));
        Assert.Equal(originalServices, builder.Services.ToArray());
    }

    [Fact]
    public async Task Windows_development_uses_its_own_key_directory_and_retains_current_user_DPAPI()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        var builder = CreateBuilder("Development", null);
        builder.Environment.ContentRootPath = directory.Path;
        Assert.Null(LocalRuntimeConfiguration.Configure(builder));
        await using var app = builder.Build();
        var keys = app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<DpapiXmlEncryptor>(keys.XmlEncryptor);
        var repository = Assert.IsType<FileSystemXmlRepository>(keys.XmlRepository);
        Assert.Equal(System.IO.Path.Combine(directory.Path, ".local", "data-protection"), repository.Directory.FullName);
        Assert.Equal("ForgeFlow.Development", app.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.DoesNotContain(app.Services.GetServices<ILoggerProvider>(), provider => provider.GetType().Name.Contains("EventLog", StringComparison.Ordinal));
        Assert.True(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RuntimeTest").IsEnabled(LogLevel.Error));
    }

    private static WebApplicationBuilder CreateBuilder(string environment, string? keyPath)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(Program).Assembly.GetName().Name
        });
        builder.Configuration[LocalRuntimeConfiguration.SmokeKeyDirectory] = keyPath;
        return builder;
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ForgeFlow.RuntimeTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            // Delete only this fixture's explicit GUID directory under temp.
            var resolved = System.IO.Path.GetFullPath(Path);
            var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ForgeFlow.RuntimeTests"));
            if (!string.Equals(System.IO.Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(System.IO.Path.GetFileName(resolved), "N", out _))
                throw new InvalidOperationException("Invalid fixture cleanup path");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }
}
