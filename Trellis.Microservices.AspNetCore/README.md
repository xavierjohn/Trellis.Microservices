# Trellis.Microservices.AspNetCore

Consumer-side counterpart to [`Trellis.Yarp`](../Trellis.Yarp/). Hydrates the full Trellis `Actor` (id + permissions + forbidden permissions + ABAC attributes) from a verified gateway-minted internal JWT, enforcing the strict sentinel + count claim contract that defends the deny-overrides-allow invariant against a proxy stripping the deny set.

## Usage

One call wires the strict `AddJwtBearer` profile and the actor provider together so they cannot drift:

```csharp
builder.Services.AddTrellisInternalJwtBearer(
    issuer: "https://gateway.internal",
    audience: "incidents-service",
    configureActor: o =>
    {
        o.RequiredAttributes = ["tenant_id"];        // fail closed on a missing tenant claim
        o.AttributeClaimMap["tenant_id"] = "tid";
    });
```

It re-applies the security-critical invariants (`MapInboundClaims = false`, `TryAllIssuerSigningKeys = false`, `RequireSignedTokens`, validate `iss`/`aud`/`lifetime`, `ValidAlgorithms = ["RS256"]`) **after** any `configureJwtBearer`, so the loose-profile footgun cannot be reintroduced. It pulls in `Microsoft.AspNetCore.Authentication.JwtBearer`, so it is **not** trim/AOT-safe — for an AOT host (or an algorithm the helper does not pin), register your scheme yourself and pair it with `AddTrellisInternalJwtActorProvider`:

```csharp
builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.Authority = "https://gateway.internal";
    o.Audience = "incidents-service";
    o.MapInboundClaims = false;     // keep raw JWT claim names (e.g. "sub", "tenant_id"), not the Microsoft long-URI forms
    o.SaveToken = false;            // do not retain the raw JWT in AuthenticationProperties
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "https://gateway.internal",
        ValidateAudience = true, ValidAudience = "incidents-service",
        ValidateLifetime = true, RequireSignedTokens = true,
        ValidAlgorithms = ["RS256"],
        ClockSkew = TimeSpan.FromSeconds(30),
        TryAllIssuerSigningKeys = false,  // honor kid-pinned key resolution; see cookbook Recipe 1
    };
});
builder.Services.AddTrellisInternalJwtActorProvider(o =>
{
    o.RequiredAttributes = ["tenant_id"];
    o.AttributeClaimMap["tenant_id"] = "tid";
    o.ExpectedIssuer = "https://gateway.internal";   // defense-in-depth complement to ValidIssuer
    o.ExpectedAudience = "incidents-service";        // defense-in-depth complement to ValidAudience
});
```

> **NOTE on composition root.** This package's `TrellisInternalJwtActorProvider` is registered via the direct `services.AddTrellisInternalJwtActorProvider(...)` extension shown above. The previous `TrellisServiceBuilder.UseTrellisInternalJwtActor` slot in upstream `Trellis.ServiceDefaults` was removed in the v3 cleanup that coincided with this provider moving to this package — both the slot and the upstream `Trellis.Asp.Authorization.TrellisInternalJwt*` implementation it bound to are gone in current upstream releases.

## Documentation

Full reference: [`trellis-api-internal-jwt.md`](../docs/docfx_project/api_reference/trellis-api-internal-jwt.md).

End-to-end recipe: [`trellis-api-microservices-cookbook.md`](../docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md).

## Optional AgentDocs setup

The NuGet package includes a versioned API reference. Restoring the package does
not activate it. Consumers can install the local tool, initialize their restored
graph, approve `Trellis.Microservices.AspNetCore` in `.agentdocs/policy.json`,
and synchronize:

```powershell
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.20 --tool-manifest .config/dotnet-tools.json
dotnet tool run agentdocs init <solution-or-project>
dotnet tool run agentdocs sync
```

Companion links use declared cross-package references, not copied guides.
Approve the other trusted, restored guidance publishers as needed; `Trellis.Core`
owns the upstream guides. References never add dependencies or grant approval.
Unavailable or unapproved targets become notices; use
`dotnet tool run agentdocs sync --strict-references` to reject them instead.

## Dependencies

- [`Trellis.Microservices.Abstractions`](../docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md#use-this-file-when) — shared `TrellisInternalJwtClaimNames` contract literals (transitive).
- Upstream [`Trellis.Authorization`](https://github.com/xavierjohn/Trellis) — `Actor`, `IActorProvider`.
- Upstream [`Trellis.Asp`](https://github.com/xavierjohn/Trellis) — `IProvideActorVaryHeaders` (cache-key partitioning by actor).

## License

[MIT](../LICENSE).
