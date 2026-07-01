namespace Trellis.Yarp;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="IReverseProxyBuilder"/> extensions that wire the Trellis actor-forwarding
/// transform pipeline. Pair with the consumer-side
/// <c>TrellisInternalJwtActorProvider</c> in <c>Trellis.Microservices.AspNetCore</c> and the
/// strict <c>AddJwtBearer</c> profile in microservices cookbook Recipe 1.
/// </summary>
public static class TrellisActorForwardingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trellis actor-forwarding YARP transform pipeline. Every request
    /// flowing through YARP's reverse proxy gets a per-cluster transform that hydrates
    /// the current <see cref="Authorization.Actor"/> from the registered
    /// <see cref="Authorization.IActorProvider"/> and mints a fresh internal JWT carrying
    /// the actor surface for the destination cluster. The upstream <c>Authorization</c>
    /// header is overwritten with the gateway-minted JWT.
    /// </summary>
    /// <param name="builder">The YARP reverse-proxy builder.</param>
    /// <param name="configure">Configures the forwarding options. The configured action runs at
    /// startup; <see cref="TrellisActorForwardingOptionsValidator"/> then validates the
    /// resulting options via <c>ValidateOnStart()</c>, so misconfiguration fails the host
    /// at startup rather than per request.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Caller responsibility — register IActorProvider.</b> This extension does NOT
    /// register an <see cref="Authorization.IActorProvider"/>. The gateway typically
    /// uses <c>AddClaimsActorProvider</c> or <c>AddEntraActorProvider</c> from
    /// <c>Trellis.Asp</c> to hydrate the actor from the upstream JWT (the JWT the
    /// gateway validated at its boundary). Calling this extension WITHOUT having
    /// registered an actor provider <b>fails at host startup</b>:
    /// <c>AddTrellisActorForwarding</c> registers
    /// <see cref="TrellisActorForwardingRegistrationValidator"/> as an
    /// <see cref="Microsoft.Extensions.Hosting.IHostedLifecycleService"/> that throws
    /// <see cref="InvalidOperationException"/> in <c>StartingAsync</c> if no
    /// <see cref="Authorization.IActorProvider"/> is registered. That turns what would
    /// otherwise be a per-request "no service registered" error into a clear startup
    /// failure pointing at the exact misconfiguration.
    /// </para>
    /// <para>
    /// <b>YARP composition.</b> Place this call immediately after
    /// <c>services.AddReverseProxy().LoadFromConfig(...)</c> and before
    /// <c>app.MapReverseProxy()</c>. The transform pipeline is built once per cluster at
    /// startup; per-request work resolves a scoped <see cref="Authorization.IActorProvider"/>
    /// and the singleton <see cref="TrellisActorJwtMinter"/>.
    /// </para>
    /// </remarks>
    public static IReverseProxyBuilder AddTrellisActorForwarding(
        this IReverseProxyBuilder builder,
        Action<TrellisActorForwardingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return AddCore(builder, configure, static sp =>
            new StaticTrellisSigningKeyProvider(
                sp.GetRequiredService<IOptions<TrellisActorForwardingOptions>>()),
            usesCustomProvider: false);
    }

    /// <summary>
    /// Registers the Trellis actor-forwarding transform pipeline with a custom
    /// <see cref="ITrellisSigningKeyProvider"/>, enabling runtime signing-key rotation without a
    /// gateway redeploy (e.g. sourcing keys from a vault / KMS refreshed on a background cadence).
    /// The provider is wrapped in fail-closed validation: every ring it returns is re-checked
    /// (asymmetric-only, unique non-empty <c>kid</c>s, current key published) before the minter
    /// signs or the JWKS endpoint publishes it, and an invalid ring falls back to the last
    /// known-good rather than taking the gateway down.
    /// </summary>
    /// <param name="builder">The YARP reverse-proxy builder.</param>
    /// <param name="configure">Configures the forwarding options (issuer, audience, lifetime,
    /// projections, public base URL). The static <c>SigningCredentials</c> / <c>PreviousSigningKeys</c>
    /// on the options are ignored when a custom provider is supplied — the provider is the source
    /// of truth for the signing-key ring.</param>
    /// <param name="signingKeyProviderFactory">Factory that resolves the custom signing-key
    /// provider from the service provider (typically a singleton that caches a ring refreshed off
    /// the hot path). MUST NOT be null.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IReverseProxyBuilder AddTrellisActorForwarding(
        this IReverseProxyBuilder builder,
        Action<TrellisActorForwardingOptions> configure,
        Func<IServiceProvider, ITrellisSigningKeyProvider> signingKeyProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(signingKeyProviderFactory);

        return AddCore(builder, configure, signingKeyProviderFactory, usesCustomProvider: true);
    }

    private static IReverseProxyBuilder AddCore(
        IReverseProxyBuilder builder,
        Action<TrellisActorForwardingOptions> configure,
        Func<IServiceProvider, ITrellisSigningKeyProvider> innerProviderFactory,
        bool usesCustomProvider)
    {
        var optionsBuilder = builder.Services
            .AddOptions<TrellisActorForwardingOptions>()
            .Configure(configure);

        // A custom signing-key provider owns the ring (validated at runtime by the decorator), so
        // startup validation must NOT require the static SigningCredentials / PreviousSigningKeys —
        // they are ignored on that path. Set the flag UNCONDITIONALLY (not only when true) so it
        // follows the same last-call-wins semantics as the RemoveAll/AddSingleton provider
        // registration below: a later static-overload call re-enables static-key validation.
        optionsBuilder.Configure(o => o.UsesCustomSigningKeyProvider = usesCustomProvider);

        optionsBuilder.ValidateOnStart();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TrellisActorForwardingOptions>,
            TrellisActorForwardingOptionsValidator>());

        builder.Services.TryAddSingleton(TimeProvider.System);

        // The consumer-facing ITrellisSigningKeyProvider MUST be the validating decorator so every
        // ring — static default or dynamic custom — is re-validated fail-closed before the minter
        // signs or the JWKS endpoint publishes it. RemoveAll + AddSingleton (NOT TryAddSingleton)
        // guarantees the decorator wins even if a consumer pre-registered a raw
        // ITrellisSigningKeyProvider directly, which would otherwise bypass validation. Custom
        // providers MUST be supplied through the signingKeyProviderFactory overload (routed through
        // the decorator here), never registered as ITrellisSigningKeyProvider.
        builder.Services.RemoveAll<ITrellisSigningKeyProvider>();
        builder.Services.AddSingleton<ITrellisSigningKeyProvider>(sp =>
            new ValidatingTrellisSigningKeyProvider(
                innerProviderFactory(sp),
                sp.GetRequiredService<ILogger<ValidatingTrellisSigningKeyProvider>>()));

        builder.Services.TryAddSingleton<TrellisActorJwtMinter>();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService,
            TrellisActorForwardingRegistrationValidator>());

        builder.AddTransforms<TrellisActorForwardingTransformProvider>();

        return builder;
    }
}
