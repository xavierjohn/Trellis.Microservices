namespace Trellis.Microservices.AspNetCore;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Trellis.Authorization;

/// <summary>
/// Extension methods for registering the Trellis internal-JWT actor provider in ASP.NET Core DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TrellisInternalJwtActorProvider"/> as the scoped
    /// <see cref="IActorProvider"/> for microservices that consume internal-network JWTs
    /// minted by a trusted gateway (typically <c>Trellis.Yarp</c> or an equivalent
    /// third-party gateway implementing the same contract). Hydrates the full
    /// <see cref="Actor"/> surface — including <see cref="Actor.ForbiddenPermissions"/>
    /// and <see cref="Actor.Attributes"/> the stock <c>ClaimsActorProvider</c> (in
    /// <c>Trellis.Asp.Authorization</c>) intentionally omits.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="TrellisInternalJwtActorOptions"/>. See the
    /// options class for the full contract — including the sentinel + count claim invariants
    /// that protect the deny-overrides-allow contract against a proxy stripping the deny set.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Replaces</b> any prior <see cref="IActorProvider"/> registration — actor-provider
    /// helpers do not stack. Pair with a strict <c>AddJwtBearer</c> profile
    /// (<c>ValidateIssuer</c>, <c>ValidateAudience</c>, <c>ValidateLifetime</c>,
    /// <c>RequireSignedTokens</c>, tight <c>ClockSkew</c>) on the configured
    /// <see cref="TrellisInternalJwtActorOptions.AuthenticationScheme"/> — the recipe
    /// in the cookbook spells out the canonical configuration.
    /// </para>
    /// <para>
    /// <b>Startup validation</b> runs via
    /// <c>services.AddOptions&lt;TrellisInternalJwtActorOptions&gt;().ValidateOnStart()</c>
    /// using <see cref="TrellisInternalJwtActorOptionsValidator"/>, which catches claim-name
    /// collisions, missing required-attribute mappings, and the registered-JWT-claim-name
    /// privilege-escalation footgun before the first request.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
    /// {
    ///     o.Authority = "https://gateway.internal";
    ///     o.Audience = "incidents-service";
    ///     o.MapInboundClaims = false;     // keep raw JWT claim names — required by this provider
    ///     o.TokenValidationParameters = new TokenValidationParameters
    ///     {
    ///         ValidateIssuer = true, ValidIssuer = "https://gateway.internal",
    ///         ValidateAudience = true, ValidAudience = "incidents-service",
    ///         ValidateLifetime = true, RequireSignedTokens = true,
    ///         ValidAlgorithms = ["RS256"],
    ///         ClockSkew = TimeSpan.FromSeconds(30),
    ///         TryAllIssuerSigningKeys = false,  // honor kid-pinned key resolution
    ///     };
    /// });
    /// builder.Services.AddTrellisInternalJwtActorProvider(o =>
    /// {
    ///     o.RequiredAttributes = ["tenant_id"];
    ///     o.AttributeClaimMap["tenant_id"] = "tid";
    ///     o.ExpectedIssuer = "https://gateway.internal";   // defense-in-depth
    ///     o.ExpectedAudience = "incidents-service";        // defense-in-depth
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisInternalJwtActorProvider(
        this IServiceCollection services,
        Action<TrellisInternalJwtActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        services
            .AddOptions<TrellisInternalJwtActorOptions>()
            .Configure(configure ?? (_ => { }))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TrellisInternalJwtActorOptions>, TrellisInternalJwtActorOptionsValidator>());

        services.Replace(ServiceDescriptor.Scoped<IActorProvider, TrellisInternalJwtActorProvider>());

        return services;
    }
}
