namespace Trellis.Microservices.AspNetCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// One-call composition of the consumer-side internal-JWT story: the strict
    /// <c>AddJwtBearer</c> validation profile <b>plus</b> <see cref="TrellisInternalJwtActorProvider"/>,
    /// wired to the same <paramref name="issuer"/> / <paramref name="audience"/> / scheme so they cannot
    /// drift apart. This closes the loose-<c>AddJwtBearer</c> footgun — the most common production failure
    /// of the internal-JWT pattern — <b>by construction</b>: the security-critical settings are re-applied
    /// after <paramref name="configureJwtBearer"/> runs, so a consumer cannot weaken them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="issuer">
    /// The gateway issuer. Forced as the validated <c>iss</c> (<c>ValidIssuer</c>) and used as the default
    /// <c>JwtBearerOptions.Authority</c> for JWKS discovery (override <c>Authority</c> — or set it to
    /// <see langword="null"/> and supply <c>IssuerSigningKeys</c> — via <paramref name="configureJwtBearer"/>
    /// for the air-gapped key-ring profile).
    /// </param>
    /// <param name="audience">This service's audience. Forced as the validated <c>aud</c> (<c>ValidAudience</c>).</param>
    /// <param name="configureActor">
    /// Optional delegate to customize <see cref="TrellisInternalJwtActorOptions"/> (for example
    /// <see cref="TrellisInternalJwtActorOptions.RequiredAttributes"/> and
    /// <see cref="TrellisInternalJwtActorOptions.AttributeClaimMap"/>). The scheme,
    /// <see cref="TrellisInternalJwtActorOptions.ExpectedIssuer"/>, and
    /// <see cref="TrellisInternalJwtActorOptions.ExpectedAudience"/> are pre-set from this method's arguments
    /// before the delegate runs.
    /// </param>
    /// <param name="configureJwtBearer">
    /// Optional delegate to adjust the deployment-specific, non-security-critical parts of
    /// <see cref="JwtBearerOptions"/> — <c>Authority</c> / <c>MetadataAddress</c>, <c>RequireHttpsMetadata</c>
    /// (set <see langword="false"/> only for a plaintext in-cluster gateway), <c>IssuerSigningKeys</c> for the
    /// air-gapped profile, or a tighter <c>ClockSkew</c>. The non-negotiable invariants below are re-applied
    /// afterward and cannot be overridden here.
    /// </param>
    /// <param name="authenticationScheme">
    /// The authentication scheme the JWT bearer handler and the actor provider share. Defaults to
    /// <c>"Bearer"</c>; also set as the default authenticate / challenge scheme.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Forced invariants.</b> These are applied in a <c>PostConfigure&lt;JwtBearerOptions&gt;</c> for the
    /// scheme, so they are the last word: neither <paramref name="configureJwtBearer"/> nor a later
    /// <c>services.Configure&lt;JwtBearerOptions&gt;</c> can weaken them. <c>MapInboundClaims = false</c> (the
    /// provider reads raw claim names); <c>TryAllIssuerSigningKeys = false</c> (preserves <c>kid</c>-pinned
    /// rotation isolation); <c>RequireSignedTokens</c>, <c>ValidateIssuer</c>/<c>ValidateAudience</c>/
    /// <c>ValidateLifetime</c>/<c>RequireExpirationTime</c>/<c>ValidateIssuerSigningKey</c>;
    /// <c>ValidIssuer = issuer</c>, <c>ValidAudience = audience</c> (the plural <c>ValidIssuers</c>/
    /// <c>ValidAudiences</c> are cleared so no extra issuer/audience slips in); <c>ValidAlgorithms = ["RS256"]</c>
    /// (pins the gateway's asymmetric algorithm, rejecting <c>alg:none</c> and HMAC key-confusion). Every
    /// validator delegate that could bypass a forced scalar — issuer/audience/lifetime/algorithm/signing-key
    /// validators, the signature validator, and the token reader — is nulled, <c>RequireAudience</c> is
    /// forced, and scheme forwarding is cleared. <c>ClockSkew</c> defaults to
    /// 30 seconds unless <paramref name="configureJwtBearer"/> set a non-default value. The actor provider's
    /// scheme, <c>ExpectedIssuer</c>, and <c>ExpectedAudience</c> are likewise forced in a
    /// <c>PostConfigure&lt;TrellisInternalJwtActorOptions&gt;</c> after <paramref name="configureActor"/>.
    /// A startup <c>IValidateOptions&lt;JwtBearerOptions&gt;</c> then re-asserts the strict profile and fails
    /// closed (host start throws) if a later <c>PostConfigure</c> or a replaced <c>TokenHandlers</c> list
    /// weakened it.
    /// </para>
    /// <para>
    /// For an asymmetric algorithm other than RS256, or any posture the forced invariants disallow, register
    /// <c>AddJwtBearer</c> yourself and pair it with <see cref="AddTrellisInternalJwtActorProvider"/>.
    /// </para>
    /// <para>
    /// <b>Not trim / AOT compatible.</b> This helper configures JWT bearer authentication, whose token
    /// validation and OIDC metadata retrieval are not trim- or AOT-safe — hence the package keeps it behind
    /// these annotations while staying AOT-compatible for <see cref="AddTrellisInternalJwtActorProvider"/>.
    /// For a trimmed or native-AOT host, register your authentication scheme yourself and pair it with
    /// <see cref="AddTrellisInternalJwtActorProvider"/>.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Configures JwtBearer authentication, whose token validation and OIDC metadata retrieval are not trim-compatible. For a trimmed host, register your authentication scheme yourself and pair it with AddTrellisInternalJwtActorProvider.")]
    [RequiresDynamicCode("Configures JwtBearer authentication, which is not native-AOT compatible. For an AOT host, register your authentication scheme yourself and pair it with AddTrellisInternalJwtActorProvider.")]
    public static IServiceCollection AddTrellisInternalJwtBearer(
        this IServiceCollection services,
        string issuer,
        string audience,
        Action<TrellisInternalJwtActorOptions>? configureActor = null,
        Action<JwtBearerOptions>? configureJwtBearer = null,
        string authenticationScheme = JwtBearerDefaults.AuthenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticationScheme);

        services
            .AddAuthentication(authenticationScheme)
            .AddJwtBearer(authenticationScheme, options =>
            {
                // Overridable, deployment-specific defaults. The consumer hook (and any later
                // services.Configure<JwtBearerOptions>) may change these; the forced invariants run in
                // PostConfigure below and always win.
                options.Authority = issuer;
                options.RequireHttpsMetadata = true;
                options.SaveToken = false;

                configureJwtBearer?.Invoke(options);
            });

        // Pin issuer validation to exactly `issuer`, ignoring any issuer the OIDC discovery metadata
        // advertises — a mis-pointed Authority/MetadataAddress must not silently widen the accepted issuer
        // (IdentityModel's default validator also accepts configuration.Issuer). Shared by reference with the
        // startup validator so a later PostConfigure that replaces it fails closed.
        IssuerValidator pinnedIssuerValidator = (tokenIssuer, _, _) =>
            string.Equals(tokenIssuer, issuer, StringComparison.Ordinal)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException("Issuer does not match the pinned issuer.")
                {
                    InvalidIssuer = tokenIssuer,
                };

        // Pin the token handler to a single JsonWebTokenHandler the helper owns, so a later PostConfigure
        // cannot swap in a permissive handler that ignores TokenValidationParameters and reports every token
        // valid — the one validation path the scalar forcing below cannot reach. MapInboundClaims is forced off
        // to match the actor provider's raw-claim reads (the options.MapInboundClaims setter only propagates to
        // the framework's own default handler instance, not to this replacement).
        var pinnedTokenHandler = new JsonWebTokenHandler { MapInboundClaims = false };

        // The non-negotiable invariants run in PostConfigure so they are the LAST word — they cannot be
        // weakened by the consumer's configureJwtBearer NOR by a later services.Configure<JwtBearerOptions>.
        services.PostConfigure<JwtBearerOptions>(
            authenticationScheme,
            options => ApplyForcedJwtBearerInvariants(options, issuer, audience, pinnedIssuerValidator, pinnedTokenHandler));

        // Belt-and-suspenders: fail closed at startup if the resolved options are not strict — this catches a
        // later consumer PostConfigure or a replaced TokenHandlers list that PostConfigure forcing cannot reach.
        services.AddSingleton<IValidateOptions<JwtBearerOptions>>(
            new TrellisInternalJwtBearerOptionsValidator(authenticationScheme, issuer, audience, pinnedIssuerValidator, pinnedTokenHandler));
        services.AddOptions<JwtBearerOptions>(authenticationScheme).ValidateOnStart();

        services.AddTrellisInternalJwtActorProvider(configureActor);

        // Force the scheme + defense-in-depth issuer/audience AFTER the consumer's configureActor, so the
        // actor provider always authenticates the same scheme the bearer handler validates and the runtime
        // cross-checks cannot be removed.
        services.PostConfigure<TrellisInternalJwtActorOptions>(actorOptions =>
        {
            actorOptions.AuthenticationScheme = authenticationScheme;
            actorOptions.ExpectedIssuer = issuer;
            actorOptions.ExpectedAudience = audience;
        });

        return services;
    }

    // The fail-closed core of AddTrellisInternalJwtBearer: every security-critical setting is asserted, plural
    // accepted-value collections are cleared, and every validator delegate that could bypass a forced scalar
    // check is nulled. A consumer that genuinely needs a custom validator registers AddJwtBearer themselves.
    private static void ApplyForcedJwtBearerInvariants(JwtBearerOptions options, string issuer, string audience, IssuerValidator pinnedIssuerValidator, TokenHandler pinnedTokenHandler)
    {
        options.MapInboundClaims = false;

        // Scheme forwarding would hand authentication to another handler entirely, bypassing the parameters.
        options.ForwardAuthenticate = null;
        options.ForwardDefault = null;
        options.ForwardDefaultSelector = null;
#pragma warning disable CS0618 // legacy validator path is obsolete; we force it off so it cannot be a bypass
        options.UseSecurityTokenValidators = false;   // force the modern TokenHandlers validation path
#pragma warning restore CS0618

        // A custom or additional TokenHandler validates the token itself and could ignore every parameter below;
        // replace the list with the single pinned handler the startup validator reference-checks.
        options.TokenHandlers.Clear();
        options.TokenHandlers.Add(pinnedTokenHandler);

        var v = options.TokenValidationParameters;
        v.ValidateIssuer = true;
        v.ValidIssuer = issuer;
        v.ValidIssuers = null;
        v.ValidateAudience = true;
        v.ValidAudience = audience;
        v.ValidAudiences = null;
        v.RequireAudience = true;        // a token with no aud must be rejected, not skipped
        v.IgnoreTrailingSlashWhenValidatingAudience = false;   // the audience pin must be exact
        v.ValidateLifetime = true;
        v.RequireExpirationTime = true;
        v.ValidateIssuerSigningKey = true;
        v.RequireSignedTokens = true;
        v.TryAllIssuerSigningKeys = false;
        v.ValidAlgorithms = ["RS256"];
        if (v.ClockSkew == TimeSpan.FromMinutes(5))
            v.ClockSkew = TimeSpan.FromSeconds(30);

        // A custom validator/resolver delegate is exactly the loose-profile escape the guardrail must close —
        // each could replace or bypass a forced scalar check above (or supply attacker-controlled keys).
        // The exact-match issuer validator (set below) is the only issuer check; clear the configuration-based
        // and consumer delegates so the discovered metadata issuer cannot widen the pin.
        v.IssuerValidator = pinnedIssuerValidator;
        v.IssuerValidatorUsingConfiguration = null;
        v.AudienceValidator = null;
        v.LifetimeValidator = null;
        v.AlgorithmValidator = null;
        v.IssuerSigningKeyValidator = null;
        v.IssuerSigningKeyValidatorUsingConfiguration = null;
        v.IssuerSigningKeyResolver = null;
        v.IssuerSigningKeyResolverUsingConfiguration = null;
        v.SignatureValidator = null;
        v.SignatureValidatorUsingConfiguration = null;
        v.TokenReader = null;
    }
}
