namespace Trellis.Yarp;

using System;
using global::Microsoft.IdentityModel.Tokens;

/// <summary>
/// Shared signing-key classification helpers used by the startup options validator
/// (<see cref="TrellisActorForwardingOptionsValidator"/>), the runtime ring validator
/// (<see cref="TrellisSigningKeyRingValidator"/>), and the JWKS publication path
/// (<see cref="TrellisDiscoveryEndpointRouteBuilderExtensions"/>). Keeping the asymmetric-only /
/// symmetric-detection rules in ONE place ensures the static-config and dynamic-provider paths
/// enforce identical key constraints, so a key accepted at startup and a key accepted at runtime
/// are held to the same bar (no drift).
/// </summary>
internal static class TrellisSigningKeyValidation
{
    /// <summary>
    /// The ONE JWT signature algorithm the Trellis internal-JWT contract supports.
    /// </summary>
    /// <remarks>
    /// This is pinned rather than configurable because it is one half of a two-sided contract:
    /// the default consumer profile (<c>AddTrellisInternalJwtBearer</c>) forces
    /// <c>ValidAlgorithms = ["RS256"]</c> as a non-negotiable invariant. If the gateway accepted
    /// any other algorithm it would mint tokens that every microservice in the fleet rejects —
    /// a fleet-wide outage discoverable only in production, because nothing at startup compares
    /// the two sides. Pinning here makes the mismatch impossible to express.
    /// </remarks>
    public const string RequiredAlgorithm = SecurityAlgorithms.RsaSha256;

    /// <summary>
    /// True when <paramref name="algorithm"/> is exactly <see cref="RequiredAlgorithm"/>.
    /// </summary>
    public static bool IsRequiredAlgorithm(string? algorithm) =>
        string.Equals(algorithm, RequiredAlgorithm, StringComparison.Ordinal);

    /// <summary>
    /// Recognizes symmetric keys, including the <see cref="JsonWebKey"/> wrapper case where the
    /// underlying material is HMAC (<c>kty: "oct"</c>). Without the JWK check a caller could
    /// bypass the asymmetric-only contract by passing
    /// <c>new SigningCredentials(new JsonWebKey(octKtyJson), SecurityAlgorithms.HmacSha256)</c>.
    /// </summary>
    public static bool IsSymmetric(SecurityKey key) =>
        key is SymmetricSecurityKey ||
        (key is JsonWebKey jwk && string.Equals(jwk.Kty, JsonWebAlgorithmsKeyTypes.Octet, StringComparison.Ordinal));

    /// <summary>
    /// HMAC algorithms (HS256/HS384/HS512) are symmetric regardless of the key wrapper type.
    /// Checking the algorithm directly defends against a future <see cref="SecurityKey"/>
    /// subclass that confuses the structural <see cref="IsSymmetric"/> check.
    /// </summary>
    public static bool IsSymmetricAlgorithm(string? algorithm) =>
        algorithm is SecurityAlgorithms.HmacSha256
            or SecurityAlgorithms.HmacSha384
            or SecurityAlgorithms.HmacSha512;

    /// <summary>
    /// The allow-list of asymmetric key types v1 supports: <see cref="RsaSecurityKey"/> only.
    /// </summary>
    /// <remarks>
    /// ECDSA keys are excluded because they cannot produce <see cref="RequiredAlgorithm"/>.
    /// <see cref="X509SecurityKey"/> and <see cref="JsonWebKey"/> wrappers are excluded because
    /// the JWKS builder cannot serialize them into a usable JWK (see the discovery endpoint
    /// remarks); unwrap them to <see cref="RsaSecurityKey"/> first.
    /// </remarks>
    public static bool IsSupportedAsymmetricKey(SecurityKey key) =>
        key is RsaSecurityKey;

    /// <summary>
    /// True when <paramref name="algorithm"/> is <see cref="RequiredAlgorithm"/>, <paramref name="key"/>
    /// belongs to the matching (RSA) family, AND the crypto provider can actually create a signer for
    /// the pair. The explicit family check is required because
    /// <c>CryptoProviderFactory.IsSupportedAlgorithm</c> alone also accepts RSA ENCRYPTION / key-wrap
    /// algorithms (e.g. RSA-OAEP), which are structurally "asymmetric + non-HMAC" yet throw at sign
    /// time. Rejecting mismatches at validation keeps a bad rotation from poisoning the last-known-good
    /// ring and 502-ing every request.
    /// </summary>
    public static bool IsAlgorithmSupportedForKey(SecurityKey key, string? algorithm)
    {
        if (!IsRequiredAlgorithm(algorithm))
            return false;

        return key is RsaSecurityKey && key.CryptoProviderFactory.IsSupportedAlgorithm(algorithm, key);
    }

    /// <summary>
    /// Renders the <c>kty</c> of a <see cref="JsonWebKey"/> for diagnostics (e.g.
    /// <c>, kty="oct"</c>); empty for non-JWK keys.
    /// </summary>
    public static string DescribeJwkKty(SecurityKey key)
        => key is JsonWebKey jwk && !string.IsNullOrEmpty(jwk.Kty)
            ? $", kty=\"{jwk.Kty}\""
            : string.Empty;
}
