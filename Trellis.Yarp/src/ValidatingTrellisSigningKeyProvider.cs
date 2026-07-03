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
/// changes.
/// </para>
/// <para>
/// <b>Audit redaction.</b> The rejection log records only the failure COUNT — never key material,
/// never the failure messages (which could echo a kid). It signals "a rotation snapshot was
/// rejected; serving last known-good" so operators can alert on it, without leaking anything a
/// low-cardinality audit event must not carry.
/// </para>
/// </remarks>
internal sealed class ValidatingTrellisSigningKeyProvider : ITrellisSigningKeyProvider
{
    private readonly ITrellisSigningKeyProvider _inner;
    private readonly ILogger<ValidatingTrellisSigningKeyProvider> _logger;

    private volatile TrellisSigningKeyRing? _lastValidated;
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
            return HandleInvalid(failureCount: 1);

        // Hot-path short-circuit: the exact instance we already validated needs no re-check.
        if (ReferenceEquals(ring, _lastValidated))
            return ring;

        var failures = TrellisSigningKeyRingValidator.Validate(ring);
        if (failures.Count == 0)
        {
            _lastGood = ring;
            _lastValidated = ring;
            return ring;
        }

        return HandleInvalid(failures.Count);
    }

    private TrellisSigningKeyRing HandleInvalid(int failureCount)
    {
        var lastGood = _lastGood;
        if (lastGood is not null)
        {
            TrellisSigningKeyProviderLog.RejectedRing(_logger, failureCount);
            return lastGood;
        }

        throw new InvalidOperationException(
            $"ITrellisSigningKeyProvider returned an invalid signing-key ring ({failureCount} validation failure(s)) and there is no previously validated ring to fall back to. " +
            "The ring MUST contain asymmetric keys with unique non-empty kids and MUST publish the current signing key's kid exactly once. Fix the provider configuration.");
    }
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
