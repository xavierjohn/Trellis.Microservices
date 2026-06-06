# Trellis.Microservices.AspNetCore

Consumer-side counterpart to `Trellis.Yarp`. Hydrates the full Trellis `Actor` (id + permissions + forbidden permissions + ABAC attributes) from a verified gateway-minted internal JWT, enforcing the strict sentinel + count claim contract that defends the deny-overrides-allow invariant against a proxy stripping the deny set.

## Usage

```csharp
builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.Authority = "https://gateway.internal";
    o.Audience = "incidents-service";
    o.MapInboundClaims = false;     // keep raw JWT claim names
    o.SaveToken = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "https://gateway.internal",
        ValidateAudience = true, ValidAudience = "incidents-service",
        ValidateLifetime = true, RequireSignedTokens = true,
        ValidAlgorithms = ["RS256"],
        ClockSkew = TimeSpan.FromSeconds(30),
        TryAllIssuerSigningKeys = false,
    };
});
builder.Services.AddTrellisInternalJwtActorProvider(o =>
{
    o.RequiredAttributes = ["tenant_id"];
    o.AttributeClaimMap["tenant_id"] = "tid";
    o.ExpectedIssuer = "https://gateway.internal";
    o.ExpectedAudience = "incidents-service";
});
```

> Upstream `Trellis.ServiceDefaults` ≤ `3.0.0-alpha.342` binds `TrellisServiceBuilder.UseTrellisInternalJwtActor` to the legacy `Trellis.Asp.Authorization` provider; use the direct `AddTrellisInternalJwtActorProvider(...)` shown above until upstream is rewired.

## Pairs with

- `Trellis.Yarp` — the gateway-side counterpart that mints these JWTs.
- `Trellis.Microservices.Abstractions` (transitive) — shared `TrellisInternalJwtClaimNames` contract literals.

## Documentation

Full reference: [`trellis-api-internal-jwt.md`](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-internal-jwt.md).

End-to-end recipe: [`trellis-api-microservices-cookbook.md`](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md).

## License

MIT.
