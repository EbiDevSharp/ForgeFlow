using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ForgeFlow.Application.Users;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AccountSecurityTests
{
    [Fact]
    public void QrCodeSvg_renders_valid_local_svg_for_totp_uri()
    {
        var uri = "otpauth://totp/ForgeFlow:admin?secret=JBSWY3DPEHPK3PXP&issuer=ForgeFlow&digits=6";
        var svg = QrCodeSvg.GenerateSvg(uri);

        Assert.StartsWith("<svg ", svg, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", svg, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 ", svg, StringComparison.Ordinal);
        Assert.Contains("<path d=\"M", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(15, true)]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(45, false)]
    [InlineData(120, false)]
    [InlineData(-15, false)]
    public void Idle_timeout_catalog_allows_only_off_15_30_60(int? minutes, bool expected)
        => Assert.Equal(expected, UserPreferenceCatalog.IsValidIdleTimeout(minutes));

    [Fact]
    public void Pending_two_factor_cookie_helper_rejects_missing_or_tampered_cookie()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        using var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        Assert.False(AuthenticationBackend.HasValidPendingTwoFactor(ctx));

        ctx.Request.Headers.Cookie = $"{AuthenticationBackend.TwoFactorPendingCookieName}=tampered-invalid-value";
        Assert.False(AuthenticationBackend.HasValidPendingTwoFactor(ctx));
    }

    [Fact]
    public async Task LoginTwoFactor_rejects_missing_pending_cookie_with_401()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAntiforgery();
        services.AddAuthenticationBackend(new ConfigurationBuilder().Build());
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var csrf = scope.ServiceProvider.GetRequiredService<IAntiforgery>();
        var sessions = scope.ServiceProvider.GetRequiredService<RuntimeSessions>();

        var issue = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        issue.Request.Scheme = "https";
        var tokens = csrf.GetAndStoreTokens(issue);
        var csrfCookie = issue.Response.Headers.SetCookie.Single()!.Split(';')[0];

        var body = $"code=123456&__RequestVerificationToken={Uri.EscapeDataString(tokens.RequestToken!)}";
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "https";
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Headers.Cookie = csrfCookie;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Response.Body = new MemoryStream();

        var result = await AuthenticationBackend.LoginTwoFactor(ctx, csrf, sessions);
        await result.ExecuteAsync(ctx);

        Assert.Equal(401, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        var json = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        Assert.Contains("Auth.TwoFactorPendingExpired", json);
    }

    [Fact]
    public async Task Totp_confirm_and_idle_timeout_validate_inputs_before_store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthenticationBackend(new ConfigurationBuilder().Build());
        using var sp = services.BuildServiceProvider();
        var sessions = sp.GetRequiredService<RuntimeSessions>();
        var reference = new SessionReference(1, Guid.NewGuid(), "stamp");

        Assert.Equal("Auth.TwoFactorInvalidCode", (await sessions.ConfirmTotpSetupAsync(reference, "123")).Code);
        Assert.Equal("Auth.TwoFactorInvalidCode", (await sessions.ConfirmTotpSetupAsync(reference, "abcdef")).Code);
        Assert.Equal("Preferences.InvalidInput", await sessions.SetIdleTimeoutAsync(reference, 45));
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void Two_factor_authentication_error_codes_have_localized_resources(string culture)
    {
        var prev = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var sp = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider();
            var text = sp.GetRequiredService<IStringLocalizer<AuthenticationResources>>();
            foreach (var code in new[]
                     {
                         "Auth.TwoFactorInvalidCode",
                         "Auth.TwoFactorPendingExpired",
                         "Auth.TwoFactorNotEnabled",
                         "Auth.TwoFactorEnabled",
                         "Auth.TwoFactorDisabled",
                         "Auth.RecoveryCodesRegenerated"
                     })
            {
                Assert.False(text[code].ResourceNotFound, $"Missing {code} for {culture}");
                Assert.False(string.IsNullOrWhiteSpace(text[code]));
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
        }
    }

    [Fact]
    public void WebAuthnVerifier_verifies_real_es256_registration_and_assertion_and_blocks_replay_or_wrong_origin()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = key.ExportParameters(false);

        var coseWriter = new CborWriter();
        coseWriter.WriteStartMap(5);
        coseWriter.WriteInt32(1); coseWriter.WriteInt32(2);   // kty: EC2
        coseWriter.WriteInt32(3); coseWriter.WriteInt32(-7);  // alg: ES256
        coseWriter.WriteInt32(-1); coseWriter.WriteInt32(1);  // crv: P-256
        coseWriter.WriteInt32(-2); coseWriter.WriteByteString(pub.Q.X!);
        coseWriter.WriteInt32(-3); coseWriter.WriteByteString(pub.Q.Y!);
        coseWriter.WriteEndMap();
        var coseKey = coseWriter.Encode();

        var rpId = "localhost";
        var origin = "https://localhost:5001";
        var rpHash = SHA256.HashData(Encoding.UTF8.GetBytes(rpId));
        var credId = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var authData = new byte[32 + 1 + 4 + 16 + 2 + credId.Length + coseKey.Length];
        rpHash.CopyTo(authData, 0);
        authData[32] = 0x41; // UP + AT
        BinaryPrimitives.WriteUInt32BigEndian(authData.AsSpan(33, 4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(authData.AsSpan(53, 2), (ushort)credId.Length);
        credId.CopyTo(authData, 55);
        coseKey.CopyTo(authData, 55 + credId.Length);

        var attWriter = new CborWriter();
        attWriter.WriteStartMap(2);
        attWriter.WriteTextString("fmt"); attWriter.WriteTextString("none");
        attWriter.WriteTextString("authData"); attWriter.WriteByteString(authData);
        attWriter.WriteEndMap();
        var attObj = attWriter.Encode();

        var regChallenge = WebAuthnVerifier.ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var regClientData = Encoding.UTF8.GetBytes($"{{\"type\":\"webauthn.create\",\"challenge\":\"{regChallenge}\",\"origin\":\"{origin}\"}}");

        var verified = WebAuthnVerifier.VerifyRegistration(regClientData, attObj, regChallenge, origin, rpId);
        Assert.NotNull(verified);
        Assert.Equal(WebAuthnVerifier.AlgEs256, verified!.Algorithm);
        Assert.Equal(credId, verified.CredentialId);

        // Wrong origin rejected
        Assert.Null(WebAuthnVerifier.VerifyRegistration(regClientData, attObj, regChallenge, "https://evil.example", rpId));

        // Assertion with signCount = 2
        var assertChallenge = WebAuthnVerifier.ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var assertClientData = Encoding.UTF8.GetBytes($"{{\"type\":\"webauthn.get\",\"challenge\":\"{assertChallenge}\",\"origin\":\"{origin}\"}}");
        var assertAuthData = new byte[37];
        rpHash.CopyTo(assertAuthData, 0);
        assertAuthData[32] = 0x01; // UP
        BinaryPrimitives.WriteUInt32BigEndian(assertAuthData.AsSpan(33, 4), 2);

        var clientHash = SHA256.HashData(assertClientData);
        var payload = assertAuthData.Concat(clientHash).ToArray();
        var sig = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Assert.True(WebAuthnVerifier.VerifyAssertion(assertClientData, assertAuthData, sig, verified.PublicKeyCose, verified.Algorithm, 1, assertChallenge, origin, rpId, out var newCount));
        Assert.Equal(2, newCount);

        // SignCount replay (<= storedSignCount) rejected
        Assert.False(WebAuthnVerifier.VerifyAssertion(assertClientData, assertAuthData, sig, verified.PublicKeyCose, verified.Algorithm, 2, assertChallenge, origin, rpId, out _));
    }
}
