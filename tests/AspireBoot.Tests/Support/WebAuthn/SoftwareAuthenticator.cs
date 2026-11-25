using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AspireBoot.Tests.Support.WebAuthn;

/// <summary>
/// Software WebAuthn authenticator for tests: one discoverable ES256 (P-256) credential,
/// "none" attestation, origin https://localhost and rpId localhost.
/// Takes the options JSON produced by the server and returns the credential JSON a browser would send.
/// </summary>
public sealed class SoftwareAuthenticator : IDisposable
{
    public const string Origin = "https://localhost";
    public const string RpId = "localhost";

    private static readonly string[] Transports = ["internal"];

    private const byte FlagUserPresent = 0x01;
    private const byte FlagUserVerified = 0x04;
    private const byte FlagAttestedCredentialData = 0x40;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private byte[]? _credentialId;
    private byte[]? _userHandle;
    private uint _signCount;

    /// <summary>Public half of the credential key, for verifying signatures.</summary>
    public ECParameters PublicKeyParameters => _key.ExportParameters(includePrivateParameters: false);

    /// <summary>Creates the credential described by PublicKeyCredentialCreationOptions JSON.</summary>
    public string CreateAttestation(string creationOptionsJson)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        var root = options.RootElement;
        var challenge = Base64Url.DecodeFromChars(root.GetProperty("challenge").GetString()!);
        _userHandle = Base64Url.DecodeFromChars(root.GetProperty("user").GetProperty("id").GetString()!);
        _credentialId = RandomNumberGenerator.GetBytes(32);

        var clientDataJson = ClientDataJson("webauthn.create", challenge);
        var authenticatorData = BuildAttestedAuthenticatorData();

        var attestationObject = new CborWriter();
        attestationObject.WriteStartMap(3);
        attestationObject.WriteTextString("fmt");
        attestationObject.WriteTextString("none");
        attestationObject.WriteTextString("attStmt");
        attestationObject.WriteStartMap(0);
        attestationObject.WriteEndMap();
        attestationObject.WriteTextString("authData");
        attestationObject.WriteByteString(authenticatorData);
        attestationObject.WriteEndMap();

        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(_credentialId),
            rawId = Base64Url.EncodeToString(_credentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                attestationObject = Base64Url.EncodeToString(attestationObject.Encode()),
                transports = Transports,
            },
        });
    }

    /// <summary>Signs a discoverable assertion for PublicKeyCredentialRequestOptions JSON.</summary>
    public string CreateAssertion(string requestOptionsJson)
    {
        if (_credentialId is null || _userHandle is null)
        {
            throw new InvalidOperationException("No credential has been created; call CreateAttestation first.");
        }

        using var options = JsonDocument.Parse(requestOptionsJson);
        var challenge = Base64Url.DecodeFromChars(options.RootElement.GetProperty("challenge").GetString()!);

        _signCount++;
        var authenticatorData = BuildAssertionAuthenticatorData(_signCount);
        var clientDataJson = ClientDataJson("webauthn.get", challenge);
        var signedData = authenticatorData.Concat(SHA256.HashData(clientDataJson)).ToArray();
        var signature = _key.SignData(signedData, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return JsonSerializer.Serialize(new
        {
            id = Base64Url.EncodeToString(_credentialId),
            rawId = Base64Url.EncodeToString(_credentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64Url.EncodeToString(clientDataJson),
                authenticatorData = Base64Url.EncodeToString(authenticatorData),
                signature = Base64Url.EncodeToString(signature),
                userHandle = Base64Url.EncodeToString(_userHandle),
            },
        });
    }

    public void Dispose() => _key.Dispose();

    private static byte[] ClientDataJson(string type, byte[] challenge) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        type,
        challenge = Base64Url.EncodeToString(challenge),
        origin = Origin,
        crossOrigin = false,
    });

    private byte[] BuildAttestedAuthenticatorData()
    {
        var credentialPublicKey = CoseEs256PublicKey();
        var data = new List<byte>(256);
        data.AddRange(SHA256.HashData(Encoding.ASCII.GetBytes(RpId)));
        data.Add(FlagUserPresent | FlagUserVerified | FlagAttestedCredentialData);
        data.AddRange(BitConverter.GetBytes((uint)0).Reverse());
        data.AddRange(new byte[16]); // AAGUID: all zeros for "none" attestation
        data.AddRange(BitConverter.GetBytes((ushort)_credentialId!.Length).Reverse());
        data.AddRange(_credentialId);
        data.AddRange(credentialPublicKey);
        return data.ToArray();
    }

    private static byte[] BuildAssertionAuthenticatorData(uint signCount)
    {
        var data = new List<byte>(37);
        data.AddRange(SHA256.HashData(Encoding.ASCII.GetBytes(RpId)));
        data.Add(FlagUserPresent | FlagUserVerified);
        data.AddRange(BitConverter.GetBytes(signCount).Reverse());
        return data.ToArray();
    }

    /// <summary>COSE_Key (RFC 9052) for ES256: kty=EC2, alg=-7, crv=P-256, x, y.</summary>
    private byte[] CoseEs256PublicKey()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        var writer = new CborWriter();
        writer.WriteStartMap(5);
        writer.WriteInt32(1);
        writer.WriteInt32(2); // kty: EC2
        writer.WriteInt32(3);
        writer.WriteInt32(-7); // alg: ES256
        writer.WriteInt32(-1);
        writer.WriteInt32(1); // crv: P-256
        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Q.X!);
        writer.WriteInt32(-3);
        writer.WriteByteString(parameters.Q.Y!);
        writer.WriteEndMap();
        return writer.Encode();
    }
}