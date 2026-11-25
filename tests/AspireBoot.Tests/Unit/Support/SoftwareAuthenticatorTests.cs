using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AspireBoot.Tests.Support.WebAuthn;

namespace AspireBoot.Tests.Unit.Support;

[Trait("Category", "Unit")]
public sealed class SoftwareAuthenticatorTests
{
    private static readonly byte[] UserId = [1, 2, 3, 4];

    [Fact]
    public void CreateAttestation_ClientDataHasCreateTypeChallengeAndOrigin()
    {
        var challenge = new byte[] { 9, 8, 7, 6 };
        using var authenticator = new SoftwareAuthenticator();

        using var response = JsonDocument.Parse(authenticator.CreateAttestation(CreationOptions(challenge)));

        var clientData = ParseClientData(response.RootElement.GetProperty("response").GetProperty("clientDataJSON"));
        Assert.Equal("webauthn.create", clientData.GetProperty("type").GetString());
        Assert.Equal(Base64Url.EncodeToString(challenge), clientData.GetProperty("challenge").GetString());
        Assert.Equal("https://localhost", clientData.GetProperty("origin").GetString());
    }

    [Fact]
    public void CreateAttestation_UsesNoneFormatAndKeepsCredentialIdConsistent()
    {
        using var authenticator = new SoftwareAuthenticator();

        using var response = JsonDocument.Parse(authenticator.CreateAttestation(CreationOptions([1])));

        var root = response.RootElement;
        Assert.Equal("public-key", root.GetProperty("type").GetString());
        Assert.Equal(root.GetProperty("id").GetString(), root.GetProperty("rawId").GetString());
        Assert.NotEmpty(Base64Url.DecodeFromChars(root.GetProperty("id").GetString()!));
        Assert.True(root.GetProperty("response").TryGetProperty("attestationObject", out _));
    }

    [Fact]
    public void CreateAssertion_ClientDataHasGetTypeChallengeAndOrigin()
    {
        var challenge = new byte[] { 5, 4, 3, 2 };
        using var authenticator = new SoftwareAuthenticator();
        authenticator.CreateAttestation(CreationOptions([1]));

        using var response = JsonDocument.Parse(authenticator.CreateAssertion(RequestOptions(challenge)));

        var clientData = ParseClientData(response.RootElement.GetProperty("response").GetProperty("clientDataJSON"));
        Assert.Equal("webauthn.get", clientData.GetProperty("type").GetString());
        Assert.Equal(Base64Url.EncodeToString(challenge), clientData.GetProperty("challenge").GetString());
        Assert.Equal("https://localhost", clientData.GetProperty("origin").GetString());
    }

    [Fact]
    public void CreateAssertion_SignatureVerifiesWithPublicKeyAndReturnsUserHandle()
    {
        using var authenticator = new SoftwareAuthenticator();
        authenticator.CreateAttestation(CreationOptions([1]));

        using var response = JsonDocument.Parse(authenticator.CreateAssertion(RequestOptions([5, 4, 3, 2])));

        var assertion = response.RootElement.GetProperty("response");
        var authenticatorData = Base64Url.DecodeFromChars(assertion.GetProperty("authenticatorData").GetString()!);
        var clientDataJson = Base64Url.DecodeFromChars(assertion.GetProperty("clientDataJSON").GetString()!);
        var signature = Base64Url.DecodeFromChars(assertion.GetProperty("signature").GetString()!);
        Assert.Equal(Base64Url.EncodeToString(UserId), assertion.GetProperty("userHandle").GetString());

        using var publicKey = ECDsa.Create(authenticator.PublicKeyParameters);
        var signedData = authenticatorData.Concat(SHA256.HashData(clientDataJson)).ToArray();
        Assert.True(publicKey.VerifyData(signedData, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }

    [Fact]
    public void CreateAssertion_RpIdHashMatchesLocalhostAndSignCountIncrements()
    {
        using var authenticator = new SoftwareAuthenticator();
        authenticator.CreateAttestation(CreationOptions([1]));

        var first = AuthenticatorDataOf(authenticator.CreateAssertion(RequestOptions([1])));
        var second = AuthenticatorDataOf(authenticator.CreateAssertion(RequestOptions([2])));

        Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes("localhost")), first[..32]);
        Assert.Equal(1u, ReadUInt32(first, 33));
        Assert.Equal(2u, ReadUInt32(second, 33));
    }

    [Fact]
    public void CreateAssertion_WithoutRegisteredCredential_Throws()
    {
        using var authenticator = new SoftwareAuthenticator();

        Assert.Throws<InvalidOperationException>(() => authenticator.CreateAssertion(RequestOptions([1])));
    }

    private static string CreationOptions(byte[] challenge) => JsonSerializer.Serialize(new
    {
        challenge = Base64Url.EncodeToString(challenge),
        rp = new { id = "localhost", name = "AspireBoot" },
        user = new { id = Base64Url.EncodeToString(UserId), name = "ana@aspireboot.test", displayName = "Ana" },
        pubKeyCredParams = new[] { new { type = "public-key", alg = -7 } },
    });

    private static string RequestOptions(byte[] challenge) => JsonSerializer.Serialize(new
    {
        challenge = Base64Url.EncodeToString(challenge),
        rpId = "localhost",
    });

    private static JsonElement ParseClientData(JsonElement encoded)
    {
        var bytes = Base64Url.DecodeFromChars(encoded.GetString()!);
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    private static byte[] AuthenticatorDataOf(string assertionJson)
    {
        using var response = JsonDocument.Parse(assertionJson);
        return Base64Url.DecodeFromChars(response.RootElement.GetProperty("response").GetProperty("authenticatorData").GetString()!);
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
}