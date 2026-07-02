namespace Trellis.Yarp;

using System;
using System.Collections.Generic;
using global::Microsoft.IdentityModel.Tokens;

/// <summary>
/// Validates a <see cref="TrellisSigningKeyRing"/> returned by an
/// <see cref="ITrellisSigningKeyProvider"/> at runtime, applying the SAME key constraints the
/// startup options validator applies to the static configuration — asymmetric-only, non-empty
/// unique <c>kid</c>s — plus the ring-level publication invariant (the current signer's
/// <c>kid</c> is published exactly once in <see cref="TrellisSigningKeyRing.ValidationKeys"/>).
/// A provider can hand the pipeline a fresh key set at any moment, bypassing
/// <c>ValidateOnStart</c>; this is where those keys are re-checked before they are used to sign
/// or publish.
/// </summary>
/// <remarks>
/// The current signer's key/algorithm compatibility IS enforced (an RSA key with an EC algorithm,
/// or vice-versa, is rejected before it can poison the last-known-good ring). Algorithm-family
/// uniformity across the PUBLISHED ring (every <see cref="TrellisSigningKeyRing.ValidationKeys"/>
/// entry sharing the active family) is not separately enforced — the JWKS builder normalizes every
/// key's <c>alg</c> to the active <see cref="TrellisSigningKeyRing.Current"/> algorithm and v1
/// assumes rotation stays within a single algorithm family.
/// </remarks>
internal static class TrellisSigningKeyRingValidator
{
    /// <summary>
    /// Validates <paramref name="ring"/> and returns the list of human-readable failures
    /// (empty when the ring is valid). Never throws for ring-content problems — the caller
    /// decides the fail-closed policy (serve last known-good vs. throw).
    /// </summary>
    /// <param name="ring">The ring snapshot to validate.</param>
    /// <returns>Zero or more failure messages.</returns>
    public static IReadOnlyList<string> Validate(TrellisSigningKeyRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        var failures = new List<string>();

        var current = ring.Current;
        if (current is null)
        {
            failures.Add("Current is required — an asymmetric SigningCredentials with a non-empty kid whose public key is published in ValidationKeys.");
        }
        else
        {
            ValidateKey(current.Key, "Current.Key", failures);
            if (TrellisSigningKeyValidation.IsSymmetricAlgorithm(current.Algorithm))
                failures.Add($"Current.Algorithm '{current.Algorithm}' is an HMAC (symmetric) algorithm; the ring is published in JWKS and MUST use an asymmetric algorithm (RS256/RS384/RS512 or ES256/ES384/ES512).");
            else if (current.Key is not null
                && TrellisSigningKeyValidation.IsSupportedAsymmetricKey(current.Key)
                && !TrellisSigningKeyValidation.IsAlgorithmSupportedForKey(current.Key, current.Algorithm))
                failures.Add($"Current.Algorithm '{current.Algorithm}' is not usable with the Current.Key type ({current.Key.GetType().Name}); an RSA key requires an RSA algorithm (RS256/384/512 or PS256/384/512) and an ECDSA key requires an EC algorithm (ES256/384/512). Signing would fail at mint time and poison the last-known-good ring.");
        }

        if (ring.ValidationKeys is null)
        {
            failures.Add("ValidationKeys must not be null; it MUST contain at least the current signing key's public component.");
            return failures;
        }

        var seenKids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ring.ValidationKeys.Count; i++)
        {
            var key = ring.ValidationKeys[i];
            ValidateKey(key, $"ValidationKeys[{i}]", failures);
            if (key?.KeyId is { Length: > 0 } kid && !seenKids.Add(kid))
                failures.Add($"ValidationKeys[{i}] uses kid '{kid}' which collides with another key in the ring; every published kid MUST be unique so JWKS lookup and audit correlation stay unambiguous.");
        }

