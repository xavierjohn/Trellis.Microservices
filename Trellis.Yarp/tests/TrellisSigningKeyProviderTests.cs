namespace Trellis.Yarp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using global::Microsoft.IdentityModel.JsonWebTokens;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// Tests for the signing-key provider seam: <see cref="TrellisSigningKeyRing"/>,
/// <see cref="TrellisSigningKeyRingValidator"/>, <see cref="StaticTrellisSigningKeyProvider"/>,
/// <see cref="ValidatingTrellisSigningKeyProvider"/>, and the end-to-end runtime rotation story
/// (mint + JWKS observing a ring that changes WITHOUT reconstructing the minter). These prove the
/// dynamic half of the contract; the static-config path stays covered by the existing minter /
/// discovery / options-validator suites.
/// </summary>
public sealed class TrellisSigningKeyProviderTests
{
    // === TrellisSigningKeyRing.FromActiveAndPrevious ===

    [Fact]
    public void FromActiveAndPrevious_PublishesCurrentKeyFirstThenPreviousInOrder()
    {
        var current = NewRsaCredentials("active-1");
        var prev1 = NewRsaKey("prev-1");
        var prev2 = NewRsaKey("prev-2");

        var ring = TrellisSigningKeyRing.FromActiveAndPrevious(current, [prev1, prev2]);

        ring.Current.Should().BeSameAs(current);
        ring.ValidationKeys.Select(k => k.KeyId).Should().Equal(["active-1", "prev-1", "prev-2"],
            "the active key's public component is published first, then each previous key — matching the pre-provider JWKS ordering");
    }

    [Fact]
    public void Ring_ValidationKeys_AreDefensivelyCopied_SoLaterSourceMutationDoesNotChangeSnapshot()
    {
        var current = NewRsaCredentials("k1");
        var mutableSource = new List<SecurityKey> { current.Key };
        var ring = new TrellisSigningKeyRing { Current = current, ValidationKeys = mutableSource };

        mutableSource.Add(NewRsaKey("k2")); // mutate the ORIGINAL list after constructing the ring

        ring.ValidationKeys.Select(k => k.KeyId).Should().Equal(["k1"],
            "the ring must defensively copy ValidationKeys so post-construction mutation of the source list cannot change a snapshot the pipeline may have already validated");
    }

    // === TrellisSigningKeyRingValidator ===

