---
package: Trellis.Microservices.AspNetCore
namespaces: [Trellis.Microservices.AspNetCore]
types: [TrellisInternalJwtActorOptions, TrellisInternalJwtActorProvider, TrellisInternalJwtActorOptionsValidator, ServiceCollectionExtensions]
version: v1
last_verified: 2026-06-05
audience: [llm]
---
# Trellis.Microservices.AspNetCore — API Reference (consumer-side internal JWT)

**Package:** `Trellis.Microservices.AspNetCore` (AOT-compatible — `TrellisInternalJwtActorProvider` uses only `Microsoft.AspNetCore.Authentication`, `System.Security.Claims`, and the canonical claim-name constants from `Trellis.Microservices.Abstractions`; no reflection, no `JsonSerializer.Deserialize<T>(...)` against open generics).
**Namespaces:** `Trellis.Microservices.AspNetCore` (formerly `Trellis.Asp.Authorization.TrellisInternalJwt*` in `xavierjohn/Trellis` — namespace **moved** for this package family; the type names did not change).
**Purpose:** Consumer-side counterpart to [`Trellis.Yarp`](trellis-api-yarp.md#use-this-file-when). Hydrates the full `Actor` surface (`Id` + `Permissions` + `ForbiddenPermissions` + `Attributes`) from a verified gateway-minted internal JWT. Enforces the strict sentinel + count claim contract that defends the deny-overrides-allow invariant against a misbehaving proxy stripping the deny set.

## Use this file when

- You are configuring a downstream microservice that consumes JWTs minted by `Trellis.Yarp` (or a third-party gateway implementing the same contract).
- You need the exact signatures of `AddTrellisInternalJwtActorProvider` or any of the `TrellisInternalJwtActorOptions` knobs.
- You are auditing the consumer-side fail-closed posture (sentinel claim, count mismatches, strict claim shape, required-attribute enforcement).
- You are wiring an alternative authentication scheme (cookie, mTLS, forwarded-headers) — the provider does not assume `Bearer`; it is `AuthenticationScheme`-driven.

## Patterns Index

| Goal | Canonical API / action | See |
|---|---|---|
| Register the actor provider | `services.AddTrellisInternalJwtActorProvider(o => ...)` (direct `IServiceCollection` extension; no `TrellisServiceBuilder` slot — the previous `UseTrellisInternalJwtActor` slot in upstream `Trellis.ServiceDefaults` was removed in v3 when the implementation moved to this package) | [`ServiceCollectionExtensions`](#servicecollectionextensions) |
| Map an ABAC attribute from a JWT claim | `options.AttributeClaimMap["tenant_id"] = "tenant_id"` | [`TrellisInternalJwtActorOptions`](#trellisinternaljwtactoroptions) |
| Require an attribute on every request | `options.RequiredAttributes = ["tenant_id"]` (entry MUST also be in `AttributeClaimMap`) | [`TrellisInternalJwtActorOptions`](#trellisinternaljwtactoroptions) |
| Cross-check the JWT issuer / audience defense-in-depth | `options.ExpectedIssuer = "..."`, `options.ExpectedAudience = "..."` | [`TrellisInternalJwtActorOptions`](#trellisinternaljwtactoroptions) |
| Surface the upstream IdP issuer as an actor attribute (multi-IdP gateways) | `options.AttributeClaimMap["external_iss"] = "external_iss"` then read via `actor.Attributes["external_iss"]` | [Recipe 2 — Multi-IdP namespacing](trellis-api-microservices-cookbook.md#recipe-2--microservices-behind-yarp-end-to-end) |
| Drop the strict-shape guard (NOT recommended) | `options.StrictClaimShape = false` | [`TrellisInternalJwtActorOptions`](#trellisinternaljwtactoroptions) |

## Threat model

This package is the trust-boundary entry point on the consumer side. The gateway has authenticated the upstream call and minted a fresh internal JWT carrying the projected actor surface. The consumer trusts the JWT signature; this provider's role is to defend against the **integrity** edges that signature validation alone does not cover:

| Attack | Mitigation |
|---|---|
| Misbehaving proxy strips the multi-valued `forbidden_permissions` claim → request that should be denied is allowed | Sentinel + count claims: provider requires `trellis_actor_contract_version = "1"` AND `trellis_forbidden_permissions_count` to equal the observed number of `forbidden_permissions` claims. The gateway MUST emit the count even when zero, so missing-count is distinguishable from empty-set. |
| Gateway-side bug joins `["a", "b"]` into `"a,b"` for a multi-valued claim | `StrictClaimShape = true` rejects permission / forbidden / mapped-attribute values containing `,` or starting with `[` / `{`. |
| Application middleware planted a `ClaimsPrincipal` with overlapping claim names → provider silently translates forged claims | Provider authenticates the configured `AuthenticationScheme` via `HttpContext.AuthenticateAsync(scheme)` — it NEVER reads `HttpContext.User` directly. |
| Reserved JWT claim name reused as a permission source (e.g. `PermissionsClaim = "iss"`) → misconfiguration silently maps the registered claim into the actor | `IValidateOptions<TrellisInternalJwtActorOptions>` runs at startup via `ValidateOnStart()` and rejects reserved claim names as permission / forbidden / attribute sources. Override only with `UnsafeAllowRegisteredClaimNames = true` and a documented rationale. |
| Two different upstream IdPs produce colliding `sub` values | This provider does not solve sub-collision directly; the GATEWAY side (`Trellis.Yarp.TrellisActorForwardingOptions.ActorIdResolver`) namespaces `sub` before minting. The consumer's `ActorIdClaim = "sub"` then resolves the namespaced value. See `trellis-api-yarp.md` and Recipe 2. |

The startup validator additionally rejects: empty `VaryByHeaders`, `RequiredAttributes` entries not present in `AttributeClaimMap`, claim-name collisions across the configured claim slots.

---

## `TrellisInternalJwtActorOptions`

**Declaration**

```csharp
public sealed class TrellisInternalJwtActorOptions
```

Controls how `TrellisInternalJwtActorProvider` translates a verified internal-network JWT — typically minted by a trusted gateway running the `Trellis.Yarp` transform or an equivalent third-party gateway implementing the same contract — into the full `Actor` surface.

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `AuthenticationScheme` | `string` | `"Bearer"` | The ASP.NET Core authentication scheme to authenticate via `HttpContext.AuthenticateAsync(scheme)`. The provider NEVER reads `HttpContext.User` directly — a misconfigured middleware planting a `ClaimsPrincipal` with matching claim names would otherwise silently translate forged claims. |
| `ActorIdClaim` | `string` | `"sub"` (= `TrellisInternalJwtClaimNames.Subject`) | The claim type used to resolve `Actor.Id`. The same short↔long claim-name fallback used by `ClaimsActorProvider` is applied for the actor-id-only subset of the JWT inbound claim-type map (handles `MapInboundClaims = true` remap of `sub` → `ClaimTypes.NameIdentifier`). |
| `PermissionsClaim` | `string` | `"permissions"` (= `TrellisInternalJwtClaimNames.Permissions`) | Multi-valued claim — each instance contributes one permission to `Actor.Permissions`. |
| `ForbiddenPermissionsClaim` | `string` | `"forbidden_permissions"` (= `TrellisInternalJwtClaimNames.ForbiddenPermissions`) | Multi-valued claim — each instance contributes one deny to `Actor.ForbiddenPermissions`. |
| `AttributeClaimMap` | `Dictionary<string, string>` | empty | Map from logical attribute names (the keys exposed via `Actor.Attributes`) to underlying JWT claim types. Only attributes explicitly mapped flow into `Actor.Attributes`. |
| `RequiredAttributes` | `IReadOnlyList<string>` | empty | Attribute names that MUST be present and non-empty on every request. Each name must also be a key in `AttributeClaimMap` (startup-validated). Missing / empty / duplicated → `Maybe<Actor>.None`. |
| `ExpectedIssuer` | `string` | `""` | When non-empty, the provider runtime-checks the JWT's `iss` claim ordinal-equal to this value and fails closed on mismatch. **Defense-in-depth complement to `JwtBearerOptions.TokenValidationParameters.ValidIssuer`, NOT a substitute.** |
| `ExpectedAudience` | `string` | `""` | When non-empty, the provider requires at least one `aud` claim ordinal-equal to this value. Defense-in-depth complement to `ValidAudience`. |
| `StrictClaimShape` | `bool` | `true` | When `true`, permission / forbidden-permission / mapped-attribute values containing commas or starting with `[` / `{` are rejected (these shapes indicate a gateway-side bug that comma-joined a set or serialized JSON into a single claim value). |
| `UnsafeAllowRegisteredClaimNames` | `bool` | `false` | Escape hatch disabling the startup-validator's rule that rejects reserved JWT claim names (`iss`, `aud`, `exp`, `nbf`, `iat`, `jti`, `sub`) as permission / forbidden-permission / attribute claim sources. Set `true` only with explicit rationale documented in your composition root. |
| `ContractVersionClaim` | `string` | `"trellis_actor_contract_version"` (= `TrellisInternalJwtClaimNames.ContractVersion`) | Sentinel claim type; the provider rejects requests whose JWT lacks this claim or whose value differs from `ExpectedContractVersion`. |
| `PermissionsCountClaim` | `string` | `"trellis_permissions_count"` (= `TrellisInternalJwtClaimNames.PermissionsCount`) | Decimal-integer count claim; must equal the observed number of `PermissionsClaim` claims on the principal. Parsed strictly (`NumberStyles.None`, `CultureInfo.InvariantCulture`, non-negative). |
| `ForbiddenPermissionsCountClaim` | `string` | `"trellis_forbidden_permissions_count"` (= `TrellisInternalJwtClaimNames.ForbiddenPermissionsCount`) | Same shape; gateway MUST emit `"0"` for empty deny sets so empty is distinguishable from absent (proxy-strip detection). |
| `ExpectedContractVersion` | `string` | `"1"` (= `TrellisInternalJwtClaimNames.CurrentContractVersion`) | The expected `ContractVersionClaim` literal value. |
| `VaryByHeaders` | `IReadOnlyCollection<string>` | `["Authorization"]` | HTTP request headers that contribute to actor identity for `HttpResponseOptionsBuilder.VaryForActor()` cache partitioning. Override when `AuthenticationScheme` is a non-Bearer scheme (e.g. `["Cookie"]` for cookie auth). Empty collection is rejected at startup. |

---

## `TrellisInternalJwtActorProvider`

**Declaration**

```csharp
public sealed partial class TrellisInternalJwtActorProvider : IActorProvider, IProvideActorVaryHeaders
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisInternalJwtActorProvider(IHttpContextAccessor httpContextAccessor, IOptions<TrellisInternalJwtActorOptions> options, ILogger<TrellisInternalJwtActorProvider>? logger = null)` | — | Constructor; `logger` defaults to `NullLogger.Instance`. All log entries are PII-redacted (no JWT body, no claim values, no actor IDs — only scheme names, claim TYPE names, counts, and consumer-configured literals). |
| `public IReadOnlyCollection<string> VaryByHeaders { get; }` | `IReadOnlyCollection<string>` | Passes through `TrellisInternalJwtActorOptions.VaryByHeaders`. |
| `public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Maybe<Actor>>` | Authenticates the configured scheme via `HttpContext.AuthenticateAsync(scheme)`, validates the sentinel + count claim contract, applies `StrictClaimShape` to permission / forbidden / attribute values, enforces `RequiredAttributes`, optionally cross-checks `ExpectedIssuer` / `ExpectedAudience`, and returns a fully-hydrated `Actor` on success. Returns `Maybe<Actor>.None` on any validation failure — the mediator pipeline maps that to `Error.AuthenticationRequired` (HTTP 401). Throws `InvalidOperationException` only when `HttpContext` is missing (configuration bug → HTTP 500). |

The provider implements `IProvideActorVaryHeaders` (from `Trellis.Asp`); when paired with `HttpResponseOptionsBuilder<T>.VaryForActor()`, the response gets `Vary: Authorization` (or whatever the consumer configured) automatically so per-actor cache partitioning works correctly behind shared caches.

> **Logging redaction contract.** The provider's `[LoggerMessage]` events (`InternalJwtSentinelMissingOrDuplicated`, `InternalJwtContractVersionMismatch`, `InternalJwtCountClaimMalformed`, `InternalJwtPermissionsCountMismatch`, `InternalJwtForbiddenPermissionsCountMismatch`, `InternalJwtStrictClaimShapeRejection`, `InternalJwtRequiredAttributeMissing`, `InternalJwtRequiredAttributeEmpty`, `InternalJwtAttributeDuplicated`, `InternalJwtExpectedIssuerMismatch`, `InternalJwtExpectedAudienceMismatch`) carry low-cardinality metadata only: scheme name, claim TYPE names, counts, and CONSUMER-configured literal expected values. They NEVER log the JWT body, observed claim values, actor IDs, or other PII. Verified by `TrellisInternalJwtActorProviderTests.GetCurrentActorAsync_LoggingRedaction_NeverLogsClaimValuesOnFailurePaths`.

---

## `ServiceCollectionExtensions`

**Declaration**

```csharp
public static class ServiceCollectionExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisInternalJwtActorProvider(this IServiceCollection services, Action<TrellisInternalJwtActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures and startup-validates `TrellisInternalJwtActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `TrellisInternalJwtActorProvider`. For microservices that consume internal-network JWTs minted by a trusted gateway (typically `Trellis.Yarp` or an equivalent third-party gateway implementing the same sentinel + count claim contract). Hydrates the FULL `Actor` surface (id + permissions + forbidden permissions + ABAC attributes). The companion `IValidateOptions<TrellisInternalJwtActorOptions>` validator runs via `ValidateOnStart()` and rejects misconfigurations before the first request. |

> **Replacement semantics.** Calling `AddTrellisInternalJwtActorProvider` Replace-registers the `IActorProvider` slot — the previous registration is removed and exactly one descriptor remains. Calling multiple `Add*ActorProvider` helpers leaves the last one wins; chained `AddCachingActorProvider<TrellisInternalJwtActorProvider>()` will wrap this provider for per-scope actor caching.

**Composition root entry.** Call this extension directly against `IServiceCollection`:

```csharp
builder.Services.AddTrellisInternalJwtActorProvider(o =>
{
    o.ExpectedIssuer = "https://gateway.internal";
    o.ExpectedAudience = "orders-api";
    o.AttributeClaimMap["tenant_id"] = "tenant_id";
    o.RequiredAttributes = ["tenant_id"];
});

builder.Services.AddTrellis(b => b
    // Other Trellis builder slots (UseMediator, UseAsp, etc.) — but NOT UseTrellisInternalJwtActor,
    // which was removed from upstream Trellis.ServiceDefaults in v3 when this provider moved
    // to Trellis.Microservices.AspNetCore.
    .UseMediator()
    .UseAsp());
```

The `IActorProvider` slot is shared between this extension and `services.AddTrellis(...)` because both resolve into the same `IServiceCollection`; the call order doesn't matter, and the single-slot enforcement runs at host start.

---

## `TrellisInternalJwtActorOptionsValidator`

**Declaration**

```csharp
internal sealed class TrellisInternalJwtActorOptionsValidator : IValidateOptions<TrellisInternalJwtActorOptions>
```

Registered automatically by `AddTrellisInternalJwtActorProvider` and gated by `ValidateOnStart()`. Throws on host start when:

| Rule | Why |
|---|---|
| `AuthenticationScheme` is null or empty | Provider cannot resolve a scheme to authenticate with. |
| `ActorIdClaim`, `PermissionsClaim`, `ForbiddenPermissionsClaim`, `ContractVersionClaim`, `PermissionsCountClaim`, or `ForbiddenPermissionsCountClaim` is null or empty | One of the contract-load claim slots is undefined. |
| Two claim slots resolve to the same JWT claim name (e.g. `PermissionsClaim == ForbiddenPermissionsClaim`) | One claim value would be interpreted as both an allow and a deny — silently fail-open posture. |
| `ExpectedContractVersion` is null or empty | Sentinel check would be no-op. |
| `VaryByHeaders` is empty | Cache-partitioning by actor would be disabled silently — endpoints using `VaryForActor()` would leak responses across actors. |
| An entry in `RequiredAttributes` is not a key of `AttributeClaimMap` | Attribute is unreachable — provider would always fail-closed at runtime. |
| A reserved JWT claim name (`iss`/`aud`/`exp`/`nbf`/`iat`/`jti`/`sub`) is used as `PermissionsClaim` / `ForbiddenPermissionsClaim` / any `AttributeClaimMap` value | The registered claim would be reinterpreted as a permission / attribute — high-blast-radius misconfiguration. Override only with `UnsafeAllowRegisteredClaimNames = true` and documented rationale. |

Fail-closed posture: the validator runs at startup and throws `OptionsValidationException`. There is no runtime fallback that "tries best-effort" — the host refuses to start.

---

## Cross-references

- [`trellis-api-yarp.md`](trellis-api-yarp.md#use-this-file-when) — gateway-side companion that mints the JWT this provider consumes; the [Internal JWT contract v1](trellis-api-yarp.md#internal-jwt-contract-v1) table is the source-of-truth for claim names and shapes.
- [`trellis-api-microservices-abstractions.md`](trellis-api-microservices-abstractions.md#use-this-file-when) — `TrellisInternalJwtClaimNames` constants both sides reference.
- [Recipe 1 — Strict `AddJwtBearer` profile](trellis-api-microservices-cookbook.md#recipe-1--strict-addjwtbearer-validation-profile-for-addtrellisinternaljwtactorprovider) — the mandatory companion `AddJwtBearer` config (`MapInboundClaims = false`, `TryAllIssuerSigningKeys = false`, `ValidAlgorithms = [activeAlg]`, `ClockSkew = 30s`).
- [Recipe 2 — Microservices behind YARP, end-to-end](trellis-api-microservices-cookbook.md#recipe-2--microservices-behind-yarp-end-to-end) — full worked example including tenant-isolation defense-in-depth, multi-IdP namespacing, key rotation, emergency revocation.
- Upstream [`trellis-api-asp.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-asp.md) (in `xavierjohn/Trellis`) — `IProvideActorVaryHeaders`, the other actor-provider implementations (`ClaimsActorProvider`, `EntraActorProvider`, `DevelopmentActorProvider`), `CachingActorProvider` (composable with this provider).
- Upstream [`trellis-api-authorization.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-authorization.md) (in `xavierjohn/Trellis`) — `Actor`, `IActorProvider`, deny-overrides-allow contract integrity invariant, `IAuthorizeResource<T>` for tenant ABAC.

## Migration note (preview-stage adopters of `Trellis.Asp.Authorization.TrellisInternalJwt*`)

If you adopted P3 / P3.5 from `xavierjohn/Trellis` before this package shipped, the types listed below moved namespaces. There were no behavior changes — only `using` directives and package references.

| Old (in `xavierjohn/Trellis`) | New (in `xavierjohn/Trellis.Microservices`) |
|---|---|
| `using Trellis.Asp.Authorization;` (for `TrellisInternalJwt*`) | `using Trellis.Microservices.AspNetCore;` |
| `Trellis.Asp` NuGet package | `Trellis.Microservices.AspNetCore` NuGet package (add reference) + still keep `Trellis.Asp` (for everything else) |
| `services.AddTrellisInternalJwtActorProvider(...)` | unchanged (extension method is `IServiceCollection`-scoped, namespace move only) |
| `services.AddTrellis(b => b.UseTrellisInternalJwtActor(...))` | `services.AddTrellisInternalJwtActorProvider(...)` — call directly against `IServiceCollection`. The upstream `TrellisServiceBuilder.UseTrellisInternalJwtActor` slot was removed in v3 cleanup (after the implementation moved to this package), so the call site DOES change. The two registrations target the same `IActorProvider` slot. |
