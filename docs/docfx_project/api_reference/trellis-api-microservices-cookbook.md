---
package: Trellis.Microservices (cross-package recipes)
namespaces: [Trellis.Yarp, Trellis.Microservices.AspNetCore, Trellis.Microservices.Abstractions]
types: [recipes]
related_docs: [trellis-api-yarp.md, trellis-api-internal-jwt.md, trellis-api-microservices-abstractions.md]
upstream_repo: https://github.com/xavierjohn/Trellis
upstream_required_docs: [trellis-api-authorization.md, trellis-api-asp.md, trellis-api-servicedefaults.md]
version: v1
last_verified: 2026-06-05
audience: [llm]
---
# Trellis Microservices Cookbook

- **Audience:** AI coding agents (and humans) writing Trellis microservice code from documentation alone.
- **Purpose:** End-to-end recipes for the Path B (Trellis internal JWT) microservice pattern — gateway-side JWT minting, consumer-side actor hydration, the strict `AddJwtBearer` profile, key-rotation runbook, emergency revocation procedure, and the multi-tenant ABAC enforcement story. Recipes use the *exact* public surface listed in this repo's per-package API references; foundational Trellis primitives (`Actor`, `IActorProvider`, `Result<T>`) are documented in the [main Trellis repo](https://github.com/xavierjohn/Trellis/tree/main/docs/docfx_project/api_reference).

