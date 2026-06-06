namespace E2EHarness;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Shared harness fixtures used by every scenario in this project.
///
/// <para>The harness deliberately uses in-process TestServers wired through a custom
/// <see cref="Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory"/> rather than
/// docker-compose: it runs in &lt; 1 second in CI, is deterministic across machines,
/// and exercises the actual YARP transform pipeline at runtime (not a mock).</para>
///
/// <para>Each scenario wires:
///   gateway (TestServer with AddTrellisActorForwarding) → destination (TestServer
///   with AddJwtBearer + AddTrellisInternalJwtActorProvider). For "attack" scenarios
///   that need a hand-crafted JWT (e.g. sentinel-stripped, count-mismatch), the test
///   skips the gateway, mints its own JWT signed with the harness's RSA key, and
///   issues a GET request to the destination's <c>/probe</c> endpoint with
///   <c>Authorization: Bearer &lt;token&gt;</c>.</para>
/// </summary>
internal static class HarnessFixtures
{
    public const string GatewayIssuer = "https://gateway.internal";
    public const string DefaultAudience = "incidents-service";
    public const string DefaultClusterId = "incidents";
    public const string DefaultKeyId = "active-1";

    /// <summary>
    /// Builds an RSA signing key with a stable <c>kid</c> the harness can re-use across
    /// scenarios that need to hand-craft an attack JWT signed by the "gateway" key.
    /// </summary>
    public static (RsaSecurityKey Key, SigningCredentials Credentials) CreateSigningKey(string kid = DefaultKeyId)
    {
        var rsaKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid };
        var credentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);
        return (rsaKey, credentials);
    }

    /// <summary>
    /// Stands up the destination TestServer with the strict cookbook Recipe 1 profile:
    /// MapInboundClaims=false, TryAllIssuerSigningKeys=false, ClockSkew=30s, pinned
    /// asymmetric algorithms, ValidIssuer/ValidAudience required, RequireExpirationTime
    /// + RequireSignedTokens + ValidateIssuerSigningKey enforced.
    /// </summary>
    /// <remarks>
    /// The destination's <c>/probe</c> endpoint requires authentication and dumps the
    /// hydrated <see cref="Actor"/> in the response body as JSON. Scenarios assert
    /// either:
    /// <list type="bullet">
    /// <item><description>HTTP 200 + expected actor shape (happy path / contract-conformant token),</description></item>
    /// <item><description>HTTP 401 (downstream fail-closed posture — actor provider returned <c>Maybe.None</c>; the <c>/probe</c> endpoint handler directly returns 401 on <c>Maybe.None</c>), or</description></item>
    /// <item><description>HTTP 401 from <c>AddJwtBearer</c> itself (signature / aud / iss / exp failure before the actor provider runs).</description></item>
    /// </list>
    /// </remarks>
    public static async Task<IHost> StartDestinationAsync(
        RsaSecurityKey trustedSigningKey,
        string expectedAudience = DefaultAudience,
        string expectedIssuer = GatewayIssuer,
        Action<TrellisInternalJwtActorOptions>? configureActor = null)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddAuthorization();
                    s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(o =>
                        {
                            o.MapInboundClaims = false;
                            o.SaveToken = false;
                            o.RequireHttpsMetadata = false; // TestServer has no HTTPS
                            o.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidateIssuer = true,
                                ValidIssuer = expectedIssuer,
                                ValidateAudience = true,
                                ValidAudience = expectedAudience,
                                ValidateLifetime = true,
                                RequireExpirationTime = true,
                                RequireSignedTokens = true,
                                ValidateIssuerSigningKey = true,
                                IssuerSigningKey = trustedSigningKey,
                                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                                ClockSkew = TimeSpan.FromSeconds(30),
                                TryAllIssuerSigningKeys = false,
                            };
                        });
                    s.AddTrellisInternalJwtActorProvider(o =>
                    {
                        o.ExpectedIssuer = expectedIssuer;
                        o.ExpectedAudience = expectedAudience;
                        configureActor?.Invoke(o);
                    });
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapGet("/probe", async context =>
                    {
                        var actorProvider = context.RequestServices.GetRequiredService<IActorProvider>();
                        var actor = await actorProvider.GetCurrentActorAsync(context.RequestAborted);
                        if (!actor.HasValue)
                        {
                            context.Response.StatusCode = 401;
                            return;
                        }

                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            id = actor.Value.Id.Value,
                            permissions = actor.Value.Permissions.OrderBy(x => x).ToArray(),
                            forbiddenPermissions = actor.Value.ForbiddenPermissions.OrderBy(x => x).ToArray(),
                            attributes = actor.Value.Attributes.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value),
                        });
                    }).RequireAuthorization());
                });
            });
        return await builder.StartAsync();
    }

    /// <summary>
    /// Mints a contract-conformant JWT directly, bypassing the YARP gateway. Used by
    /// scenarios that need to send a specific shape of attack token to the downstream.
    /// </summary>
    public static string MintToken(
        SigningCredentials credentials,
        string subject = "user-42",
        string issuer = GatewayIssuer,
        string audience = DefaultAudience,
        TimeSpan? lifetime = null,
        IEnumerable<string>? permissions = null,
        IEnumerable<string>? forbiddenPermissions = null,
        IDictionary<string, string>? attributes = null,
        // Sentinel/count overrides for negative-path scenarios:
        bool emitContractVersion = true,
        string? contractVersionOverride = null,
        bool emitPermissionsCount = true,
        string? permissionsCountOverride = null,
        bool emitForbiddenPermissionsCount = true,
        string? forbiddenPermissionsCountOverride = null,
        bool emitJti = true)
    {
        var perms = (permissions ?? []).ToArray();
        var forbidden = (forbiddenPermissions ?? []).ToArray();
        var lifeSpan = lifetime ?? TimeSpan.FromMinutes(5);
        var now = DateTime.UtcNow;

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.Subject, subject));
        if (emitJti) identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.JwtId, Guid.NewGuid().ToString("N")));
        foreach (var p in perms) identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.Permissions, p));
        foreach (var f in forbidden) identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ForbiddenPermissions, f));
        if (emitContractVersion)
            identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ContractVersion, contractVersionOverride ?? TrellisInternalJwtClaimNames.CurrentContractVersion));
        if (emitPermissionsCount)
            identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.PermissionsCount, permissionsCountOverride ?? perms.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (emitForbiddenPermissionsCount)
            identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ForbiddenPermissionsCount, forbiddenPermissionsCountOverride ?? forbidden.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (attributes is not null)
            foreach (var kv in attributes) identity.AddClaim(new Claim(kv.Key, kv.Value));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = identity,
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + lifeSpan,
            SigningCredentials = credentials,
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Helper for the "attack" scenarios: send a hand-crafted token to /probe.</summary>
    public static async Task<HttpResponseMessage> ProbeAsync(IHost destination, string compactJws, CancellationToken ct)
    {
        using var client = destination.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {compactJws}");
        return await client.GetAsync("/probe", ct);
    }
}
