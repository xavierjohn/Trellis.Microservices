# Trellis.Microservices.AspNetCore

Consumer-side counterpart to [`Trellis.Yarp`](../Trellis.Yarp/). Hydrates the full Trellis `Actor` (id + permissions + forbidden permissions + ABAC attributes) from a verified gateway-minted internal JWT, enforcing the strict sentinel + count claim contract that defends the deny-overrides-allow invariant against a proxy stripping the deny set.

## Usage

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

## Dependencies

- [`Trellis.Microservices.Abstractions`](../docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md#use-this-file-when) — shared `TrellisInternalJwtClaimNames` contract literals (transitive).
- Upstream [`Trellis.Authorization`](https://github.com/xavierjohn/Trellis) — `Actor`, `IActorProvider`.
- Upstream [`Trellis.Asp`](https://github.com/xavierjohn/Trellis) — `IProvideActorVaryHeaders` (cache-key partitioning by actor).

## License

[MIT](../LICENSE).
