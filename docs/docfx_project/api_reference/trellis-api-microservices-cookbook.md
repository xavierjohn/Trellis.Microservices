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
  - [trellis-api-internal-jwt.md](trellis-api-internal-jwt.md#use-this-file-when) — `TrellisInternalJwtActorProvider`, `TrellisInternalJwtActorOptions`, `UseTrellisInternalJwtActor`
  - [trellis-api-microservices-abstractions.md](trellis-api-microservices-abstractions.md#use-this-file-when) — `TrellisInternalJwtClaimNames`, contract version constants

- **Required upstream docs (load from xavierjohn/Trellis):**
  - [`trellis-api-authorization.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-authorization.md) — `Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<>` (every minted JWT hydrates back into an `Actor`)
  - [`trellis-api-asp.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-asp.md) — `ClaimsActorProvider`, `EntraActorProvider` (the gateway-side actor sources that feed `AddTrellisActorForwarding`)
  - [`trellis-api-servicedefaults.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-servicedefaults.md) — `AddTrellis`, `TrellisServiceBuilder` (the composition root `UseTrellisInternalJwtActor` extends)
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
| Gateway-side signing key rotation | `trellis-api-yarp.md` AND this cookbook's key-rotation recipe; upstream `trellis-api-cookbook.md` Recipe 33's rotation runbook for the consumer-side timing | The runbook spans both sides; both halves must stay in lock-step. |
| Consumer-side actor hydration from minted JWT | `trellis-api-internal-jwt.md`, `trellis-api-microservices-abstractions.md`; upstream `trellis-api-authorization.md` for `Actor`/`IActorProvider`, upstream `trellis-api-asp.md` for `AddJwtBearer` integration | `TrellisInternalJwtActorProvider` produces the same `Actor` shape the gateway minted from; the contract claim names live in Abstractions. |
| Consumer-side strict `AddJwtBearer` profile | This cookbook's Recipe 1 (strict profile); `trellis-api-internal-jwt.md` for the consumer-side options | The strict profile is the mandatory companion to `UseTrellisInternalJwtActor`. |
| Tenant-isolation ABAC enforcement (downstream resource auth defending against gateway-claim spoof) | This cookbook's Recipe 2 (end-to-end); upstream `trellis-api-authorization.md` for `IAuthorizeResource<>` | The gateway's `tenant_id` claim is necessary but not sufficient — resource authorization MUST enforce `resource.TenantId == actor.Attributes["tenant_id"]` as a second gate. |
| Composition root (`AddTrellis(o => o.UseTrellisInternalJwtActor(...))` / `services.AddReverseProxy().AddTrellisActorForwarding(...)`) | `trellis-api-internal-jwt.md`, `trellis-api-yarp.md`; upstream `trellis-api-servicedefaults.md` for `TrellisServiceBuilder` | Both registration extensions hook through to upstream `Trellis.ServiceDefaults` patterns. |
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
| Configure the strict `AddJwtBearer` validation profile for a downstream service consuming gateway-minted JWTs | [Recipe 1 — Strict AddJwtBearer profile for UseTrellisInternalJwtActor](#recipe-1--strict-addjwtbearer-validation-profile-for-usetrellisinternaljwtactor) |
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

## Recipe 1 — Strict `AddJwtBearer` validation profile for `UseTrellisInternalJwtActor`

**[Body carried over verbatim from the main repo's Recipe 33 in `xavierjohn/Trellis` — see `trellis-api-cookbook.md` PR #583. Includes: Profile A (JWKS-discovery), Profile B (air-gapped static key ring), `TryAllIssuerSigningKeys = false` (BOTH profiles), `ClockSkew` rationale, logging-redaction checklist, key-rotation runbook (overlapping JWKS window). Subheadings preserved so the existing anchor links from the main repo's docs continue to resolve via redirect.]**

---

## Recipe 2 — Microservices behind YARP, end-to-end

**[Body carried over verbatim from the main repo's Recipe 34 in `xavierjohn/Trellis` — see `trellis-api-cookbook.md` PR #584. Includes: gateway composition example, downstream pairing reference, trust-boundary documentation, key-rotation runbook reference, emergency revocation procedure, mTLS-environment note ("JWT is belt-and-suspenders by design when mTLS already trusts the channel"), multi-IdP namespacing subsection (uses `external_iss` not `iss`), MultiIdpClaimsActorProvider sample with `FrozenSet<string>.Empty` for forbiddenPermissions, AttributeClaimMap['tenant_id'] = 'tid' downstream mapping.]**

---

## Cross-references

- [trellis-api-yarp.md](trellis-api-yarp.md#use-this-file-when) — gateway-side mint surface; internal JWT contract v1 specification
- [trellis-api-internal-jwt.md](trellis-api-internal-jwt.md#use-this-file-when) — consumer-side hydration surface; option-by-option reference for `TrellisInternalJwtActorOptions`
- [trellis-api-microservices-abstractions.md](trellis-api-microservices-abstractions.md#use-this-file-when) — shared contract constants (`TrellisInternalJwtClaimNames`, contract version)
- [Upstream `trellis-api-authorization.md`](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-authorization.md) — `Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<>` (foundational types both sides build on)
- [Upstream Recipe 7](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth) — 3-path microservices framing (this repo implements Path B)
- [Upstream Recipe 32](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) — `AuthFailureExposurePolicy.HideAsNotFound` (pairs naturally with tenant isolation)
- [Companion template repo](https://github.com/xavierjohn/Trellis.Microservices.Template) — working Project Tracker starter