        // Publication invariant: the current signer's kid MUST be published exactly once, AND the
        // published key under that kid MUST be the public component of the current signing key.
        // Signing with an unpublished kid (or a kid whose published key material differs) fails ALL
        // downstream validation (consumers pin TryAllIssuerSigningKeys = false); publishing it twice
        // makes JWKS lookup ambiguous.
        if (current?.Key is { KeyId: { Length: > 0 } currentKid } currentKey)
        {
            var publishedCount = 0;
            SecurityKey? matched = null;
            foreach (var key in ring.ValidationKeys)
                if (key?.KeyId is { Length: > 0 } kid && string.Equals(kid, currentKid, StringComparison.Ordinal))
                {
                    publishedCount++;
                    matched ??= key;
                }

            if (publishedCount == 0)
                failures.Add($"Current signing key kid '{currentKid}' is not present in ValidationKeys; signing with an unpublished kid fails ALL downstream token validation (consumers pin TryAllIssuerSigningKeys = false). Publish the current key's public component in the ring.");
            else if (publishedCount > 1)
                failures.Add($"Current signing key kid '{currentKid}' appears {publishedCount} times in ValidationKeys; it MUST be published exactly once so JWKS lookup is unambiguous.");
            else if (matched is not null && !SamePublicKey(currentKey, matched))
                failures.Add($"Current signing key kid '{currentKid}' is published in ValidationKeys but with DIFFERENT key material than the current signing key; the published key under that kid MUST be the public component of the current signing key, otherwise downstream validation fails for every newly minted token.");
        }

        return failures;
    }

    /// <summary>
    /// True when two asymmetric keys share the same PUBLIC component. Compares the public JWK fields
    /// (<c>n</c>/<c>e</c> for RSA, <c>crv</c>/<c>x</c>/<c>y</c> for EC) via the same converter the JWKS
    /// builder uses, so a private-key signer and its published public key are treated as a match while
    /// a same-kid/different-key mismatch is caught. Both keys are validated
    /// <see cref="RsaSecurityKey"/>/<see cref="ECDsaSecurityKey"/> by the time this runs; a converter
    /// failure (should not happen) is treated as "cannot confirm" → fail closed.
    /// </summary>
    private static bool SamePublicKey(SecurityKey current, SecurityKey published)
    {
        try
        {
            var a = JsonWebKeyConverter.ConvertFromSecurityKey(current);
            var b = JsonWebKeyConverter.ConvertFromSecurityKey(published);
            return string.Equals(a.Kty, b.Kty, StringComparison.Ordinal)
                && string.Equals(a.N, b.N, StringComparison.Ordinal)
                && string.Equals(a.E, b.E, StringComparison.Ordinal)
                && string.Equals(a.Crv, b.Crv, StringComparison.Ordinal)
                && string.Equals(a.X, b.X, StringComparison.Ordinal)
                && string.Equals(a.Y, b.Y, StringComparison.Ordinal);
        }
        catch (Exception ex) when (
            ex is NotSupportedException
                or ArgumentException
                or System.Security.Cryptography.CryptographicException
                or ObjectDisposedException)
        {
            // Cannot confirm the public material (unsupported curve, broken / disposed key handle,
            // converter refusal, etc.). Fail closed: treat as "not a match" so the ring is rejected
            // and the validating decorator serves the last known-good ring rather than letting the
            // exception escape Validate and take the mint / JWKS path down.
            return false;
        }
    }

    private static void ValidateKey(SecurityKey? key, string context, List<string> failures)
    {
        if (key is null)
        {
            failures.Add($"{context} is null; every key in the ring MUST be a non-null asymmetric key with a non-empty kid.");
            return;
        }

        if (string.IsNullOrEmpty(key.KeyId))
            failures.Add($"{context}.KeyId (the 'kid') must be a non-empty string — downstream services resolve keys by kid during rotation.");

        if (TrellisSigningKeyValidation.IsSymmetric(key))
            failures.Add($"{context} is a symmetric key ({key.GetType().Name}{TrellisSigningKeyValidation.DescribeJwkKty(key)}); the ring is published in JWKS and MUST be asymmetric — publishing symmetric key material would leak the signing secret.");
        else if (!TrellisSigningKeyValidation.IsSupportedAsymmetricKey(key))
            failures.Add($"{context} is a {key.GetType().Name}; only RsaSecurityKey and ECDsaSecurityKey are supported in the ring. Unwrap X509SecurityKey / JsonWebKey to RsaSecurityKey or ECDsaSecurityKey before publishing.");
    }
}
