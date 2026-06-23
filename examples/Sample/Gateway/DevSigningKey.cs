namespace Gateway;

using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Dev-only helper that returns the sample gateway's RSA signing key, optionally persisting it so
/// locally minted tokens survive a gateway restart.
/// </summary>
public static class DevSigningKey
{
    /// <summary>
    /// Returns an <see cref="RsaSecurityKey"/> for signing the sample gateway's internal JWTs.
    /// When <paramref name="path"/> is null or empty, a fresh ephemeral key is generated on each call
    /// (the zero-config default). When <paramref name="path"/> is set, the key is loaded from that file
    /// if it exists, otherwise generated and saved there — so the <c>kid</c> (derived from the public-key
    /// hash) stays stable across restarts and tokens minted before a restart keep validating.
    /// <para>
    /// <b>Dev only.</b> An unencrypted private key on disk is never appropriate for production; real
    /// deployments load signing material from a secret store and rotate via <c>PreviousSigningKeys</c>
    /// (see the microservices cookbook key-rotation runbook).
    /// </para>
    /// </summary>
    /// <param name="path">Optional file path to persist/load the PEM-encoded private key.</param>
    /// <returns>The signing key, with a stable <see cref="SecurityKey.KeyId"/> when persisted.</returns>
    public static RsaSecurityKey LoadOrCreate(string? path)
    {
        RSA rsa;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(path));
        }
        else
        {
            rsa = RSA.Create(2048);
            if (!string.IsNullOrWhiteSpace(path))
                File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        }

        // kid derived from a hash of the public key bytes so a persisted key keeps a stable kid across
        // restarts (and a fresh key gets a fresh kid) — matches the gateway's JWKS-cache-miss recovery.
        var publicKeyHash = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
        var kid = Convert.ToHexString(publicKeyHash, 0, 8);
        return new RsaSecurityKey(rsa) { KeyId = $"sample-key-{kid}" };
    }
}
