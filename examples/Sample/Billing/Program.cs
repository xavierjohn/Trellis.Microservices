using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Trellis.Authorization;
using Trellis.Microservices.AspNetCore;

// Billing microservice — second of the two downstream services behind the YARP gateway.
//
// Audience: "billing" (matches the YARP cluster name → AudiencePerCluster).
// Path:     /api/billing
//
// Identical structure to Orders/Program.cs aside from the audience pin + endpoint
// route. A token minted for /api/orders (audience="orders") MUST NOT validate here —
// the cross-audience reject is one of the framework's invariants on display.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Gate dev-only options on IsDevelopment so a copy/paste into a
        // production composition root keeps RequireHttpsMetadata=true (the
        // ASP.NET Core default) and IncludeErrorDetails=false (don't leak
        // JWT validation failure reasons to the wire).
        var isDev = builder.Environment.IsDevelopment();

        o.Authority = "http://localhost:5001";
        o.Audience = "billing";
        o.RequireHttpsMetadata = !isDev;         // dev only: allow http JWKS discovery
        o.IncludeErrorDetails = isDev;           // dev only: surface real failure reason in WWW-Authenticate
        o.MapInboundClaims = false;
        o.SaveToken = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "http://localhost:5001",
            ValidateAudience = true,
            ValidAudience = "billing",
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            TryAllIssuerSigningKeys = false,
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddTrellisInternalJwtActorProvider(o =>
{
    o.ExpectedIssuer = "http://localhost:5001";
    o.ExpectedAudience = "billing";

    o.AttributeClaimMap["tenant_id"] = "tenant_id";
    o.RequiredAttributes = ["tenant_id"];
});

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/billing", async (IActorProvider actorProvider, CancellationToken ct) =>
{
    var actor = await actorProvider.GetCurrentActorAsync(ct);
    if (!actor.HasValue)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        service = "billing",
        message = "hello from the billing service",
        actor = new
        {
            id = actor.Value.Id.Value,
            permissions = actor.Value.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            forbiddenPermissions = actor.Value.ForbiddenPermissions.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            attributes = actor.Value.Attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
        },
    });
}).RequireAuthorization();

app.Run();
