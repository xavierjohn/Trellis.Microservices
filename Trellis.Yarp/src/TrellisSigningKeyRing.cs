namespace Trellis.Yarp;

using System;
using System.Collections.Generic;
using global::Microsoft.IdentityModel.Tokens;

/// <summary>
/// Immutable snapshot of the gateway's signing-key rotation ring at a point in time: the
/// single credential used to sign new tokens (<see cref="Current"/>) plus every key
/// published in JWKS for downstream validation (<see cref="ValidationKeys"/>).
/// </summary>
/// <remarks>
/// <para>
/// This type is the unit an <see cref="ITrellisSigningKeyProvider"/> returns. Reading the
/// current signer and the publication set as ONE immutable object (rather than two
/// independent getters) is what makes rotation safe under concurrency: the minter and the
/// JWKS endpoint always observe a consistent (<see cref="Current"/>, <see cref="ValidationKeys"/>)
/// pair, never a torn read where the JWT header <c>kid</c> disagrees with the published key set.
/// </para>
/// <para>
/// <b>Publication invariant.</b> The public component of <see cref="Current"/>'s key MUST
/// appear in <see cref="ValidationKeys"/> exactly once (matched by <c>kid</c>). Signing with a
/// key whose <c>kid</c> is not published fails downstream validation for every token
/// (consumers pin <c>TryAllIssuerSigningKeys = false</c>). The runtime provider pipeline
/// (<see cref="ITrellisSigningKeyProvider"/> + validation) enforces this before any key in a
/// snapshot is used to sign or publish; a snapshot that violates it is rejected fail-closed.
/// </para>
/// <para>
/// <b>Asymmetric only.</b> Every key in <see cref="ValidationKeys"/> is published in JWKS, so
/// all keys MUST be <see cref="RsaSecurityKey"/> (the contract pins RS256)
/// with a non-empty, unique <c>kid</c>. Symmetric keys / HMAC algorithms are rejected —
/// publishing them would leak the signing secret.
/// </para>
/// </remarks>
public sealed class TrellisSigningKeyRing
{
    /// <summary>
    /// The credential used to sign newly minted tokens. Its <see cref="SigningCredentials.Key"/>
    /// MUST be asymmetric with a non-empty <c>kid</c>, and its public component MUST be present
    /// in <see cref="ValidationKeys"/>.
    /// </summary>
    public required SigningCredentials Current { get; init; }

    /// <summary>
    /// Every key to publish in the JWKS document for downstream validation — the current key
    /// plus any retiring keys still inside their rotation overlap window. MUST include the
    /// public component of <see cref="Current"/>'s key (matched by <c>kid</c>). Every entry
    /// MUST be asymmetric with a unique, non-empty <c>kid</c>.
    /// </summary>
    /// <remarks>
    /// Defensively copied on assignment so the snapshot is structurally immutable: a provider that
    /// keeps and later mutates the original collection cannot change a ring the pipeline has already
    /// validated (the validating decorator short-circuits re-validation by reference).
    /// </remarks>
    public required IReadOnlyList<SecurityKey> ValidationKeys
    {
        get => _validationKeys;
        init => _validationKeys = value is null ? [] : [.. value];
    }

    private readonly IReadOnlyList<SecurityKey> _validationKeys = [];

    /// <summary>
    /// Builds a ring from a single active signing credential plus zero or more retiring
    /// validation keys, projecting the legacy
    /// <see cref="TrellisActorForwardingOptions.SigningCredentials"/> +
    /// <see cref="TrellisActorForwardingOptions.PreviousSigningKeys"/> shape onto the ring model.
    /// The active key's public component is published first, then each previous key — matching
    /// the JWKS ordering the static configuration produced before the provider seam existed.
    /// </summary>
    /// <param name="current">The active signing credential.</param>
    /// <param name="previous">Retiring keys still trusted during the overlap window (published, never used to sign).</param>
    /// <returns>An immutable ring snapshot.</returns>
    public static TrellisSigningKeyRing FromActiveAndPrevious(
        SigningCredentials current,
        IReadOnlyList<SecurityKey> previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var validationKeys = new List<SecurityKey>(previous.Count + 1) { current.Key };
        validationKeys.AddRange(previous);

        return new TrellisSigningKeyRing
        {
            Current = current,
            ValidationKeys = validationKeys,
        };
    }
}
