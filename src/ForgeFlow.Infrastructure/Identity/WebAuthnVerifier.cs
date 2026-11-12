using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ForgeFlow.Infrastructure.Identity;

public sealed record VerifiedPasskeyRegistration(
    byte[] CredentialId,
    byte[] PublicKeyCose,
    int Algorithm,
    long SignCount);

public static class WebAuthnVerifier
{
    public const int AlgEs256 = -7;
    public const int AlgRs256 = -257;

    public static string ToBase64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        var mod = padded.Length % 4;
        if (mod == 2) padded += "==";
        else if (mod == 3) padded += "=";
        else if (mod == 1) throw new FormatException("FF-WEBAUTHN-InvalidBase64Url");
        return Convert.FromBase64String(padded);
    }

    public static VerifiedPasskeyRegistration? VerifyRegistration(
        byte[] clientDataJsonBytes,
        byte[] attestationObjectBytes,
        string expectedChallengeB64Url,
        string expectedOrigin,
        string expectedRpId)
    {
        if (!ValidateClientData(clientDataJsonBytes, "webauthn.create", expectedChallengeB64Url, expectedOrigin))
            return null;

        byte[]? authData = null;
        try
        {
            var reader = new CborReader(attestationObjectBytes, CborConformanceMode.Lax);
            var mapCount = reader.ReadStartMap();
            for (var i = 0; mapCount is null ? reader.PeekState() != CborReaderState.EndMap : i < mapCount.Value; i++)
            {
                var key = reader.ReadTextString();
                if (key == "authData")
                    authData = reader.ReadByteString();
                else
                    reader.SkipValue();
            }
            reader.ReadEndMap();
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException)
        {
            return null;
        }

        if (authData is null || authData.Length < 55)
            return null;

        if (!ValidateRpIdHashAndFlags(authData, expectedRpId, requireAttestedCredentialData: true, out var signCount))
            return null;

        var credIdLen = BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(53, 2));
        if (credIdLen == 0 || authData.Length < 55 + credIdLen)
            return null;

        var credId = authData.AsSpan(55, credIdLen).ToArray();
        var coseBytes = authData.AsSpan(55 + credIdLen).ToArray();
        if (!TryExtractAlgorithmAndValidateCoseKey(coseBytes, out var alg))
            return null;

        return new VerifiedPasskeyRegistration(credId, coseBytes, alg, signCount);
    }

    public static bool VerifyAssertion(
        byte[] clientDataJsonBytes,
        byte[] authData,
        byte[] signature,
        byte[] publicKeyCose,
        int algorithm,
        long storedSignCount,
        string expectedChallengeB64Url,
        string expectedOrigin,
        string expectedRpId,
        out long newSignCount)
    {
        newSignCount = storedSignCount;
        if (!ValidateClientData(clientDataJsonBytes, "webauthn.get", expectedChallengeB64Url, expectedOrigin))
            return false;

        if (!ValidateRpIdHashAndFlags(authData, expectedRpId, requireAttestedCredentialData: false, out var rawCount))
            return false;

        if ((storedSignCount > 0 || rawCount > 0) && rawCount <= storedSignCount)
            return false;

        newSignCount = rawCount;
        var clientDataHash = SHA256.HashData(clientDataJsonBytes);
        var signedPayload = new byte[authData.Length + clientDataHash.Length];
        Buffer.BlockCopy(authData, 0, signedPayload, 0, authData.Length);
        Buffer.BlockCopy(clientDataHash, 0, signedPayload, authData.Length, clientDataHash.Length);

        return algorithm switch
        {
            AlgEs256 => VerifyEs256(publicKeyCose, signedPayload, signature),
            AlgRs256 => VerifyRs256(publicKeyCose, signedPayload, signature),
            _ => false
        };
    }

    private static bool ValidateClientData(
        byte[] clientDataJsonBytes,
        string expectedType,
        string expectedChallengeB64Url,
        string expectedOrigin)
    {
        try
        {
            using var doc = JsonDocument.Parse(clientDataJsonBytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != expectedType) return false;
            if (!root.TryGetProperty("challenge", out var chalProp) || !string.Equals(chalProp.GetString(), expectedChallengeB64Url, StringComparison.Ordinal)) return false;
            if (!root.TryGetProperty("origin", out var origProp) || !string.Equals(origProp.GetString(), expectedOrigin, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ValidateRpIdHashAndFlags(
        byte[] authData,
        string expectedRpId,
        bool requireAttestedCredentialData,
        out long signCount)
    {
        signCount = 0;
        if (authData.Length < 37) return false;

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedRpId));
        if (!CryptographicOperations.FixedTimeEquals(authData.AsSpan(0, 32), expectedHash))
            return false;

        var flags = authData[32];
        var userPresent = (flags & 0x01) != 0;
        var attested = (flags & 0x40) != 0;
        if (!userPresent) return false;
        if (requireAttestedCredentialData && !attested) return false;

        signCount = BinaryPrimitives.ReadUInt32BigEndian(authData.AsSpan(33, 4));
        return true;
    }

    private static bool TryExtractAlgorithmAndValidateCoseKey(byte[] coseBytes, out int alg)
    {
        alg = 0;
        try
        {
            var map = ReadCoseMap(coseBytes);
            if (!map.TryGetValue(3, out var algObj) || algObj is not long algVal) return false;
            alg = (int)algVal;
            if (alg == AlgEs256)
                return TryBuildEcParameters(map, out _);
            if (alg == AlgRs256)
                return TryBuildRsaParameters(map, out _);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyEs256(byte[] coseBytes, byte[] payload, byte[] signature)
    {
        try
        {
            var map = ReadCoseMap(coseBytes);
            if (!TryBuildEcParameters(map, out var ecParams)) return false;
            using var ecdsa = ECDsa.Create(ecParams);
            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyRs256(byte[] coseBytes, byte[] payload, byte[] signature)
    {
        try
        {
            var map = ReadCoseMap(coseBytes);
            if (!TryBuildRsaParameters(map, out var rsaParams)) return false;
            using var rsa = RSA.Create(rsaParams);
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<long, object> ReadCoseMap(byte[] coseBytes)
    {
        var reader = new CborReader(coseBytes, CborConformanceMode.Lax);
        var count = reader.ReadStartMap();
        var map = new Dictionary<long, object>();
        for (var i = 0; count is null ? reader.PeekState() != CborReaderState.EndMap : i < count.Value; i++)
        {
            var key = reader.ReadInt64();
            var state = reader.PeekState();
            object val = state switch
            {
                CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger => reader.ReadInt64(),
                CborReaderState.ByteString => reader.ReadByteString(),
                _ => SkipAndNull(reader)
            };
            map[key] = val;
        }
        reader.ReadEndMap();
        return map;

        static object SkipAndNull(CborReader r) { r.SkipValue(); return new object(); }
    }

    private static bool TryBuildEcParameters(Dictionary<long, object> map, out ECParameters ecParams)
    {
        ecParams = default;
        if (!map.TryGetValue(1, out var kty) || kty is not 2L) return false;
        if (!map.TryGetValue(-1, out var crv) || crv is not 1L) return false;
        if (!map.TryGetValue(-2, out var xObj) || xObj is not byte[] x || x.Length != 32) return false;
        if (!map.TryGetValue(-3, out var yObj) || yObj is not byte[] y || y.Length != 32) return false;

        ecParams = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = x, Y = y }
        };
        return true;
    }

    private static bool TryBuildRsaParameters(Dictionary<long, object> map, out RSAParameters rsaParams)
    {
        rsaParams = default;
        if (!map.TryGetValue(1, out var kty) || kty is not 3L) return false;
        if (!map.TryGetValue(-1, out var nObj) || nObj is not byte[] n || n.Length == 0) return false;
        if (!map.TryGetValue(-2, out var eObj) || eObj is not byte[] e || e.Length == 0) return false;

        rsaParams = new RSAParameters { Modulus = n, Exponent = e };
        return true;
    }
}
