namespace Trellis.Microservices.AspNetCore.Tests;

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

/// <summary>
/// Tests for <see cref="ServiceCollectionExtensions.AddTrellisInternalJwtBearer"/> — the one-call
/// composition of the strict <c>AddJwtBearer</c> profile and the internal-JWT actor provider.
/// </summary>
public sealed class AddTrellisInternalJwtBearerTests
{
    private const string Issuer = "https://gateway.internal";
    private const string Audience = "incidents-service";

    private static JwtBearerOptions ResolveJwtBearer(IServiceCollection services, string scheme = "Bearer") =>
        services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(scheme);

    private static TrellisInternalJwtActorOptions ResolveActorOptions(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IOptions<TrellisInternalJwtActorOptions>>().Value;

    [Fact]
    public void AddTrellisInternalJwtBearer_RegistersScopedActorProvider()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IActorProvider)
            && d.ImplementationType == typeof(TrellisInternalJwtActorProvider)
            && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_RegistersJwtBearerHandlerOnScheme_WithAuthorityDefault()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);

        var jwt = ResolveJwtBearer(services);
        jwt.Authority.Should().Be(Issuer);
        jwt.RequireHttpsMetadata.Should().BeTrue();
        jwt.SaveToken.Should().BeFalse();
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_NoConfigure_ForcesStrictInvariants()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);

        var jwt = ResolveJwtBearer(services);
        jwt.MapInboundClaims.Should().BeFalse();
        var v = jwt.TokenValidationParameters;
        v.ValidateIssuer.Should().BeTrue();
        v.ValidIssuer.Should().Be(Issuer);
        v.ValidateAudience.Should().BeTrue();
        v.ValidAudience.Should().Be(Audience);
        v.RequireAudience.Should().BeTrue();
        v.IgnoreTrailingSlashWhenValidatingAudience.Should().BeFalse();
        v.ValidateLifetime.Should().BeTrue();
        v.RequireExpirationTime.Should().BeTrue();
        v.ValidateIssuerSigningKey.Should().BeTrue();
        v.RequireSignedTokens.Should().BeTrue();
        v.TryAllIssuerSigningKeys.Should().BeFalse();
        v.ValidAlgorithms.Should().Equal("RS256");
        v.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_ConfigureJwtBearer_CannotWeakenForcedInvariants()
    {
        var services = new ServiceCollection();

        // A consumer tries to re-open every footgun the helper closes.
        services.AddTrellisInternalJwtBearer(Issuer, Audience, configureJwtBearer: o =>
        {
            o.MapInboundClaims = true;
            o.TokenValidationParameters.TryAllIssuerSigningKeys = true;
            o.TokenValidationParameters.RequireSignedTokens = false;
            o.TokenValidationParameters.ValidateIssuer = false;
            o.TokenValidationParameters.ValidateAudience = false;
            o.TokenValidationParameters.ValidateLifetime = false;
            o.TokenValidationParameters.ValidAlgorithms = ["HS256"];
        });

        var jwt = ResolveJwtBearer(services);
        jwt.MapInboundClaims.Should().BeFalse("the helper re-applies the invariants after configureJwtBearer");
        var v = jwt.TokenValidationParameters;
        v.TryAllIssuerSigningKeys.Should().BeFalse();
        v.RequireSignedTokens.Should().BeTrue();
        v.ValidateIssuer.Should().BeTrue();
        v.ValidateAudience.Should().BeTrue();
        v.ValidateLifetime.Should().BeTrue();
        v.ValidAlgorithms.Should().Equal("RS256");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_ConfigureJwtBearer_CanSetNonCriticalBits()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience, configureJwtBearer: o =>
        {
            o.RequireHttpsMetadata = false;
            o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(5);
        });

        var jwt = ResolveJwtBearer(services);
        jwt.RequireHttpsMetadata.Should().BeFalse("a plaintext in-cluster gateway is a legitimate override");
        jwt.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.FromSeconds(5), "a non-default ClockSkew is respected");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_FlowsIssuerAudienceAndSchemeToActorOptions()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);

        var actor = ResolveActorOptions(services);
        actor.ExpectedIssuer.Should().Be(Issuer);
        actor.ExpectedAudience.Should().Be(Audience);
        actor.AuthenticationScheme.Should().Be("Bearer");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_ConfigureActor_AppliesRequiredAttributes()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience, configureActor: o =>
        {
            o.AttributeClaimMap["tenant_id"] = "tid";
            o.RequiredAttributes = ["tenant_id"];
        });

        var actor = ResolveActorOptions(services);
        actor.RequiredAttributes.Should().Equal("tenant_id");
        actor.AttributeClaimMap.Should().Contain(new KeyValuePair<string, string>("tenant_id", "tid"));
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_CustomScheme_BindsBothSides()
    {
        var services = new ServiceCollection();
        const string scheme = "Internal";

        services.AddTrellisInternalJwtBearer(Issuer, Audience, authenticationScheme: scheme);

        ResolveJwtBearer(services, scheme).TokenValidationParameters.ValidIssuer.Should().Be(Issuer);
        ResolveActorOptions(services).AuthenticationScheme.Should().Be(scheme);
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_ConfigureJwtBearer_ClearsBypassValidatorsAndPluralCollections()
    {
        var services = new ServiceCollection();

        // A consumer tries to bypass the forced scalar checks with plural collections and validator delegates.
        services.AddTrellisInternalJwtBearer(Issuer, Audience, configureJwtBearer: o =>
        {
            o.ForwardAuthenticate = "SomeOtherScheme";                        // forward auth to a weaker handler
            var p = o.TokenValidationParameters;
            p.ValidIssuers = ["https://evil.example"];
            p.ValidAudiences = ["evil-audience"];
            p.IssuerValidator = (issuer, token, parameters) => issuer;        // accept any issuer
            p.AudienceValidator = (audiences, token, parameters) => true;     // accept any audience
            p.LifetimeValidator = (nb, exp, token, parameters) => true;       // never expire
            p.SignatureValidator = (token, parameters) => null!;              // bypass signature validation
            p.IssuerSigningKeyValidator = (key, token, parameters) => true;   // accept any signing key
            p.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) => [];  // attacker-supplied keys
        });

        var jwt = ResolveJwtBearer(services);
        var v = jwt.TokenValidationParameters;
        jwt.ForwardAuthenticate.Should().BeNull("forwarding would bypass the bearer handler entirely");
        v.ValidIssuers.Should().BeNull("no extra accepted issuers may slip past the forced ValidIssuer");
        v.ValidAudiences.Should().BeNull("no extra accepted audiences may slip past the forced ValidAudience");
        v.IssuerValidator.Should().NotBeNull("the issuer is pinned to an exact-match validator, replacing any consumer-supplied one");
        v.AudienceValidator.Should().BeNull();
        v.LifetimeValidator.Should().BeNull();
        v.SignatureValidator.Should().BeNull();
        v.IssuerSigningKeyValidator.Should().BeNull();
        v.IssuerSigningKeyResolver.Should().BeNull();
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_LaterPostConfigure_FailsClosedAtOptionsResolution()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);
        // A later PostConfigure runs AFTER the helper's forcing; the startup validator must reject it.
        services.PostConfigure<JwtBearerOptions>("Bearer", o => o.MapInboundClaims = true);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        act.Should().Throw<OptionsValidationException>().WithMessage("*MapInboundClaims*");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_LaterPostConfigure_DisablingExpiration_FailsClosed()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);
        services.PostConfigure<JwtBearerOptions>("Bearer", o => o.TokenValidationParameters.RequireExpirationTime = false);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        act.Should().Throw<OptionsValidationException>().WithMessage("*RequireExpirationTime*");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_LaterPostConfigure_LegacyValidatorPath_FailsClosed()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);
