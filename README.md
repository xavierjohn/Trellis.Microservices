# Trellis.Microservices

[![Build](https://github.com/xavierjohn/Trellis.Microservices/actions/workflows/build.yml/badge.svg)](https://github.com/xavierjohn/Trellis.Microservices/actions/workflows/build.yml)
[![NuGet: Trellis.Microservices.Abstractions](https://img.shields.io/nuget/v/Trellis.Microservices.Abstractions.svg?label=Trellis.Microservices.Abstractions)](https://www.nuget.org/packages/Trellis.Microservices.Abstractions)
[![NuGet: Trellis.Microservices.AspNetCore](https://img.shields.io/nuget/v/Trellis.Microservices.AspNetCore.svg?label=Trellis.Microservices.AspNetCore)](https://www.nuget.org/packages/Trellis.Microservices.AspNetCore)
[![NuGet: Trellis.Yarp](https://img.shields.io/nuget/v/Trellis.Yarp.svg?label=Trellis.Yarp)](https://www.nuget.org/packages/Trellis.Yarp)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trellis.Microservices.AspNetCore.svg)](https://www.nuget.org/packages/Trellis.Microservices.AspNetCore)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![C#](https://img.shields.io/badge/C%23-14.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![GitHub Stars](https://img.shields.io/github/stars/xavierjohn/Trellis.Microservices?style=social)](https://github.com/xavierjohn/Trellis.Microservices/stargazers)

> Microservices building blocks for [Trellis](https://github.com/xavierjohn/Trellis): YARP gateway that mints internal-network JWTs + consumer-side actor provider enforcing a strict claim contract that defends multi-tenant ABAC.

This repository ships three NuGet packages that pair a YARP gateway (minter) with downstream services (consumers) over a tightly-defined internal JWT contract. Both sides reference the same shared constants so any contract change is one coordinated edit.

## Try it now

The fastest way to see this in action is the companion [`Trellis.Microservices.Template`](https://github.com/xavierjohn/Trellis.Microservices.Template):

```bash
dotnet new install Trellis.Microservices.Templates
dotnet new trellis-microservices -n MyOrg.Tracker
cd MyOrg.Tracker
dotnet run --project AppHost
```

You get a working multi-tenant Project Tracker topology — Aspire-orchestrated — with click-to-send scenarios for every authorization outcome.

## Packages

| Package | Role |
| --- | --- |
| [Trellis.Microservices.Abstractions](https://www.nuget.org/packages/Trellis.Microservices.Abstractions) | Shared `TrellisInternalJwtClaimNames` constants. AOT-compatible, zero runtime dependencies. Reference this directly only when implementing a third-party gateway or a custom actor provider; otherwise both packages below bring it in transitively. |
| [Trellis.Yarp](https://www.nuget.org/packages/Trellis.Yarp) | YARP integration. `AddTrellisActorForwarding()` mints a per-cluster internal JWT from the full Trellis `Actor`; `MapTrellisDiscoveryEndpoint()` publishes OIDC discovery + JWKS for downstream services. |
| [Trellis.Microservices.AspNetCore](https://www.nuget.org/packages/Trellis.Microservices.AspNetCore) | Consumer-side counterpart. `TrellisInternalJwtActorProvider` hydrates the full `Actor` (id + permissions + forbidden permissions + ABAC attributes) from a verified gateway-minted JWT with strict sentinel + count claim enforcement against proxy-strip attacks. AOT-compatible. |

## Gateway minute one

```csharp
using Microsoft.IdentityModel.Tokens;
using Trellis.Asp.Authorization;
using Trellis.Yarp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDevelopmentActorProvider();   // dev only; swap for ClaimsActorProvider / EntraActorProvider in production.

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTrellisActorForwarding(o =>
    {
        o.Issuer          = "https://gateway.internal";
        o.PublicBaseUrl   = new Uri("https://gateway.internal");
        o.SigningCredentials = new SigningCredentials(myRsaKey, SecurityAlgorithms.RsaSha256);
        o.AudiencePerCluster = cluster => cluster.ClusterId;   // per-cluster audience pinning
    });

var app = builder.Build();
app.MapTrellisDiscoveryEndpoint();   // /.well-known/jwks.json + /.well-known/openid-configuration
app.MapReverseProxy();
app.Run();
```

## Consumer minute one

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Trellis.Microservices.AspNetCore;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = "https://gateway.internal";   // auto-discovers signing keys from JWKS
        o.Audience  = "orders";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,   ValidIssuer    = "https://gateway.internal",
            ValidateAudience = true, ValidAudience  = "orders",
            ValidateLifetime = true,
            ValidAlgorithms = ["RS256"],
        };
    });

builder.Services.AddTrellisInternalJwtActorProvider(o =>
{
    o.ExpectedIssuer    = "https://gateway.internal";
    o.ExpectedAudience  = "orders";
    o.RequiredAttributes = ["tenant_id"];   // multi-tenancy enforced at JWT validation, not later
});
```

## What you get

- **Trust-boundary by design.** Gateway and consumer agree on a single contract (`Trellis.Microservices.Abstractions`); any future change is one coordinated edit on both sides.
- **Multi-tenant from line 1.** `tenant_id` ABAC claim flows through every minted token and the consumer-side validator rejects tokens missing it — no silent tenant escape.
- **Sentinel + count claims** defend the deny-overrides-allow invariant against a proxy that strips the forbidden-permissions array but leaves the allow list intact.
- **Transparent key rotation.** Gateway publishes JWKS at a stable URL; downstream services use `AddJwtBearer(o.Authority = gatewayUrl)` and never see a config change during rotation.
- **Asymmetric-only signing** at runtime — symmetric keys + HMAC algorithms rejected at startup so a misconfiguration cannot silently weaken security.
- **AOT-friendly** on the consumer-side (`Trellis.Microservices.Abstractions` + `Trellis.Microservices.AspNetCore`). `Trellis.Yarp` is intentionally non-AOT because YARP itself is not AOT-clean.

## Threat model

This repository ships JWT-minting and JWT-validation code that downstream services trust as authoritative identity. **Signing-key compromise = full identity spoof until key revocation propagates.** Mitigations baked into the design:

- Asymmetric-only signing (symmetric keys + HMAC algorithms rejected at startup).
- Every `SigningCredentials` must carry a non-empty `Kid`.
- Short token lifetimes (default 5 minutes, capped at `[1m, 30m]`).
- `kid`-aware overlapping JWKS rotation (active + every `PreviousSigningKeys` entry).
- Audit-log redaction (no JWT body, no raw claim values, no actor IDs in any `[LoggerMessage]` event).
- Sentinel + count claims defending the deny-overrides-allow invariant.

## Documentation

| Doc | Purpose |
|---|---|
| [Microservices cookbook](docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md) | LLM entry point. Task-lookup table + recipes + known anti-patterns. Load this first. |
| [`Trellis.Microservices.Abstractions` API reference](docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md) | Shared claim-name constants + contract integrity rules. |
| [`Trellis.Yarp` API reference](docs/docfx_project/api_reference/trellis-api-yarp.md) | Gateway-side: options, minter, discovery endpoint, audit-log redaction contract, internal JWT v1 claim set. |
| [`Trellis.Microservices.AspNetCore` API reference](docs/docfx_project/api_reference/trellis-api-internal-jwt.md) | Consumer-side: options, validator rules, migration note for early adopters. |
| [`.github/copilot-instructions.md`](.github/copilot-instructions.md) | Repository conventions and the "P4 invariants — never regress" checklist. |

## Related repositories

- [`xavierjohn/Trellis`](https://github.com/xavierjohn/Trellis) — the framework: `Result<T>`, `Maybe<T>`, value objects, DDD primitives, ASP.NET / EF Core / Mediator integration.
- [`xavierjohn/Trellis.Microservices.Template`](https://github.com/xavierjohn/Trellis.Microservices.Template) — `dotnet new trellis-microservices` Project Tracker starter that scaffolds a multi-tenant topology using these packages.
- [`xavierjohn/Trellis.AspTemplate`](https://github.com/xavierjohn/Trellis.AspTemplate) — single-service Clean Architecture template (no microservices topology).

## License

[MIT](LICENSE).