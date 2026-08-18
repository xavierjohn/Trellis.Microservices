namespace Trellis.Yarp;

using System;
using Microsoft.Extensions.Logging;

/// <summary>
/// Decorates an inner <see cref="ITrellisSigningKeyProvider"/> with fail-closed runtime
/// validation. Every ring the inner provider returns is validated
/// (<see cref="TrellisSigningKeyRingValidator"/>) before the pipeline uses it to sign or publish.
/// A valid ring becomes the new "last known-good"; an INVALID ring is rejected and the last
/// known-good ring keeps serving — a botched rotation must not take the gateway down. If the very
/// first ring is invalid (no known-good exists yet), this throws, surfacing the misconfiguration
/// loudly at first use rather than minting or publishing unvalidated keys.
/// </summary>
/// <remarks>
/// <para>
/// Steady-state cost is a single reference-equality check: a well-behaved provider returns the
/// SAME ring instance between rotations, so full re-validation only runs when the ring actually
/// changes. This holds for REJECTED rings too — a provider stuck returning the same bad instance
/// is validated once, not once per request. Without that, every proxied request would pay full
/// ring validation and emit a Warning, turning a misconfiguration into a CPU and log-volume
/// amplifier (and drowning the surrounding audit trail).
/// </para>
/// <para>
/// <b>Audit redaction.</b> The rejection log records only the failure COUNT — never key material,
/// never the failure messages (which could echo a kid). It signals "a rotation snapshot was
/// rejected; serving last known-good" so operators can alert on it, without leaking anything a
/// low-cardinality audit event must not carry. It is emitted on TRANSITION — once per distinct
/// rejected ring instance — so the alert fires promptly without repeating per request.
/// </para>
/// </remarks>
internal sealed class ValidatingTrellisSigningKeyProvider : ITrellisSigningKeyProvider
{
    private readonly ITrellisSigningKeyProvider _inner;
    private readonly ILogger<ValidatingTrellisSigningKeyProvider> _logger;

    private volatile Verdict? _lastVerdict;
    private volatile TrellisSigningKeyRing? _lastGood;

    public ValidatingTrellisSigningKeyProvider(
        ITrellisSigningKeyProvider inner,
        ILogger<ValidatingTrellisSigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _logger = logger;
    }

    /// <inheritdoc />
    public TrellisSigningKeyRing GetCurrentRing()
    {
        var ring = _inner.GetCurrentRing();
        if (ring is null)
            return Reject(failureCount: 1);

        // Hot-path short-circuit: the exact instance we already judged needs no re-check. The ring
        // and its verdict are published together as one immutable object, so a concurrent rotation
        // can never pair a ring with another ring's failure count.
        var verdict = _lastVerdict;
        if (verdict is not null && ReferenceEquals(ring, verdict.Ring))
        {
            return verdict.FailureCount == 0
                ? ring
                : ServeLastGoodOrThrow(verdict.FailureCount);
        }

        var failures = TrellisSigningKeyRingValidator.Validate(ring);
        if (failures.Count == 0)
        {
            // Publish the fallback BEFORE the verdict. A concurrent caller that observes a verdict
            // must never be able to observe a not-yet-published _lastGood, or it would throw
            // "no known-good ring" at the exact moment one exists.
            _lastGood = ring;
            _lastVerdict = new Verdict(ring, 0);
            return ring;
        }

        var served = Reject(failures.Count);

        // Cache the rejection only AFTER its warning has been emitted. Caching first would let a
        // rejected ring become "already judged" while the warning was skipped (the throw path
        // emits none), permanently silencing the alert for that instance.
        _lastVerdict = new Verdict(ring, failures.Count);
        return served;
    }

    /// <summary>
    /// Serves the last known-good ring and records the rejection, or throws when no known-good
    /// ring exists yet.
    /// </summary>
    private TrellisSigningKeyRing Reject(int failureCount)
    {
        var lastGood = _lastGood ?? throw NoKnownGoodRing(failureCount);
        TrellisSigningKeyProviderLog.RejectedRing(_logger, failureCount);
        return lastGood;
    }

    /// <summary>
    /// Fallback path for an ALREADY-logged rejection. Still throws when no known-good ring exists,
    /// so caching a verdict can never soften the fail-closed guarantee.
    /// </summary>
    private TrellisSigningKeyRing ServeLastGoodOrThrow(int failureCount) =>
        _lastGood ?? throw NoKnownGoodRing(failureCount);

    private static InvalidOperationException NoKnownGoodRing(int failureCount) =>
        new(
            $"ITrellisSigningKeyProvider returned an invalid signing-key ring ({failureCount} validation failure(s)) and there is no previously validated ring to fall back to. " +
            $"Every key in the ring MUST be an RsaSecurityKey with a unique non-empty kid, Current.Algorithm MUST be {TrellisSigningKeyValidation.RequiredAlgorithm} (ECDsa keys and all other algorithms are rejected — the contract is pinned to match the consumer's ValidAlgorithms), " +
            "and the current signing key's kid MUST be published in ValidationKeys exactly once. Fix the provider configuration.");

    /// <summary>
    /// A ring paired with the outcome of validating it. Held in a single volatile field so the
    /// pair is published atomically — reading the ring and its failure count as two independent
    /// fields would let a concurrent rotation interleave them.
    /// </summary>
    private sealed record Verdict(TrellisSigningKeyRing Ring, int FailureCount);
}

/// <summary>
/// Source-generated redacted audit events for <see cref="ValidatingTrellisSigningKeyProvider"/>.
/// Low-cardinality only — never key material, never claim values.
/// </summary>
internal static partial class TrellisSigningKeyProviderLog
{
    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Warning,
        EventName = "TrellisYarpSigningKeyRingRejected",
        Message = "Trellis YARP rejected an invalid signing-key ring from the provider ({FailureCount} validation failure(s)); continuing to serve the last known-good ring")]
    public static partial void RejectedRing(ILogger logger, int failureCount);
}
