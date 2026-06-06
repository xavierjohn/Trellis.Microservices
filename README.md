# Trellis.Microservices

Microservice trust-boundary support for the [Trellis framework](https://github.com/xavierjohn/Trellis): gateway-side JWT minting, consumer-side actor hydration from the minted JWT, and the shared contract constants both sides must agree on.

This repository ships three NuGet packages:

| Package | Role |
|---|---|
| [`Trellis.Microservices.Abstractions`](docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md) | Shared `TrellisInternalJwtClaimNames` constants — the source-of-truth for claim literals both gateway and consumer reference. Tiny, AOT-compatible, no runtime dependencies. |
| [`Trellis.Yarp`](docs/docfx_project/api_reference/trellis-api-yarp.md) | YARP reverse-proxy integration. `AddTrellisActorForwarding(...)` mints a per-cluster internal JWT from the full Trellis `Actor`; `MapTrellisDiscoveryEndpoint()` publishes OIDC discovery + JWKS for downstream services. |
| [`Trellis.Microservices.AspNetCore`](docs/docfx_project/api_reference/trellis-api-internal-jwt.md) | Consumer-side counterpart. `TrellisInternalJwtActorProvider` hydrates the full `Actor` (id + permissions + forbidden permissions + ABAC attributes) from a verified gateway-minted JWT, with strict sentinel + count claim enforcement against proxy-strip attacks. |

## Getting started

The canonical end-to-end guide lives in [`trellis-api-microservices-cookbook.md`](docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md):

- **Recipe 1** — strict `AddJwtBearer` validation profile for `UseTrellisInternalJwtActor`.
- **Recipe 2** — microservices behind YARP, end-to-end (mint → project per cluster → publish discovery → consume downstream → rotate keys → emergency revocation).

## Threat model

This repository ships JWT-minting and JWT-validation code that downstream services trust as authoritative identity. **Signing-key compromise = full identity spoof until key revocation propagates.** The packages are designed for that scenario:

- Asymmetric-only signing (symmetric keys + HMAC algorithms rejected at startup).
- Every `SigningCredentials` must carry a non-empty `Kid`.
- Short token lifetimes (default 5 minutes, capped at `[1m, 30m]`).
- `kid`-aware overlapping JWKS rotation (active + every `PreviousSigningKeys` entry).
- Audit-log redaction (no JWT body, no raw claim values, no actor IDs in any `[LoggerMessage]` event).
- Sentinel + count claims defending the deny-overrides-allow invariant against a proxy stripping the deny set.

See [`.github/copilot-instructions.md`](.github/copilot-instructions.md) for the "P4 invariants — never regress" checklist that any change touching minter / validator / provider code must preserve.

## Relationship to `xavierjohn/Trellis`

This repo is a peer of the main [`xavierjohn/Trellis`](https://github.com/xavierjohn/Trellis) framework. The main repo owns the core primitives (`Actor`, `IActorProvider`, `Result<T>`, `Maybe<T>`, ASP.NET integration, EF Core integration, etc.). This repo owns the microservice trust-boundary surface. Packages here depend on the main framework via NuGet.

The microservices cookbook in this repo references upstream main-repo docs (`trellis-api-authorization.md`, `trellis-api-asp.md`, `trellis-api-servicedefaults.md`, plus Recipes 7 and 32) as required reading for any non-trivial change.

## Documentation

| Doc | Purpose |
|---|---|
| [`trellis-api-microservices-cookbook.md`](docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md) | LLM entry point. Task-lookup table + recipes + known anti-patterns. Load this first. |
| [`trellis-api-microservices-abstractions.md`](docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md) | `TrellisInternalJwtClaimNames` constants + contract integrity rules. |
| [`trellis-api-yarp.md`](docs/docfx_project/api_reference/trellis-api-yarp.md) | Gateway-side: options, minter, discovery endpoint, audit-log redaction contract, internal JWT v1 claim set. |
| [`trellis-api-internal-jwt.md`](docs/docfx_project/api_reference/trellis-api-internal-jwt.md) | Consumer-side: `TrellisInternalJwtActorOptions`, `TrellisInternalJwtActorProvider`, validator rules, migration note for early adopters. |
| [`.github/copilot-instructions.md`](.github/copilot-instructions.md) | Conventions for any agent (Copilot, sub-agents, or human) working in this repo. |

## License

[MIT](LICENSE).
