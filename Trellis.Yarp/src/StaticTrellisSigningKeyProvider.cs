namespace Trellis.Yarp;

using System;
using Microsoft.Extensions.Options;

/// <summary>
/// Default <see cref="ITrellisSigningKeyProvider"/> registered by
/// <see cref="TrellisActorForwardingServiceCollectionExtensions.AddTrellisActorForwarding(Microsoft.Extensions.DependencyInjection.IReverseProxyBuilder, System.Action{TrellisActorForwardingOptions})"/>
/// when no custom provider is supplied. Projects the static
/// <see cref="TrellisActorForwardingOptions.SigningCredentials"/> +
/// <see cref="TrellisActorForwardingOptions.PreviousSigningKeys"/> into an immutable ring,
/// preserving the pre-provider behavior exactly. The ring is captured once — the static options
/// never change after startup validation, so the same snapshot instance is returned on every
/// call (which lets the validating decorator short-circuit re-validation by reference).
/// </summary>
internal sealed class StaticTrellisSigningKeyProvider : ITrellisSigningKeyProvider
{
    private readonly TrellisSigningKeyRing _ring;

    public StaticTrellisSigningKeyProvider(IOptions<TrellisActorForwardingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        _ring = TrellisSigningKeyRing.FromActiveAndPrevious(value.SigningCredentials, value.PreviousSigningKeys);
    }

    /// <inheritdoc />
    public TrellisSigningKeyRing GetCurrentRing() => _ring;
}
