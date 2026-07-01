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
    /// The allow-list of asymmetric key types v1 supports: <see cref="RsaSecurityKey"/> and
    /// <see cref="ECDsaSecurityKey"/>. <see cref="X509SecurityKey"/> and <see cref="JsonWebKey"/>
    /// wrappers are rejected because the JWKS builder cannot serialize them into a usable JWK
    /// (see the discovery endpoint remarks).
    /// </summary>
    public static bool IsSupportedAsymmetricKey(SecurityKey key) =>
        key is RsaSecurityKey or ECDsaSecurityKey;

    /// <summary>
    /// True when <paramref name="algorithm"/> is a JWT SIGNATURE algorithm for <paramref name="key"/>'s
    /// family AND the crypto provider can actually create a signer for the pair. The explicit
    /// signing allow-list (RS*/PS* for RSA, ES* for ECDSA) is required because
    /// <c>CryptoProviderFactory.IsSupportedAlgorithm</c> alone also accepts RSA ENCRYPTION / key-wrap
    /// algorithms (e.g. RSA-OAEP), which are structurally "asymmetric + non-HMAC" yet throw at sign
    /// time. Rejecting mismatches at validation keeps a bad rotation from poisoning the last-known-good
    /// ring and 502-ing every request.
    /// </summary>
    public static bool IsAlgorithmSupportedForKey(SecurityKey key, string? algorithm)
    {
        if (string.IsNullOrEmpty(algorithm))
            return false;

        var isSignatureAlgorithmForKeyFamily = key switch
        {
            RsaSecurityKey => algorithm is SecurityAlgorithms.RsaSha256 or SecurityAlgorithms.RsaSha384 or SecurityAlgorithms.RsaSha512
                or SecurityAlgorithms.RsaSsaPssSha256 or SecurityAlgorithms.RsaSsaPssSha384 or SecurityAlgorithms.RsaSsaPssSha512,
            ECDsaSecurityKey => algorithm is SecurityAlgorithms.EcdsaSha256 or SecurityAlgorithms.EcdsaSha384 or SecurityAlgorithms.EcdsaSha512,
            _ => false,
        };

        return isSignatureAlgorithmForKeyFamily && key.CryptoProviderFactory.IsSupportedAlgorithm(algorithm, key);
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