- **Companion docs (this repo):**
  - [trellis-api-yarp.md](trellis-api-yarp.md#use-this-file-when) — `TrellisActorForwardingOptions`, `AddTrellisActorForwarding`, `MapTrellisDiscoveryEndpoint`
  - [trellis-api-internal-jwt.md](trellis-api-internal-jwt.md#use-this-file-when) — `TrellisInternalJwtActorProvider`, `TrellisInternalJwtActorOptions`, `AddTrellisInternalJwtActorProvider`
  - [trellis-api-microservices-abstractions.md](trellis-api-microservices-abstractions.md#use-this-file-when) — `TrellisInternalJwtClaimNames`, contract version constants

- **Required upstream docs (load from xavierjohn/Trellis):**
  - [`trellis-api-authorization.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-authorization.md) — `Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<>` (every minted JWT hydrates back into an `Actor`)
  - [`trellis-api-asp.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-asp.md) — `ClaimsActorProvider`, `EntraActorProvider` (the gateway-side actor sources that feed `AddTrellisActorForwarding`)
  - [`trellis-api-servicedefaults.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-servicedefaults.md) — `AddTrellis`, `TrellisServiceBuilder` (the composition root; for the internal-JWT consumer call `services.AddTrellisInternalJwtActorProvider(...)` directly — the `UseTrellisInternalJwtActor` slot was removed in v3 when the implementation moved to this repo)
  - [`trellis-api-cookbook.md` Recipe 7](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth) — the 3-path microservices framing (this repo implements Path B)
  - [`trellis-api-cookbook.md` Recipe 32](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) — `AuthFailureExposurePolicy.HideAsNotFound` (orthogonal Mediator behavior that pairs naturally with this repo's tenant-isolation pattern)

## How to read these recipes

Every recipe follows the same shape (matches main repo cookbook conventions):

1. **Problem statement** — what the consumer is trying to accomplish.
2. **Solution code** — copy-pasteable C# that compiles against the documented public surface only. No invented APIs.
3. **What it shows** — the cross-cutting concept being demonstrated.
4. **Trust boundary notes** *(when applicable)* — the security implications, threat model, and mitigations.

Conventions:

- All gateway types live in the `Trellis.Yarp` namespace.
- All consumer-side actor-hydration types live in the `Trellis.Microservices.AspNetCore` namespace.
- Shared contract constants live in the `Trellis.Microservices.Abstractions` namespace.
- Snippets use C# 12+ features; both packages target `net10.0`.
- `Trellis.Yarp` is **not AOT-compatible** (YARP isn't AOT-clean); `Trellis.Microservices.AspNetCore` and `Trellis.Microservices.Abstractions` are AOT-friendly.
- Examples use the multi-tenant Project Tracker domain (`Projects` cluster + `Members` cluster) — same shape as the [companion template repo](https://github.com/xavierjohn/Trellis.Microservices.Template).

## LLM preflight: load the smallest correct reference set

Before writing microservice code, choose the task in the lookup table below and load only the references needed. The cookbook gives the end-to-end recipe; the package references are the source of truth for exact signatures, overloads, and edge-case behavior.

| If you are changing... | Load these references before coding | Why |
|---|---|---|
| Gateway-side YARP transform (mint, projection callbacks, audience selection, ActorIdResolver) | `trellis-api-yarp.md`, `trellis-api-microservices-abstractions.md`; upstream `trellis-api-authorization.md` for the `Actor` source type | The minter consumes `Actor` from the upstream; this repo's options surface controls how the Actor maps to claims. |
| Gateway-side discovery endpoint (OIDC + JWKS) | `trellis-api-yarp.md` only | Self-contained on the gateway side. |
| Gateway-side signing key rotation | `trellis-api-yarp.md` AND this cookbook's Recipe 1 key-rotation runbook | The runbook spans both sides; both halves must stay in lock-step. |
| Consumer-side actor hydration from minted JWT | `trellis-api-internal-jwt.md`, `trellis-api-microservices-abstractions.md`; upstream `trellis-api-authorization.md` for `Actor`/`IActorProvider`, upstream `trellis-api-asp.md` for `AddJwtBearer` integration | `TrellisInternalJwtActorProvider` produces the same `Actor` shape the gateway minted from; the contract claim names live in Abstractions. |
| Consumer-side strict `AddJwtBearer` profile | This cookbook's Recipe 1 (strict profile); `trellis-api-internal-jwt.md` for the consumer-side options | The strict profile is the mandatory companion to `AddTrellisInternalJwtActorProvider`. |
| Tenant-isolation ABAC enforcement (downstream resource auth defending against gateway-claim spoof) | This cookbook's Recipe 2 (end-to-end); upstream `trellis-api-authorization.md` for `IAuthorizeResource<>` | The gateway's `tenant_id` claim is necessary but not sufficient — resource authorization MUST enforce `resource.TenantId == actor.Attributes["tenant_id"]` as a second gate. |
| Composition root (`services.AddTrellisInternalJwtActorProvider(...)` / `services.AddReverseProxy().AddTrellisActorForwarding(...)`) | `trellis-api-internal-jwt.md`, `trellis-api-yarp.md`; upstream `trellis-api-servicedefaults.md` for `TrellisServiceBuilder` and the `AddTrellis(...)` extension | The internal-JWT actor provider registers directly via `IServiceCollection`; the YARP forwarding pipeline hooks `IReverseProxyBuilder`. Neither has a `TrellisServiceBuilder.Use*` slot in upstream — there's no slot for the YARP gateway, and the previous `UseTrellisInternalJwtActor` slot was removed in v3 when the implementation moved here. |
| Audit-log redaction / SIEM correlation | `trellis-api-yarp.md` (audit-log redaction contract section) | Every mint emits one `[LoggerMessage]` event with `kid`/`jti`/`iss`/`aud`/`exp` and the **projected** `trellis_permissions_count` / `trellis_forbidden_permissions_count` (post-`ProjectActor`, NOT source-actor counts) only — never raw JWT, never claim values, never actor IDs. |

**Measurable completion check for generated code:** every API call should be traceable to a loaded package reference; every registration helper should match the documented `Use*` / `Add*` pair; every recipe followed should produce a working request flow from inbound external JWT → gateway mint → downstream actor hydration → resource authorization.

Known non-APIs and corrected assumptions:

| Do not write | Correct source-backed statement |
|---|---|
| `o.Attributes["iss"] = ...` or `ProjectAttributes` returning `iss` (gateway side) | `iss` is a reserved JWT claim name — minter throws `InvalidOperationException` at mint time if any actor attribute key (after projection) collides (ordinal-ignore-case) with `iss`/`aud`/`exp`/`nbf`/`iat`/`jti`/`sub` or with the EXACT Trellis structural names `permissions`/`forbidden_permissions`/`trellis_actor_contract_version`/`trellis_permissions_count`/`trellis_forbidden_permissions_count`. Custom attribute keys outside this set are allowed (e.g. `trellis_request_id` is fine — only the listed five structural names collide). Use `external_iss` or another non-registered key for issuer namespacing. |
| `o.AttributeClaimMap["iss"] = "external_iss"` (consumer side) | Doesn't collide, but is also wrong: the consumer's `AttributeClaimMap` maps SOURCE JWT claim names → actor attribute keys. The minted JWT has no `iss`-as-attribute (only the standard `iss` registered claim); map the gateway-emitted `external_iss` claim into the actor attribute instead. |
| `o.SigningCredentials = new SigningCredentials(symmetricKey, "HS256")` | v1 is asymmetric-only. Use `RsaSecurityKey` (RS256/384/512) or `ECDsaSecurityKey` (ES256/384/512). Validator rejects symmetric + HMAC at startup. |
| `o.SigningCredentials = new SigningCredentials(x509Key, "RS256")` | `X509SecurityKey` rejected at startup; unwrap via `cert.GetRSAPrivateKey()` first. |
| `o.SigningCredentials = new SigningCredentials(new JsonWebKey(...), "RS256")` | `JsonWebKey` rejected at startup (JWKS converter throws on JsonWebKey input). Use `RsaSecurityKey` or `ECDsaSecurityKey`. |
| `o.Lifetime = TimeSpan.FromHours(1)` | Lifetime capped to `[1m, 30m]` at startup. Cookbook recommends 5 minutes default. |
| `o.MapInboundClaims = true` (downstream side) | Mandatory `false` — the provider reads JWT claim names directly and case-sensitively; mapping breaks every attribute lookup. |
| `o.TokenValidationParameters.TryAllIssuerSigningKeys = true` (downstream side) | Mandatory `false` — default `true` lets an attacker bypass `kid`-pinned key resolution during rotation. |
| `o.ActorIdResolver = a => a.Id.Value` for multi-IdP gateways | Default produces collisions across IdPs. MUST namespace as `$"{externalIss}|{tenant}|{actor.Id.Value}"`. |

## Patterns Index

### Task → recipe lookup

| Task | Start here |
|---|---|
| Configure the strict `AddJwtBearer` validation profile for a downstream service consuming gateway-minted JWTs | [Recipe 1 — Strict AddJwtBearer profile for AddTrellisInternalJwtActorProvider](#recipe-1--strict-addjwtbearer-validation-profile-for-addtrellisinternaljwtactorprovider) |
| Stand up a YARP gateway end-to-end (mint, project per-cluster, publish OIDC + JWKS, rotation runbook, emergency revocation) | [Recipe 2 — Microservices behind YARP, end-to-end](#recipe-2--microservices-behind-yarp-end-to-end) |
| Enforce multi-tenant ABAC at the downstream service (defense in depth against gateway-claim spoof) | [Recipe 2 — Tenant isolation defense-in-depth section](#tenant-isolation-defense-in-depth--the-gateway-claim-is-not-sufficient) <!-- trellis-doc-lint: allow-broken-anchor; resolves once Recipe 2 body is inlined --> |
| Rotate signing keys without dropping in-flight requests | [Recipe 1 — Key-rotation runbook](#key-rotation-runbook-overlapping-jwks-window) <!-- trellis-doc-lint: allow-broken-anchor; resolves once Recipe 1 body is inlined --> |
| Recover from a signing-key compromise | [Recipe 2 — Emergency revocation procedure](#emergency-revocation-procedure) <!-- trellis-doc-lint: allow-broken-anchor; resolves once Recipe 2 body is inlined --> |
| Mint your own gateway implementation against the same contract (APIM / Envoy / custom) | [Internal JWT contract v1](trellis-api-yarp.md#internal-jwt-contract-v1) |

### Mistake-regression routing

| If the task involves... | Read first | Why |
|---|---|---|
| Storing the external IdP issuer in `actor.Attributes` for sub namespacing | [Recipe 2 — Multi-IdP namespacing](#multi-idp-namespacing-when-fronting-two-or-more-external-idps) <!-- trellis-doc-lint: allow-broken-anchor; resolves once Recipe 2 body is inlined --> | Use `external_iss` not `iss` — minter throws on reserved JWT claim names in attribute output. |
| Pairing `Trellis.Yarp` with downstream `Trellis.Microservices.AspNetCore` | Recipes 1 + 2 (both halves) | The contract claim names + sentinel/count claims must match exactly on both sides. |
| Cookies, sessions, or anything that needs `VaryByHeaders` other than `Authorization` | [trellis-api-internal-jwt.md](trellis-api-internal-jwt.md#use-this-file-when) `VaryByHeaders` section | Default `["Authorization"]` is correct for Bearer; non-Bearer schemes MUST override. |

---

## Recipe 1 — Strict `AddJwtBearer` validation profile for `AddTrellisInternalJwtActorProvider`

**Problem.** Recipe 7's Path B (Trellis internal JWT) selects the `TrellisInternalJwtActorProvider` (registered via `services.AddTrellisInternalJwtActorProvider(...)`) to hydrate the `Actor` from a gateway-minted internal JWT, but `Trellis.Asp` deliberately does **not** take a hard `Microsoft.AspNetCore.Authentication.JwtBearer` dependency — the actor provider's job is claim-shape contract enforcement, not transport-level token validation. That makes JWT validation the consumer's responsibility, and most production failures of the internal-JWT pattern come from a too-loose `AddJwtBearer(...)` profile (default 5-minute `ClockSkew`, no `ValidAlgorithms` pin, no `RequireSignedTokens`, no signing-key resolver pinned to the gateway), not from the actor provider itself.

**Fix.** Two strict validation profiles paired with `AddTrellisInternalJwtActorProvider` and a few defense-in-depth checks that the gateway claim alone cannot guarantee.

### Profile A — JWKS-discovery (default; gateway exposes a JWKS endpoint)

Use when the gateway can serve a JWKS document over HTTPS that downstream services can fetch and cache. Suitable for in-cluster gateways such as `Trellis.Yarp` or any reverse proxy that signs tokens with a key whose public material is published at a well-known endpoint.

```csharp
// Microservice composition — strict JWKS-discovery profile.
builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.Authority = "https://gateway.internal";       // OIDC discovery = {Authority}/.well-known/openid-configuration; JWKS lives at the discovery doc's jwks_uri
    o.Audience = "incidents-service";               // pin per-service audience
    o.RequireHttpsMetadata = true;                  // never accept JWKS over plaintext, even on-prem
    o.MapInboundClaims = false;                     // keep raw JWT claim names (e.g. "tid"/"amr"), not the Microsoft long-URI forms
    o.SaveToken = false;                            // do not retain the raw JWT in AuthenticationProperties (redaction default)
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "https://gateway.internal",
        ValidateAudience = true, ValidAudience = "incidents-service",
        ValidateLifetime = true, RequireExpirationTime = true,
        ValidateIssuerSigningKey = true, RequireSignedTokens = true,
        ValidAlgorithms = ["RS256"],                // pin asymmetric — reject "alg":"none", HS256 key-confusion
        ClockSkew = TimeSpan.FromSeconds(30),       // tighten from the 5-minute default
        TryAllIssuerSigningKeys = false,            // see "Disable try-all signing-key fallback" below — applies to both profiles
    };
});

builder.Services.AddTrellisInternalJwtActorProvider(c =>
{
    c.RequiredAttributes = ["tenant_id"];        // fail closed on missing tenant
    c.AttributeClaimMap["tenant_id"] = "tid";
    c.AttributeClaimMap["mfa"] = "amr_normalized";
    c.ExpectedIssuer = "https://gateway.internal"; // defense-in-depth runtime check
    c.ExpectedAudience = "incidents-service";
});

builder.Services.AddTrellis(o => o
    .UseResourceAuthorization()
    .UseResourceAuthorization<UpdateIncidentCommand, Incident, Result<Unit>>());
```

`MapInboundClaims = false` is essential: with the default `true`, `Microsoft.IdentityModel.JsonWebTokens` rewrites a fixed set of standard JWT claim names through `JsonWebTokenHandler.DefaultInboundClaimTypeMap` — most consequentially for this recipe, `tid` → `http://schemas.microsoft.com/identity/claims/tenantid` and the role claims. Any of those used as an `AttributeClaimMap` value silently misses at runtime because `TrellisInternalJwtActorProvider` reads claim names directly and case-sensitively with no short↔long fallback for attributes. Gateway-controlled custom claims that aren't in the default map (e.g. `amr_normalized` above, `permissions`, `trellis_*`) pass through unchanged either way, but turning the map off is still the simplest correct posture — the only general property is "what `ActorIdClaim` reads has short↔long fallback, everything else does not", so a consumer who later reconfigures `ActorIdClaim` away from `sub` or maps a different standard claim still gets predictable behavior.

### Profile B — Air-gapped static key ring (no JWKS endpoint)

Use when network policy forbids the microservice from reaching the gateway's metadata endpoint (cross-segment, regulated workloads, air-gapped deployments). The signing key set is provisioned out-of-band via configuration/secrets and looked up by `kid` at validation time.

```csharp
// Microservice composition — air-gapped key-ring profile.
// Gateway's active + previous-generation public keys are loaded from configuration
// (Key Vault, sealed Secret, mounted JWK file, etc.) and resolved by 'kid'.
var keyRing = new Dictionary<string, SecurityKey>
{
    ["2026-Q2"] = new RsaSecurityKey(LoadPublicRsaKey("gateway-2026-Q2.pem")),
    ["2026-Q3"] = new RsaSecurityKey(LoadPublicRsaKey("gateway-2026-Q3.pem")),
};

builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.SaveToken = false;                            // do not retain the raw JWT (redaction default)
    // No Authority / MetadataAddress is set — there is no OIDC metadata endpoint to fetch,
    // so RequireHttpsMetadata (default true) has no effect. Leave it at the default rather
    // than flipping it off — that way a future revision that adds an Authority cannot
    // accidentally permit HTTP metadata.
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "https://gateway.internal",
        ValidateAudience = true, ValidAudience = "incidents-service",
        ValidateLifetime = true, RequireExpirationTime = true,
        ValidateIssuerSigningKey = true, RequireSignedTokens = true,
        ValidAlgorithms = ["RS256"],
        ClockSkew = TimeSpan.FromSeconds(30),
        TryAllIssuerSigningKeys = false,            // honor the kid-pinned resolver below — never fall back to "try every key"
        IssuerSigningKeyResolver = (token, securityToken, kid, _) =>
            kid is not null && keyRing.TryGetValue(kid, out var key)
                ? [key]
                : Array.Empty<SecurityKey>(),       // unknown kid → no key → fail closed
    };
});
```

### Disable try-all signing-key fallback (BOTH profiles)

`TokenValidationParameters.TryAllIssuerSigningKeys` defaults to `true`. When kid resolution returns nothing (no `kid` header, an unmapped `kid`, or a resolver returning empty), IdentityModel iterates every still-trusted signing key from cached OIDC configuration (Profile A) or `IssuerSigningKeys` (Profile B) and attempts signature validation against each. That throws away the rotation-isolation property: after the gateway has retired `K_old` but before some microservices have refreshed their cache, an attacker who exfiltrated `K_old` before retirement can mint a token with no/wrong `kid`; the kid-pinned resolver returns nothing; the try-all fallback then succeeds against the still-cached `K_old`. Pin `TryAllIssuerSigningKeys = false` so that an unmapped `kid` fails closed immediately — the only valid signatures are those produced under the currently advertised `kid`. The gateway MUST always set `kid` on issued tokens; treat unknown / null `kid` as fail-closed.

In Profile B specifically, `TryAllIssuerSigningKeys = false` is also a config-drift guard: if some future revision of the validation parameters ever adds entries to `IssuerSigningKeys` (e.g. a copy-paste from another sample), the resolver's `kid` pinning still wins.

Air-gapped rotation SLA: rotation requires a redeploy (or a config-reload hook) on every microservice. Plan a 24–72h overlap window per the runbook below.

### Tightened `ClockSkew` rationale

The framework default for `JwtBearerOptions.TokenValidationParameters.ClockSkew` is 5 minutes. Combined with a typical 5-minute access-token lifetime, that yields a ~10-minute effective replay window per token. The internal-JWT scenario is wholly inside your trust boundary with NTP-synchronized hosts (sub-second skew on production fleets), so 30 seconds is plenty of headroom and shrinks the replay window by an order of magnitude. Don't go below 30 seconds without an NTP SLA — clock drift can spike during VM live-migration and cause spurious validation failures.

### Logging-redaction checklist

Trellis-side: `TrellisInternalJwtActorProvider` already redacts (only logs scheme, claim **type** names, counts, consumer-configured literals — never claim values, JWT body, actor IDs, or PII). The consumer side has equivalent obligations:

| Safe to log | Never log |
|---|---|
| Scheme name (e.g. `"Bearer"`) | The raw JWT (Authorization header value) |
| `kid` (key identifier) | Raw claim values (`sub`, `tid`, `email`, permission strings) |
| `iss` / `aud` (gateway-controlled literals) | The full `Actor.Id` |
| `JwtBearerEvents.OnAuthenticationFailed` exception **type** | The decoded JWT body (`token.Claims`) |
| `JwtBearerEvents.OnChallenge` failure code (`error`) | `Authorization` header on `HttpContext.Request` |
| Request `traceparent` / `Activity.Id` for correlation | `JwtBearerEvents` `Token`/`Exception` raw content beyond type |

Conditionally safe — only with controls in place:

- **Keyed HMAC of `sub` / `jti`** (NOT plain SHA-256). Plain hashes of low-cardinality `sub` values are reversible by dictionary / frequency analysis; use a server-side HMAC key rotated separately from the JWT signing key, and truncate to 8-16 bytes for log volume.
- **Permissions / forbidden / attribute *counts***. For small or known actor populations, exact counts fingerprint an actor (an actor with exactly 47 permissions narrows enumeration). Bucket (`0`, `1`, `2-5`, `6-20`, `21+`) or suppress entirely for sensitive populations.

Add a `JwtBearerEvents.OnAuthenticationFailed` handler that logs only the exception type and `kid` (if available), never the token or full exception message.

### Tenant-isolation defense-in-depth — the gateway claim is NOT sufficient

A `tenant_id` claim minted by the gateway tells the microservice *which tenant the actor is acting on behalf of*. It does NOT prove that the resource being accessed belongs to that tenant. A bug in the gateway, a stale cached actor envelope, or a downstream service trusted to a wider tenant scope could surface a tenant claim that disagrees with the resource. Resource authorization MUST enforce `resource.TenantId == actor.Attributes["tenant_id"]` as a second gate — failing closed even when the static permission check (`actor.HasPermission("incidents:read")`) succeeds.

```csharp
public sealed record UpdateIncidentCommand(IncidentId Id, IncidentPatch Patch)
    : ICommand<Result<Unit>>,
      IAuthorizeResource<Incident>,
      IIdentifyResource<Incident, IncidentId>
{
    public IncidentId GetResourceId() => Id;

    public IResult Authorize(Actor actor, Incident incident)
    {
        if (!actor.Attributes.TryGetValue("tenant_id", out var actorTenant))
            return Result.Fail(new Error.Forbidden("incidents.tenant-missing"));

        if (!string.Equals(incident.TenantId.Value, actorTenant, StringComparison.Ordinal))
            return Result.Fail(new Error.Forbidden("incidents.cross-tenant"));

        return Result.Ensure(
            incident.AssigneeId == actor.Id || actor.HasPermission("incidents:write-any"),
            new Error.Forbidden("incidents.write-denied"));
    }
}
```

Pair this with [Recipe 32 (upstream)](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) when cross-tenant probing is itself a leak — `Forbidden` from this check translates to `NotFound`, so an attacker enumerating incident IDs across tenants sees an indistinguishable 404. The `ExistenceHidden` log carries the original `incidents.cross-tenant` code so SecOps can still detect the probe pattern.

### Key-rotation runbook (overlapping JWKS window)

The internal JWT lifetime should be short (Trellis-recommended: 5 minutes). Rotation must cover any token already in flight, so the overlap window is `token_lifetime + ClockSkew + safety_margin`. With 5-minute lifetime + 30-second skew + 30-second safety = **~6 minutes minimum** to retire the previous key.

Profile A (JWKS-discovery):

1. **T0** — Gateway adds the new key `K_new` (kid `2026-Q3`) to its JWKS endpoint **alongside** `K_old` (kid `2026-Q2`). Gateway continues to sign tokens with `K_old`.
2. **T0 + `AutomaticRefreshInterval` + jitter window + warm-up probe** — All microservices have refreshed their cached OIDC configuration. `ConfigurationManager` refresh is lazy (driven by the next inbound request after the interval elapses) and the next refresh is scheduled at `AutomaticRefreshInterval + random(AutomaticRefreshInterval/20)`, so a 30-second `safety_margin` is NOT enough on its own — fleet-wide convergence can lag by `AutomaticRefreshInterval + 5%` plus your slowest service's request idle gap. Operational gate: probe every microservice with a request that ACTUALLY exercises JWT validation — an authenticated endpoint (NOT an anonymous `/health` route, which returns 200 regardless of token state) such as a dedicated internal `/_internal/auth-probe` requiring a Bearer token signed by `K_new`. Require an `AuthenticateAsync("Bearer")` success (HTTP 200) from EVERY instance before proceeding. `JwtBearerOptions.RefreshOnIssuerKeyNotFound = true` (default) requests a forced refresh on the first request that hits an unknown `kid` — useful as a backstop, but it only fires AFTER a `SecurityTokenSignatureKeyNotFoundException`, so a botched rotation will produce a brief spike of rejected requests until the forced refresh completes (`ConfigurationManager.RefreshInterval`, default 5min, throttles forced refreshes).
3. **Probe-confirmed convergence** — Gateway flips signer to `K_new`. Newly minted tokens carry `kid: "2026-Q3"`.
4. **Signer-flip + token_lifetime + ClockSkew + safety_margin** — No in-flight token is signed with `K_old`. Gateway removes `K_old` from JWKS. Microservices stop accepting it on the next cache refresh.

Profile B (air-gapped static key ring):

1. **T0** — Push a config update to every microservice that adds `K_new` to the key ring **alongside** `K_old`. Restart / hot-reload as your infra requires. The microservice now trusts both keys.
2. **T0 + slowest-fleet-rollout** — All microservices have the dual-key ring. Gateway flips signer.
3. **Signer-flip + token_lifetime + ClockSkew + safety_margin** — Push a config update removing `K_old`. Restart / hot-reload.

The fail-loud signal during a botched rotation is `SecurityTokenSignatureKeyNotFoundException` on the microservice side (this is the type `RefreshOnIssuerKeyNotFound` reacts to) — surface it on a high-priority alert (it correlates 1:1 with "tokens being rejected"). Do NOT silently retry; the runbook step is *roll forward the gateway* or *roll back the signing switch*, not *expand the key acceptance window*.

### What it shows

- `Trellis.Asp` decouples claim-shape contract enforcement (`TrellisInternalJwtActorProvider`) from transport-level token validation (`Microsoft.AspNetCore.Authentication.JwtBearer`). Consumers wire both, and each is independently strict.
- The two profiles map to the two most common topologies (JWKS-reachable gateway vs air-gapped key ring) and share the same `TokenValidationParameters` core — only the key-source surface differs.
- `MapInboundClaims = false` is mandatory for any consumer of `TrellisInternalJwtActorProvider`; the provider reads JWT claim names directly and case-sensitively.
- Tenant isolation is the canonical example of "gateway claim ≠ resource authorization" — the resource authorization layer is the defense-in-depth second gate, never optional.

**Related recipes.** [Recipe 7 (upstream)](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth) for the three-path microservices framing (Path A pass-through, Path B internal JWT shown here, Path C OBO); [Recipe 24 (upstream)](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-24--indirect-multi-hop-resource-authorization) for owner-chain tenant enforcement; [Recipe 32 (upstream)](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) for cross-tenant probing defense; [Recipe 2](#recipe-2--microservices-behind-yarp-end-to-end) for the gateway-side bookend (`Trellis.Yarp` package — the matching mint of the same internal-JWT contract this recipe validates).

---

## Recipe 2 — Microservices behind YARP, end-to-end

**Problem.** Recipes 7 + 33 cover the downstream microservice side of Path B (Trellis internal JWT): how to validate a gateway-minted JWT and hydrate the full `Actor`. The other half of that contract is the gateway — the component that authenticates the external user, resolves their full `Actor` (id + permissions + forbidden permissions + ABAC attributes), and re-mints a per-cluster internal JWT that downstream services can validate without ever touching the external IDP. v1 ships that gateway as a YARP transform in the `Trellis.Yarp` package.

**Fix.** Three pieces: register the actor-forwarding transform on YARP's `IReverseProxyBuilder`, publish OIDC discovery + JWKS so downstream services can fetch the signing keys, and follow the operational guardrails for key rotation + redaction.

### Gateway composition

```csharp
// Program.cs — YARP gateway with Trellis actor forwarding.
var builder = WebApplication.CreateBuilder(args);

// Authenticate the inbound (external) JWT — typically against the public IDP.
// This is what gets hydrated into the Actor that's re-minted for downstream services.
builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.Authority = "https://your-idp.example";
    o.Audience = "trellis-gateway";
    o.MapInboundClaims = false;
});

// IActorProvider hydrates Actor from the inbound JWT's claims. AddTrellisActorForwarding
// REQUIRES one of these to be registered — startup validation fails fast if missing.
// Multi-tenant Entra example: AddEntraActorProvider populates actor.Attributes["tid"]
// automatically. For multi-IdP fronts, ship a custom IActorProvider that ALSO populates
// actor.Attributes["iss"] from the inbound JWT — see the "Multi-IdP namespacing" note below.
builder.Services.AddTrellis(o => o
    .UseEntraActorProvider());

// Load YARP routes/clusters from configuration and attach the Trellis actor-forwarding
// transform. The transform mints a fresh per-cluster JWT on every request and overwrites
// the upstream Authorization header before forwarding.
var rsa = LoadRsaPrivateKey("gateway-2026-Q3.pem");
var signingKey = new RsaSecurityKey(rsa) { KeyId = "2026-Q3" };

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTrellisActorForwarding(o =>
    {
        o.Issuer = "https://gateway.internal";
        o.PublicBaseUrl = new Uri("https://gateway.internal");
        o.SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
        // Rotation overlap — keep the previous key in JWKS until token_lifetime + skew expires.
        o.PreviousSigningKeys = [LoadRsaPublicKey("gateway-2026-Q2.pem", kid: "2026-Q2")];

        // Per-cluster audience (defaults to ClusterConfig.ClusterId; explicit is clearer).
        o.AudiencePerCluster = cluster => cluster.ClusterId switch
        {
            "incidents" => "incidents-service",
            "orders" => "orders-service",
            _ => cluster.ClusterId,
        };

        // Permission projection — forward only the permissions in the destination cluster's
        // namespace. Convention: <cluster-id>.<verb>.
        o.ProjectPermissionsFor = (cluster, perms) =>
            perms.Where(p => p.StartsWith(cluster.ClusterId + ".", StringComparison.Ordinal))
                 .ToHashSet(StringComparer.Ordinal);

        // Attribute projection — actor.Attributes keys become JWT claim names verbatim
        // (default pass-through). EntraActorProvider populates actor.Attributes["tid"] from
        // the inbound JWT's 'tid' claim, so the gateway emits a JWT 'tid' claim that the
        // downstream maps to its own actor attribute via AttributeClaimMap (see below).
        o.ProjectAttributes = (_, attrs) => attrs;

        // ActorIdResolver — namespace 'sub' so a downstream attacker cannot replay a token
        // by guessing actor IDs across tenants. EntraActorProvider stores the externally-
        // validated tenant in actor.Attributes["tid"]; combining tenant + sub is sufficient
        // for the SINGLE-IdP-multi-tenant case. Fronting MULTIPLE IdPs requires a custom
        // IActorProvider that also populates actor.Attributes["iss"] (see note below).
        o.ActorIdResolver = actor =>
        {
            var tenant = actor.Attributes.GetValueOrDefault("tid", "unknown-tid");
            return $"{tenant}|{actor.Id.Value}";
        };
    });

var app = builder.Build();
app.MapReverseProxy();

// Publish OIDC discovery + JWKS so downstream services using
// `AddJwtBearer(o => o.Authority = "https://gateway.internal")` can auto-fetch keys.
app.MapTrellisDiscoveryEndpoint();   // defaults: /.well-known/openid-configuration + /.well-known/jwks.json

app.Run();
```

The transform on every request: resolves the inbound `Actor` from the registered `IActorProvider`, applies the per-cluster projections, mints a fresh JWT (`iss`, `aud`, `sub`, `jti`, `iat`/`nbf`/`exp`, multi-valued `permissions` / `forbidden_permissions`, the three sentinel claims `trellis_actor_contract_version=1` + counts, and a claim per projected attribute), and overwrites the upstream `Authorization` header before the request hits the destination cluster. **No actor on the inbound request → the upstream `Authorization` header is CLEARED** before forwarding so the external bearer token cannot leak to the downstream service (fail-closed posture, security-amended P4 round 1); downstream policy decides whether anonymous is allowed.

#### Multi-IdP namespacing (when fronting two or more external IDPs)

`Actor` equality is identity-based on `Id` only, so two IdPs that both issue `sub = "12345"` collide. The single-tenant `actor.Attributes["tid"]` namespacing above is sufficient when the gateway fronts ONE IDP. For multi-IDP fronts, ship a minimal custom `IActorProvider` that populates `actor.Attributes["external_iss"]` from the inbound JWT's `iss` claim (NOT under the attribute key `"iss"` — that name collides with the gateway's structural JWT `iss` claim AND with the consumer-side reserved-claim guard, and the minter will throw if `ProjectAttributes` returns a reserved key). Then expand the resolver to `$"{external_iss}|{tid}|{actor.Id.Value}"`:

```csharp
public sealed class MultiIdpClaimsActorProvider(IHttpContextAccessor accessor) : IActorProvider
{
    public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return Task.FromResult(Maybe<Actor>.None);

        var sub = user.FindFirst("sub")?.Value;
        if (sub is null) return Task.FromResult(Maybe<Actor>.None);

        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        // Store the EXTERNAL issuer under a non-registered attribute key. Using "iss" here
        // would collide with the gateway-minted structural JWT iss claim — the minter would
        // throw InvalidOperationException because ProjectAttributes returned a reserved name.
        if (user.FindFirst("iss")?.Value is { } iss) attrs["external_iss"] = iss;
        if (user.FindFirst("tid")?.Value is { } tid) attrs["tid"] = tid;

        var permissions = user.FindAll("permissions").Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        // Actor's constructor takes IReadOnlySet<string> for forbiddenPermissions; the
        // collection-expression `[]` does not satisfy that target type (the C# 12 collection
        // expression doesn't synthesize IReadOnlySet). Use FrozenSet<string>.Empty for the
        // unused parameter so the snippet compiles when copied.
        return Task.FromResult(Maybe<Actor>.From(new Actor(sub, permissions, FrozenSet<string>.Empty, attrs)));
    }
}
```

Pair the provider with an updated `ActorIdResolver` that reads `external_iss`:

```csharp
o.ActorIdResolver = actor =>
{
    var externalIss = actor.Attributes.GetValueOrDefault("external_iss", "unknown-iss");
    var tenant = actor.Attributes.GetValueOrDefault("tid", "unknown-tid");
    return $"{externalIss}|{tenant}|{actor.Id.Value}";
};
```

### Pair with Recipe 1 on the downstream side

The microservice consumes the gateway-minted JWT via `AddTrellisInternalJwtActorProvider` + the strict `AddJwtBearer` profile from [Recipe 1](#recipe-1--strict-addjwtbearer-validation-profile-for-addtrellisinternaljwtactorprovider). The `Authority` it sets points at the gateway, and OIDC discovery handles JWKS fetch:

```csharp
// Downstream microservice — pairs with the gateway above.
builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
{
    o.Authority = "https://gateway.internal";    // matches the gateway's PublicBaseUrl
    o.Audience = "incidents-service";            // matches AudiencePerCluster for "incidents"
    o.MapInboundClaims = false;
    o.SaveToken = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "https://gateway.internal",
        ValidateAudience = true, ValidAudience = "incidents-service",
        ValidateLifetime = true, RequireExpirationTime = true,
        ValidateIssuerSigningKey = true, RequireSignedTokens = true,
        ValidAlgorithms = ["RS256"],
        ClockSkew = TimeSpan.FromSeconds(30),
        TryAllIssuerSigningKeys = false,
    };
});

builder.Services.AddTrellisInternalJwtActorProvider(c =>
{
    c.ExpectedIssuer = "https://gateway.internal";
    c.ExpectedAudience = "incidents-service";
    c.RequiredAttributes = ["tenant_id"];
    // The gateway uses EntraActorProvider, which populates actor.Attributes["tid"].
    // Default ProjectAttributes pass-through emits a JWT claim 'tid' (not 'tenant_id'),
    // so the downstream maps 'tenant_id' (the actor attribute name we want to enforce
    // via RequiredAttributes) ← 'tid' (the JWT claim name we receive).
    c.AttributeClaimMap["tenant_id"] = "tid";
});
```

### Trust boundary — signing-key compromise is full identity spoof

`Trellis.Yarp` is a trusted subsystem: the gateway owns identity + route policy + topology projection; downstream microservices trust the gateway's signature and treat the minted `Actor` as authoritative. The corollary is sharp: **if an attacker exfiltrates the gateway's private signing key, they can mint a token impersonating any user with any permission set, and every downstream service will accept it as authentic** — until the compromised `kid` is dropped from JWKS and every downstream's cached config refreshes.

Mitigations built into the package:

- **Short token lifetimes.** Default 5 minutes; capped to `[1m, 30m]` at startup validation. Even a successful key exfiltration grants the attacker only a short window of unbounded spoof per token (the attacker can re-mint indefinitely, but each token expires quickly).
- **`kid`-aware overlapping JWKS rotation.** `PreviousSigningKeys` keeps the outgoing key in JWKS for the rotation overlap window so in-flight tokens validated downstream don't get rejected as the gateway switches signers. The runbook (Recipe 33) gates the signer flip on a warm-up probe.
- **Audit-log redaction by construction.** Every mint emits a `[LoggerMessage]` event carrying `kid`, `iss`, `aud`, `jti`, expiration bucket, permission count, forbidden count. NEVER the JWT body, raw claim values, actor IDs, tenant IDs, or PII. Redaction tests assert no claim-value strings appear in the audit-log output.

### Emergency revocation procedure

When you suspect a signing-key compromise:

1. **Drop the compromised `kid` from JWKS** — remove it from both `SigningCredentials` AND `PreviousSigningKeys`. Redeploy the gateway. The compromised key is no longer published.
2. **Force a JWKS refresh on every downstream service.** Restart them, or wait for `ConfigurationManager.AutomaticRefreshInterval` to elapse, OR send a malformed-`kid` token to trigger `RefreshOnIssuerKeyNotFound`.
3. **Rotate to a fresh `kid`** — generate a new asymmetric key pair, set it as the new `SigningCredentials`, deploy. Pre-cache the new key in `PreviousSigningKeys` for at least one rotation cycle so the rollback is bounded.
4. **Audit the gateway log** — the `jti` in every minted token correlates 1:1 with a mint event. Cross-reference against your downstream services' authentication logs to identify which requests were potentially affected.

The key takeaway: design assumes the signing key is secret, but the operational recovery procedure assumes it isn't (a stolen key cannot be unstolen). Short lifetimes + fast rotation + auditable mint correlation are the defense-in-depth posture.

### mTLS environment — JWT is belt-and-suspenders

When the gateway-to-microservice channel is already mTLS-authenticated or Managed-Identity-bound, the minted JWT is redundant for channel authentication (~25 µs of crypto per request) — but it carries application-layer identity (permissions, attributes, tenant) that mTLS does not. v1 deliberately does NOT skip the JWT in mTLS environments because the cost is negligible relative to typical workload I/O and the claim contract is the unification point. A v1.1 mTLS forwarded-headers path is on the roadmap; until then the v1 acceptance is: **JWT is belt-and-suspenders by design when mTLS already trusts the channel.**

### What it shows

- The gateway-side bookend of Path B (Trellis internal JWT). Where Recipe 33 covered the downstream's strict `AddJwtBearer` validation profile, this recipe shows where the JWT being validated comes from.
- The two-package contract — `Trellis.Yarp` mints, `Trellis.Microservices.AspNetCore`'s `TrellisInternalJwtActorProvider` hydrates — is unified through the sentinel + count claims (`trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count`) and the strict claim-shape contract.
- Operational guardrails: short lifetimes, kid-aware rotation overlap, audit-log redaction, emergency revocation procedure. Signing-key compromise is the worst-case scenario the framework explicitly designs around.

**Related recipes.** [Recipe 1](#recipe-1--strict-addjwtbearer-validation-profile-for-addtrellisinternaljwtactorprovider) for the downstream side of the same contract; [Recipe 7 (upstream)](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth) for the three-path microservices framing (this recipe + Recipe 33 together cover Path B end-to-end); [Recipe 32 (upstream)](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) for downstream cross-tenant probing defense.

---

---

## Cross-references

- [trellis-api-yarp.md](trellis-api-yarp.md#use-this-file-when) — gateway-side mint surface; internal JWT contract v1 specification
- [trellis-api-internal-jwt.md](trellis-api-internal-jwt.md#use-this-file-when) — consumer-side hydration surface; option-by-option reference for `TrellisInternalJwtActorOptions`
- [trellis-api-microservices-abstractions.md](trellis-api-microservices-abstractions.md#use-this-file-when) — shared contract constants (`TrellisInternalJwtClaimNames`, contract version)
- [Upstream `trellis-api-authorization.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-authorization.md) — `Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<>` (foundational types both sides build on)
- [Upstream Recipe 7](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth) — 3-path microservices framing (this repo implements Path B)
- [Upstream Recipe 32](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) — `AuthFailureExposurePolicy.HideAsNotFound` (pairs naturally with tenant isolation)
- [Companion template repo](https://github.com/xavierjohn/Trellis.Microservices.Template) — working Project Tracker starter