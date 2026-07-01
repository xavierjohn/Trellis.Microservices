namespace Trellis.Yarp.Tests;

using System;
using System.Security.Cryptography;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy.Transforms.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Tests for <see cref="TrellisActorForwardingServiceCollectionExtensions.AddTrellisActorForwarding(Microsoft.Extensions.DependencyInjection.IReverseProxyBuilder, System.Action{TrellisActorForwardingOptions})"/>.
/// Asserts the service-collection wiring (options binding, validator registration,
/// minter singleton, transform provider) and the ValidateOnStart contract.
/// </summary>
public sealed class AddTrellisActorForwardingTests
{
    [Fact]
    public void AddTrellisActorForwarding_RegistersOptionsValidatorAndMinter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var yarpBuilder = services.AddReverseProxy();

        yarpBuilder.AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<TrellisActorForwardingOptions>>().Should().NotBeNull();
        sp.GetServices<IValidateOptions<TrellisActorForwardingOptions>>()
            .Should().ContainSingle(v => v is TrellisActorForwardingOptionsValidator);
        sp.GetRequiredService<TrellisActorJwtMinter>().Should().NotBeNull();
    }

    [Fact]
    public void AddTrellisActorForwarding_RegistersTimeProviderWhenAbsent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddTrellisActorForwarding_DoesNotReplaceUserRegisteredTimeProvider()
    {
        var custom = new FakeFixedTimeProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(custom);
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<TimeProvider>().Should().BeSameAs(custom,
            "AddTrellisActorForwarding must use TryAddSingleton so consumers can pre-register a test TimeProvider (typical pattern in xUnit integration tests)");
    }

    [Fact]
    public void AddTrellisActorForwarding_RegistersTransformProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        sp.GetServices<ITransformProvider>().Should().ContainSingle(p => p is TrellisActorForwardingTransformProvider);
    }

    [Fact]
    public void AddTrellisActorForwarding_ValidateOnStart_FailsOnInvalidOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(o =>
        {
            o.Issuer = ""; // invalid
            o.SigningCredentials = NewRsaSigningCredentials("active-1");
            o.PublicBaseUrl = new Uri("https://gateway.internal");
        });

        var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptions<TrellisActorForwardingOptions>>();

        // Touching .Value forces validation; the IValidateOptions<> instance runs and throws.
        var act = () => monitor.Value;
        act.Should().Throw<OptionsValidationException>()
           .WithMessage("*Issuer*");
    }

    [Fact]
    public void AddTrellisActorForwarding_NullBuilder_Throws()
    {
        IReverseProxyBuilder? builder = null;
        var act = () => builder!.AddTrellisActorForwarding(ConfigureValid);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrellisActorForwarding_NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddReverseProxy();
        var act = () => builder.AddTrellisActorForwarding(configure: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrellisActorForwarding_MinterIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        var minter1 = sp.GetRequiredService<TrellisActorJwtMinter>();
        var minter2 = sp.GetRequiredService<TrellisActorJwtMinter>();
        minter1.Should().BeSameAs(minter2,
            "the minter holds the singleton JsonWebTokenHandler and options instance — no per-request allocation");
    }

    [Fact]
    public void AddTrellisActorForwarding_RegistersValidatingSigningKeyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<ITrellisSigningKeyProvider>();

        provider.Should().BeOfType<ValidatingTrellisSigningKeyProvider>(
            "the consumer-facing provider must always be the fail-closed validating decorator");
        provider.GetCurrentRing().Current.Key.KeyId.Should().Be("active-1",
            "the default provider projects the static SigningCredentials into the ring");
    }

    [Fact]
    public void AddTrellisActorForwarding_CustomProviderOverload_WrapsAndUsesTheProvider()
    {
        const string customKid = "vault-key-7";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(
            ConfigureValid,
            _ => new StubSigningKeyProvider(NewRing(customKid)));

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<ITrellisSigningKeyProvider>();

        provider.Should().BeOfType<ValidatingTrellisSigningKeyProvider>(
            "custom providers must be wrapped by the validating decorator, never resolved raw");
        provider.GetCurrentRing().Current.Key.KeyId.Should().Be(customKid);
    }

    [Fact]
    public void AddTrellisActorForwarding_NullSigningKeyProviderFactory_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddReverseProxy();
        var act = () => builder.AddTrellisActorForwarding(ConfigureValid, signingKeyProviderFactory: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrellisActorForwarding_PreRegisteredRawProvider_IsOverriddenByValidatingDecorator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // A consumer wrongly registers a raw provider directly instead of using the overload.
        services.AddSingleton<ITrellisSigningKeyProvider>(new StubSigningKeyProvider(NewRing("raw-bypass")));
        services.AddReverseProxy().AddTrellisActorForwarding(ConfigureValid);

        var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<ITrellisSigningKeyProvider>();

        provider.Should().BeOfType<ValidatingTrellisSigningKeyProvider>(
            "a pre-registered raw provider must NOT bypass the fail-closed validating decorator");
        provider.GetCurrentRing().Current.Key.KeyId.Should().Be("active-1",
            "the decorator wraps the validated static default, not the consumer's unvalidated raw registration");
    }

    [Fact]
    public void AddTrellisActorForwarding_CustomProvider_StartsWithoutStaticSigningCredentials()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy().AddTrellisActorForwarding(
            configure: o =>
            {
                o.Issuer = "https://gateway.internal";
                o.PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute);
                // Intentionally no SigningCredentials — the custom provider owns the ring.
            },
            signingKeyProviderFactory: _ => new StubSigningKeyProvider(NewRing("vault-1")));

        var sp = services.BuildServiceProvider();

        // ValidateOnStart must NOT require the static SigningCredentials on the custom-provider path.
        var act = () => sp.GetRequiredService<IOptions<TrellisActorForwardingOptions>>().Value;
        act.Should().NotThrow();
        sp.GetRequiredService<ITrellisSigningKeyProvider>().GetCurrentRing().Current.Key.KeyId.Should().Be("vault-1");
    }

    [Fact]
    public void AddTrellisActorForwarding_CustomThenStaticOverload_ReEnablesStaticKeyValidationFailClosed()
    {
        // Calling the custom overload then the static overload: the resolved provider becomes the
        // static decorator (last-wins), so the UsesCustomSigningKeyProvider flag MUST reset to false
        // and startup validation must again require the static SigningCredentials — fail-closed.
        var services = new ServiceCollection();
        services.AddLogging();
        var yarp = services.AddReverseProxy();
        yarp.AddTrellisActorForwarding(
            o => { o.Issuer = "https://gateway.internal"; o.PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute); },
            _ => new StubSigningKeyProvider(NewRing("vault-1")));
        yarp.AddTrellisActorForwarding(
            o => { o.Issuer = "https://gateway.internal"; o.PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute); });

        var sp = services.BuildServiceProvider();
        var act = () => sp.GetRequiredService<IOptions<TrellisActorForwardingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
           .WithMessage("*SigningCredentials*",
               "the static overload must reset the custom-provider flag so missing static credentials fail at startup");
    }

    // === Fixtures ===

    private static void ConfigureValid(TrellisActorForwardingOptions o)
    {
        o.Issuer = "https://gateway.internal";
        o.SigningCredentials = NewRsaSigningCredentials("active-1");
        o.PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute);
    }

    private static SigningCredentials NewRsaSigningCredentials(string kid) =>
        new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid }, SecurityAlgorithms.RsaSha256);

    private static TrellisSigningKeyRing NewRing(string kid) =>
        TrellisSigningKeyRing.FromActiveAndPrevious(NewRsaSigningCredentials(kid), []);

    private sealed class StubSigningKeyProvider(TrellisSigningKeyRing ring) : ITrellisSigningKeyProvider
    {
        public TrellisSigningKeyRing GetCurrentRing() => ring;
    }

    private sealed class FakeFixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    }
}
