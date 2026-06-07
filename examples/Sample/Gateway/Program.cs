using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Trellis.Asp.Authorization;
using Trellis.Yarp;

// Sample gateway: terminates the inbound user request via DevelopmentActorProvider
// (X-Test-Actor header), then re-mints a fresh per-cluster internal JWT carrying the
// full Actor surface and forwards downstream via YARP. The downstream Orders/Billing
// services fetch our JWKS at /.well-known/jwks.json to validate the JWT.
//
// Routes (see appsettings.json):
//   /api/orders/{**catch-all}  -> cluster "orders"  -> Orders service  (audience="orders")
//   /api/billing/{**catch-all} -> cluster "billing" -> Billing service (audience="billing")
//
// AudiencePerCluster = cluster => cluster.ClusterId pins each downstream's audience
// so a token minted for /api/orders/* fails closed at /api/billing/* (and vice versa).
// That cross-audience reject is one of the framework's invariants on display.
//
// Destination URLs in appsettings.json use https+http://orders / https+http://billing,
// which Microsoft.Extensions.ServiceDiscovery.Yarp resolves at request time via the
// env vars Aspire AppHost injects through WithReference(orders).WithReference(billing).
//
// In a real deployment you would swap AddDevelopmentActorProvider for one of the
// production actor providers in upstream Trellis.Asp.Authorization (ClaimsActorProvider,
// EntraActorProvider, NestedJsonPathClaimsActorProvider) wired to your real IdP.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Dev-mode actor provider: reads the X-Test-Actor header for easy curl-based testing.
// Throws if ASPNETCORE_ENVIRONMENT is not Development. See README walkthrough.
builder.Services.AddDevelopmentActorProvider(o =>
{
    o.DefaultActorId = "guest";
    o.DefaultPermissions = new HashSet<string>(StringComparer.Ordinal);
});

// Fresh per-startup RSA signing key. Production would persist the key + rotate via
// PreviousSigningKeys; this is a sample, so a process-lifetime ephemeral key keeps
// the demo zero-config. The JWKS endpoint publishes the public component.
//
// kid is derived from a hash of the public key bytes so a gateway restart that
// regenerates the key material also gets a fresh kid. Downstream services that
// have a cached JWKS with the old (kid, public-key) pair will refresh on the
// next request whose JWT carries an unknown kid — they won't fail validation
// against stale key material. (A static kid like "sample-key-1" would let the
// cached old public key match the new JWT's kid header but fail signature
// verification until the JWKS cache TTL expires.)
var rsa = RSA.Create(2048);
var publicKeyHash = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
var kid = Convert.ToHexString(publicKeyHash, 0, 8); // first 16 hex chars = 64 bits of pubkey hash
var signingKey = new RsaSecurityKey(rsa) { KeyId = $"sample-key-{kid}" };

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    .AddTrellisActorForwarding(o =>
    {
        o.Issuer = "http://localhost:5001";
        o.PublicBaseUrl = new Uri("http://localhost:5001");
        o.SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        // Per-cluster audience pinning: each downstream pins its own ValidAudience.
        // Cross-audience confusion (token minted for cluster A used at cluster B) is
        // rejected by the downstream's JwtBearer ValidAudience check.
        o.AudiencePerCluster = cluster => cluster.ClusterId;
        o.Lifetime = TimeSpan.FromMinutes(5);
    });

var app = builder.Build();
app.MapDefaultEndpoints();

// Publish the OIDC discovery + JWKS endpoints so downstream services can use
// AddJwtBearer(o.Authority = "http://localhost:5001") to auto-discover the signing key.
app.MapTrellisDiscoveryEndpoint();
app.MapReverseProxy();

app.Run();
