namespace E2EHarness;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

/// <summary>
/// Builds an in-process YARP gateway TestServer wired to a custom forwarder client
/// factory so that outbound requests are routed through a destination TestServer's
/// handler instead of the real network.
/// </summary>
internal static class GatewayHarness
{
    /// <summary>
    /// Stands up a gateway TestServer for the given destination. Returns the gateway
    /// host. The caller passes in a pre-built signing credential so the destination's
    /// JwtBearer can be configured with the matching key BEFORE the gateway starts.
    /// </summary>
    public static async Task<IHost> StartAsync(
        TestServer destination,
        Actor? actor,
        SigningCredentials credentials,
        string audience = HarnessFixtures.DefaultAudience,
        string clusterId = HarnessFixtures.DefaultClusterId)
    {
        var route = new RouteConfig
        {
            RouteId = "test-route",
            ClusterId = clusterId,
            Match = new RouteMatch { Path = "/{**catch-all}" },
        };
        var cluster = new ClusterConfig
        {
            ClusterId = clusterId,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
            {
                ["d1"] = new DestinationConfig { Address = destination.BaseAddress.ToString() },
            },
        };

        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddSingleton<IActorProvider>(new StubActorProvider(actor));

                    s.AddReverseProxy()
                        .LoadFromMemory([route], [cluster])
                        .AddTrellisActorForwarding(o =>
                        {
                            o.Issuer = HarnessFixtures.GatewayIssuer;
                            o.SigningCredentials = credentials;
                            o.PublicBaseUrl = new Uri(HarnessFixtures.GatewayIssuer, UriKind.Absolute);
                            o.AudiencePerCluster = _ => audience;
                        });

                    s.AddSingleton<IForwarderHttpClientFactory>(
                        new TestServerForwarderHttpClientFactory(destination.CreateHandler()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapReverseProxy());
                });
            });
        return await builder.StartAsync();
    }

    private sealed class StubActorProvider(Actor? actor) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(actor is null ? Maybe<Actor>.None : Maybe<Actor>.From(actor));
    }

    private sealed class TestServerForwarderHttpClientFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory, IDisposable
    {
        // disposeHandler:false on the invoker because the singleton owns the handler
        // for the lifetime of the gateway host; dispose it once on host teardown.
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
            => new(handler, disposeHandler: false);

        public void Dispose() => handler.Dispose();
    }
}
