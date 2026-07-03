namespace Trellis.Yarp;

/// <summary>
/// Seam that supplies the gateway's current signing-key rotation ring. Implement this to
/// source signing keys from a secret store / KMS / vault / PEM file and rotate them at runtime
/// WITHOUT redeploying the gateway. The default registration projects the static
/// <see cref="TrellisActorForwardingOptions.SigningCredentials"/> +
/// <see cref="TrellisActorForwardingOptions.PreviousSigningKeys"/> so existing configurations
/// are unaffected.
/// </summary>
/// <remarks>
/// <para>
/// <b>No concrete secret-store adapter ships in this package.</b> Sourcing key material from a
/// vault / KMS / file is intentionally an application or community concern; this interface is
/// only the seam plus the multi-key JWKS publication + rotation contract.
/// </para>
/// <para>
/// <b>Atomic snapshot.</b> <see cref="GetCurrentRing"/> is called on the mint hot path (every
/// forwarded request) and on every JWKS / discovery request. It MUST return an immutable
/// <see cref="TrellisSigningKeyRing"/> in a single read and MUST be safe to call concurrently
/// from many threads. Return the SAME instance between rotations so the validating pipeline can
/// short-circuit re-validation by reference; swap to a NEW instance atomically when the ring
/// changes.
/// </para>
/// <para>
/// <b>No I/O on the hot path.</b> Do not fetch from a secret store inside
/// <see cref="GetCurrentRing"/>. Refresh the ring on a background cadence and hand out the
/// cached snapshot here.
/// </para>
/// <para>
/// <b>Fleet convergence.</b> Horizontally scaled gateways behind one issuer / JWKS URL MUST
/// coordinate rotation: no instance may flip <see cref="TrellisSigningKeyRing.Current"/> to a
/// new key until EVERY instance publishes that key in its
/// <see cref="TrellisSigningKeyRing.ValidationKeys"/>, and the overlap window must cover both
/// consumer JWKS-cache convergence and gateway-fleet convergence. This coordination is the
/// provider's responsibility; the core pipeline only validates each snapshot it is handed.
/// </para>
/// <para>
/// <b>Validation is enforced downstream.</b> Whatever snapshot this returns is validated by the
/// pipeline before use (asymmetric-only, non-empty unique <c>kid</c>s, current key published).
/// An invalid snapshot is rejected fail-closed — the last known-good ring keeps serving rather
/// than taking the gateway down.
/// </para>
/// </remarks>
public interface ITrellisSigningKeyProvider
{
    /// <summary>
    /// Returns the current signing-key ring as an atomic, immutable snapshot. Called on the mint
    /// hot path and on every JWKS / discovery request; must be cheap, thread-safe, and free of
    /// I/O (see the interface remarks).
    /// </summary>
    /// <returns>The current <see cref="TrellisSigningKeyRing"/>.</returns>
    TrellisSigningKeyRing GetCurrentRing();
}