    [Fact]
    public void Validate_ValidSingleKeyRing_NoFailures()
    {
        var ring = NewRing("active-1");

        TrellisSigningKeyRingValidator.Validate(ring).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidRotationRing_NoFailures()
    {
        var ring = TrellisSigningKeyRing.FromActiveAndPrevious(NewRsaCredentials("active-1"), [NewRsaKey("prev-1")]);

        TrellisSigningKeyRingValidator.Validate(ring).Should().BeEmpty();
    }

    [Fact]
    public void Validate_CurrentKeyMissingKid_Fails()
    {
        var keyNoKid = new RsaSecurityKey(RSA.Create(2048));
        var ring = new TrellisSigningKeyRing
        {
            Current = new SigningCredentials(keyNoKid, SecurityAlgorithms.RsaSha256),
            ValidationKeys = [keyNoKid],
        };

        TrellisSigningKeyRingValidator.Validate(ring).Should().Contain(f => f.Contains("kid", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SymmetricKeyInRing_Fails()
    {
        var current = NewRsaCredentials("active-1");
        var symmetric = new SymmetricSecurityKey(new byte[64]) { KeyId = "sym-1" };
        var ring = new TrellisSigningKeyRing { Current = current, ValidationKeys = [current.Key, symmetric] };

        TrellisSigningKeyRingValidator.Validate(ring).Should().Contain(f => f.Contains("symmetric", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_JsonWebKeyOctInRing_RejectedAsSymmetric()
    {
        var current = NewRsaCredentials("active-1");
        var octJwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.Octet,
            K = Convert.ToBase64String(new byte[32]),
            KeyId = "oct-1",
        };
        var ring = new TrellisSigningKeyRing { Current = current, ValidationKeys = [current.Key, octJwk] };

        TrellisSigningKeyRingValidator.Validate(ring).Should().Contain(f => f.Contains("symmetric", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_HmacCurrentAlgorithm_Fails()
    {
        var key = NewRsaKey("active-1");
        var ring = new TrellisSigningKeyRing
        {
            Current = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            ValidationKeys = [key],
        };

        TrellisSigningKeyRingValidator.Validate(ring).Should().Contain(f => f.Contains("HMAC", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UnsupportedX509KeyType_Fails()
    {
        using var rsa = RSA.Create(2048);
        var cert = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        var x509 = new X509SecurityKey(cert) { KeyId = "x509-1" };
        var ring = new TrellisSigningKeyRing
        {
            Current = new SigningCredentials(x509, SecurityAlgorithms.RsaSha256),
            ValidationKeys = [x509],
        };

        TrellisSigningKeyRingValidator.Validate(ring).Should().Contain(f => f.Contains("RsaSecurityKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateKidInValidationKeys_Fails()
    {
        var current = NewRsaCredentials("active-1");
        var collidingPrev = NewRsaKey("active-1");   // same kid as the active key
        var ring = new TrellisSigningKeyRing { Current = current, ValidationKeys = [current.Key, collidingPrev] };

        TrellisSigningKeyRingValidator.Validate(ring).Should().Contain(f => f.Contains("collides", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CurrentKidNotPublished_Fails()
    {
        // Current signs with active-1 but ValidationKeys only publishes some other key: signing
        // with an unpublished kid fails ALL downstream validation (TryAllIssuerSigningKeys=false).
        var current = NewRsaCredentials("active-1");
        var ring = new TrellisSigningKeyRing { Current = current, ValidationKeys = [NewRsaKey("only-other")] };

        TrellisSigningKeyRingValidator.Validate(ring).Should()
            .Contain(f => f.Contains("not present in ValidationKeys", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CurrentKidPublishedTwice_Fails()
    {
        var current = NewRsaCredentials("active-1");
        var dupPublic = NewRsaKey("active-1");
        var ring = new TrellisSigningKeyRing { Current = current, ValidationKeys = [current.Key, dupPublic] };

        TrellisSigningKeyRingValidator.Validate(ring).Should()
            .Contain(f => f.Contains("exactly once", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CurrentKidPublishedWithDifferentKeyMaterial_Fails()
    {
        // Same kid, different RSA key material: the minter would sign with one key while JWKS
        // publishes another key's public component under that kid, so every newly minted token
        // would fail downstream validation. Catch it at ring validation time.
        var current = NewRsaCredentials("k1");
        var differentKeySameKid = NewRsaKey("k1");
        var ring = new TrellisSigningKeyRing { Current = current, ValidationKeys = [differentKeySameKid] };

        TrellisSigningKeyRingValidator.Validate(ring).Should()
            .Contain(f => f.Contains("DIFFERENT key material", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CurrentKeyAlgorithmMismatch_Fails()
    {
        // RSA key paired with an EC algorithm is structurally "asymmetric + non-HMAC" but throws at
        // sign time; catch it at validation so it can't poison the last-known-good ring.
        var rsaKey = NewRsaKey("k1");
        var ring = new TrellisSigningKeyRing
        {
            Current = new SigningCredentials(rsaKey, SecurityAlgorithms.EcdsaSha256),
            ValidationKeys = [rsaKey],
        };

        TrellisSigningKeyRingValidator.Validate(ring).Should()
            .Contain(f => f.Contains("not usable with the Current.Key type", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CurrentKeyWithNonSigningRsaAlgorithm_Fails()
    {
        // RSA-OAEP is an RSA ENCRYPTION algorithm, not a JWT signature algorithm; it is structurally
        // asymmetric + non-HMAC but throws at sign time. Must be rejected.
        var rsaKey = NewRsaKey("k1");
        var ring = new TrellisSigningKeyRing
        {
            Current = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaOAEP),
            ValidationKeys = [rsaKey],
        };

        TrellisSigningKeyRingValidator.Validate(ring).Should()
            .Contain(f => f.Contains("not usable with the Current.Key type", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NullRing_Throws()
    {
        var act = () => TrellisSigningKeyRingValidator.Validate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // === ValidatingTrellisSigningKeyProvider ===

    [Fact]
    public void GetCurrentRing_ValidRing_ReturnsIt()
    {
        var ring = NewRing("active-1");
        var provider = NewValidating(new MutableSigningKeyProvider(ring));

        provider.GetCurrentRing().Should().BeSameAs(ring);
    }

    [Fact]
    public void GetCurrentRing_FirstRingInvalid_ThrowsFailClosed()
    {
        var invalid = new TrellisSigningKeyRing
        {
            Current = NewRsaCredentials("active-1"),
            ValidationKeys = [NewRsaKey("only-other")],   // current kid not published
        };
        var provider = NewValidating(new MutableSigningKeyProvider(invalid));

        var act = () => provider.GetCurrentRing();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no previously validated ring to fall back to*");
    }

    [Fact]
    public void GetCurrentRing_InvalidRingAfterValid_ReturnsLastKnownGood()
    {
        var good = NewRing("active-1");
        var inner = new MutableSigningKeyProvider(good);
        var provider = NewValidating(inner);

        provider.GetCurrentRing().Should().BeSameAs(good);

        // The provider now yields a structurally invalid ring (current kid not published).
        inner.Set(new TrellisSigningKeyRing
        {
            Current = NewRsaCredentials("rotated-2"),
            ValidationKeys = [NewRsaKey("only-other")],
        });

        provider.GetCurrentRing().Should().BeSameAs(good,
            "a botched rotation must fall back to the last known-good ring, never take the gateway down");
    }

    [Fact]
    public void GetCurrentRing_InnerReturnsNull_FirstCall_ThrowsFailClosed()
    {
        var provider = NewValidating(new MutableSigningKeyProvider(null!));

        var act = () => provider.GetCurrentRing();

        act.Should().Throw<InvalidOperationException>();
    }

    // === StaticTrellisSigningKeyProvider ===

    [Fact]
    public void StaticProvider_ProjectsOptionsSigningCredentialsIntoRing()
    {
        var options = NewOptions("static-1", previousKids: ["prev-1"]);
        var provider = new StaticTrellisSigningKeyProvider(options);

        var ring = provider.GetCurrentRing();
        ring.Current.Key.KeyId.Should().Be("static-1");
        ring.ValidationKeys.Select(k => k.KeyId).Should().Equal(["static-1", "prev-1"]);
    }

    [Fact]
    public void StaticProvider_ReturnsSameInstanceEachCall_EnablesReferenceShortCircuit()
    {
        var provider = new StaticTrellisSigningKeyProvider(NewOptions("static-1"));

        provider.GetCurrentRing().Should().BeSameAs(provider.GetCurrentRing());
    }

    // === Minter reads the provider ===

    [Fact]
    public void Minter_SignsWithProviderCurrentKey_NotStaticOptionsKey()
    {
        // Options carry kid "options-1"; the provider's ring carries kid "provider-1". The minter
        // MUST sign with the provider's current key — that's what makes runtime rotation take effect.
        var options = NewOptions("options-1");
        var provider = NewValidating(new MutableSigningKeyProvider(NewRing("provider-1")));
        var minter = new TrellisActorJwtMinter(options, provider, NewClock());

        var result = minter.MintFor(NewActor(), NewCluster("incidents"));

        result.Kid.Should().Be("provider-1");
        new JsonWebTokenHandler().ReadJsonWebToken(result.CompactJws).Kid.Should().Be("provider-1");
    }

    // === End-to-end runtime rotation (headline) ===

    [Fact]
    public async Task Rotation_PrepublishThenFlip_OldTokenStillValidatesWhileOverlapping()
    {
        var k1 = NewRsaKey("k1");
        var k2 = NewRsaKey("k2");
        var k1Credentials = new SigningCredentials(k1, SecurityAlgorithms.RsaSha256);
        var k2Credentials = new SigningCredentials(k2, SecurityAlgorithms.RsaSha256);

        var inner = new MutableSigningKeyProvider(TrellisSigningKeyRing.FromActiveAndPrevious(k1Credentials, []));
        var provider = NewValidating(inner);

        // One minter instance for the whole rotation — never reconstructed.
        var minter = new TrellisActorJwtMinter(NewOptions("ignored"), provider, NewClock());

        // Phase 0 — signing with k1, JWKS publishes only k1.
        var token1 = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;
        new JsonWebTokenHandler().ReadJsonWebToken(token1).Kid.Should().Be("k1");
        JwksKids(provider.GetCurrentRing()).Should().Equal(["k1"]);

        // Phase 1 — PRE-PUBLISH k2 while still signing with k1 (the key rotation invariant:
        // publish the new key in JWKS BEFORE signing with it).
        inner.Set(new TrellisSigningKeyRing { Current = k1Credentials, ValidationKeys = [k1, k2] });
        var token2 = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;
        new JsonWebTokenHandler().ReadJsonWebToken(token2).Kid.Should().Be("k1",
            "signing must NOT flip to k2 during the pre-publish phase");
        JwksKids(provider.GetCurrentRing()).Should().BeEquivalentTo(["k1", "k2"], "k2 must be published before the flip");

        // Phase 2 — FLIP the signer to k2, keeping k1 in JWKS for the overlap window.
        inner.Set(new TrellisSigningKeyRing { Current = k2Credentials, ValidationKeys = [k2, k1] });
        var token3 = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;
        new JsonWebTokenHandler().ReadJsonWebToken(token3).Kid.Should().Be("k2",
            "after the flip, new tokens are signed with k2 — no minter reconstruction required");
        JwksKids(provider.GetCurrentRing()).Should().BeEquivalentTo(["k2", "k1"], "k1 stays published during the overlap window");

        // Overlap proof — the token minted under k1 in phase 0 STILL validates against k1, which is
        // still published, so in-flight requests are not dropped mid-rotation.
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(token1, new TokenValidationParameters
        {
            IssuerSigningKey = k1,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
        });
        validation.IsValid.Should().BeTrue("a token minted under k1 must still validate while k1 remains in JWKS");
    }

    // === Fixtures ===

    private static ValidatingTrellisSigningKeyProvider NewValidating(ITrellisSigningKeyProvider inner) =>
        new(inner, NullLogger<ValidatingTrellisSigningKeyProvider>.Instance);

    private static TrellisSigningKeyRing NewRing(string kid) =>
        TrellisSigningKeyRing.FromActiveAndPrevious(NewRsaCredentials(kid), []);

    private static SigningCredentials NewRsaCredentials(string kid) =>
        new(NewRsaKey(kid), SecurityAlgorithms.RsaSha256);

    private static RsaSecurityKey NewRsaKey(string kid) =>
        new(RSA.Create(2048)) { KeyId = kid };

    private static IOptions<TrellisActorForwardingOptions> NewOptions(string activeKid, string[]? previousKids = null)
        => Options.Create(new TrellisActorForwardingOptions
        {
            Issuer = "https://gateway.internal",
            SigningCredentials = NewRsaCredentials(activeKid),
            PreviousSigningKeys = (previousKids ?? []).Select(k => (SecurityKey)NewRsaKey(k)).ToList(),
            PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute),
        });

    private static FakeTimeProvider NewClock() =>
        new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    private static Actor NewActor() =>
        new("user-42",
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static ClusterConfig NewCluster(string clusterId) => new() { ClusterId = clusterId };

    private static List<string> JwksKids(TrellisSigningKeyRing ring)
    {
        var jwks = TrellisDiscoveryEndpointRouteBuilderExtensions.BuildJwks(ring);
        return jwks["keys"]!.AsArray().Select(k => k!["kid"]!.GetValue<string>()).ToList();
    }

    /// <summary>Test provider whose ring can be swapped at runtime to simulate rotation.</summary>
    private sealed class MutableSigningKeyProvider(TrellisSigningKeyRing initial) : ITrellisSigningKeyProvider
    {
        private volatile TrellisSigningKeyRing _ring = initial;
        public void Set(TrellisSigningKeyRing ring) => _ring = ring;
        public TrellisSigningKeyRing GetCurrentRing() => _ring;
    }
}