#pragma warning disable CS0618 // intentionally exercising the obsolete legacy validator path
        services.PostConfigure<JwtBearerOptions>("Bearer", o => o.UseSecurityTokenValidators = true);
#pragma warning restore CS0618

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        act.Should().Throw<OptionsValidationException>().WithMessage("*UseSecurityTokenValidators*");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_LaterPostConfigure_TrailingSlashAudience_FailsClosed()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);
        services.PostConfigure<JwtBearerOptions>("Bearer", o => o.TokenValidationParameters.IgnoreTrailingSlashWhenValidatingAudience = true);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        act.Should().Throw<OptionsValidationException>().WithMessage("*IgnoreTrailingSlash*");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_LaterPostConfigure_ReplacedIssuerValidator_FailsClosed()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);
        // Replacing the pinned issuer validator with a permissive one would let the discovered metadata issuer
        // (or any issuer) through — the startup validator must reject it.
        services.PostConfigure<JwtBearerOptions>("Bearer", o => o.TokenValidationParameters.IssuerValidator = (i, t, p) => i);

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        act.Should().Throw<OptionsValidationException>().WithMessage("*pinned exact-match*");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_PinsSingleJsonWebTokenHandlerWithMapInboundClaimsOff()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);

        var jwt = ResolveJwtBearer(services);
        jwt.TokenHandlers.Should().ContainSingle("a single owned handler is the only validation path")
            .Which.Should().BeOfType<JsonWebTokenHandler>()
            .Which.MapInboundClaims.Should().BeFalse("the actor provider reads raw JWT claim names");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_LaterPostConfigure_ReplacedTokenHandler_FailsClosed()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);
        // A swapped-in handler validates the token itself and could report every token valid, ignoring all of
        // the forced TokenValidationParameters — the startup validator must reject anything but the pinned one.
        services.PostConfigure<JwtBearerOptions>("Bearer", o =>
        {
            o.TokenHandlers.Clear();
            o.TokenHandlers.Add(new JsonWebTokenHandler());
        });

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");

        act.Should().Throw<OptionsValidationException>().WithMessage("*pinned handler*");
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_LaterConfigure_CannotWeakenForcedInvariants()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience);
        // A separate, later registration tries to loosen the profile — PostConfigure must still win.
        services.Configure<JwtBearerOptions>("Bearer", o =>
        {
            o.MapInboundClaims = true;
            o.TokenValidationParameters.TryAllIssuerSigningKeys = true;
            o.TokenValidationParameters.RequireSignedTokens = false;
        });

        var jwt = ResolveJwtBearer(services);
        jwt.MapInboundClaims.Should().BeFalse("the invariants run in PostConfigure, after every Configure");
        jwt.TokenValidationParameters.TryAllIssuerSigningKeys.Should().BeFalse();
        jwt.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
    }

    [Fact]
    public void AddTrellisInternalJwtBearer_ConfigureActor_CannotOverrideSchemeIssuerOrAudience()
    {
        var services = new ServiceCollection();

        services.AddTrellisInternalJwtBearer(Issuer, Audience, configureActor: o =>
        {
            o.AuthenticationScheme = "WrongScheme";
            o.ExpectedIssuer = "https://evil.example";
            o.ExpectedAudience = "evil-audience";
        });

        var actor = ResolveActorOptions(services);
        actor.AuthenticationScheme.Should().Be("Bearer", "forced after configureActor so it matches the bearer handler");
        actor.ExpectedIssuer.Should().Be(Issuer);
        actor.ExpectedAudience.Should().Be(Audience);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTrellisInternalJwtBearer_BlankIssuer_Throws(string? issuer)
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisInternalJwtBearer(issuer!, Audience);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTrellisInternalJwtBearer_BlankAudience_Throws(string? audience)
    {
        var services = new ServiceCollection();

        var act = () => services.AddTrellisInternalJwtBearer(Issuer, audience!);

        act.Should().Throw<ArgumentException>();
    }
}
