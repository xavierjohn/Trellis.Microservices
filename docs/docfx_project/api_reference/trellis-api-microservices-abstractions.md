---
package: Trellis.Microservices.Abstractions
namespaces: [Trellis.Microservices.Abstractions]
types: [TrellisInternalJwtClaimNames]
version: v1
last_verified: 2026-06-05
audience: [llm]
---
# Trellis.Microservices.Abstractions — API Reference

**Package:** `Trellis.Microservices.Abstractions` (AOT-compatible, no runtime dependencies; ships only `const string` literals).
**Namespaces:** `Trellis.Microservices.Abstractions`
**Purpose:** Single source of truth for the canonical claim-name literals and contract-version constants that the gateway-side minter (`Trellis.Yarp.TrellisActorJwtMinter`) and the consumer-side provider (`Trellis.Microservices.AspNetCore.TrellisInternalJwtActorProvider`) must agree on bit-for-bit.

## Use this file when

- You are implementing a third-party gateway against the Trellis internal JWT contract (anything other than `Trellis.Yarp`).
- You are implementing a custom consumer-side actor provider (anything other than `TrellisInternalJwtActorProvider`).
- You are writing an integration test that hand-crafts JWTs and need to assert the exact claim names that both sides emit / consume.
- You are reviewing a PR that touches either gateway or consumer claim handling — confirm the change references these constants, not freshly-typed string literals.

If you are using `Trellis.Yarp` AND `Trellis.Microservices.AspNetCore` (the standard pairing), you do NOT need to reference this package directly. Both packages reference it transitively and you can rely on the defaults in `TrellisActorForwardingOptions` / `TrellisInternalJwtActorOptions`.

## Why this package exists

Until this package shipped, the canonical claim names lived as `internal const` literals inside `Trellis.Yarp.TrellisInternalJwtClaimNames`, with the consumer side hard-coding the same strings as defaults in `TrellisInternalJwtActorOptions`. Both sides agreed by **convention** — the only enforcement was a code review and a contract test that loaded both projects and asserted equality. The risk was real: a typo or future-contract-version change to one side without the other would create a silent fail-open / fail-closed divergence (one side accepts a token, the other rejects, depending on the direction of the typo).

This package promotes those constants to `public`, gives them a stable namespace, and lets BOTH sides reference the same literals. Third-party gateway and consumer implementations now have a versioned NuGet contract to compile against.

---

## `TrellisInternalJwtClaimNames`

**Declaration**

```csharp
public static class TrellisInternalJwtClaimNames
```

Canonical JWT claim-name literals for the Trellis internal-network JWT contract v1. All values are `public const string` so they may be used in attribute arguments, switch expressions, and pattern matches.

| Member | Value | Claim role |
| --- | --- | --- |
| `Subject` | `"sub"` | RFC 7519 registered claim. Carries the namespaced actor identifier produced by `TrellisActorForwardingOptions.ActorIdResolver`. Defense-in-depth for multi-IdP gateways — see `Trellis.Yarp` reference. |
| `JwtId` | `"jti"` | RFC 7519 registered claim. Fresh per token (cryptographically-random GUID-N). Audit-correlation key — every minted JWT correlates to a single mint event without leaking actor identity. |
| `Permissions` | `"permissions"` | Per-actor authorization-grant claim. Emitted multi-valued (one JSON-array entry per permission). NEVER comma-joined or JSON-stringified, per the strict-shape contract enforced by `TrellisInternalJwtActorOptions.StrictClaimShape`. |
| `ForbiddenPermissions` | `"forbidden_permissions"` | Per-actor deny-set claim. Emitted multi-valued. **The deny-overrides-allow contract invariant requires the matching `ForbiddenPermissionsCount` claim to ALWAYS be emitted (even when the set is empty)** so the consumer can distinguish "evaluated to empty" from "stripped by a misbehaving proxy." |
| `ContractVersion` | `"trellis_actor_contract_version"` | Sentinel claim asserting which version of the internal-JWT contract this token conforms to. v1 emits the literal `"1"` (see `CurrentContractVersion`). |
| `PermissionsCount` | `"trellis_permissions_count"` | Decimal-string count of `Permissions` claims emitted in the same token. Always emitted (including `"0"` for empty sets) so the consumer can fail closed when a proxy strips the multi-valued permission claims. |
| `ForbiddenPermissionsCount` | `"trellis_forbidden_permissions_count"` | Decimal-string count of `ForbiddenPermissions` claims emitted in the same token. Always emitted (including `"0"` for empty sets) so the consumer can detect the privilege-escalation footgun where a malicious proxy strips the deny set silently. |
| `CurrentContractVersion` | `"1"` | The `ContractVersion` claim value emitted by v1. Bump together with a coordinated `Trellis.Yarp` + `Trellis.Microservices.AspNetCore` major release when (if) the contract evolves. |

## Contract integrity rules

These rules are enforced jointly by the gateway and consumer. A third-party implementation that omits any of them is **not** contract-conformant.

1. **Always emit the sentinel.** Every minted token MUST carry `ContractVersion = CurrentContractVersion`. The consumer fails closed (`Maybe<Actor>.None`) on missing or duplicated sentinel.
2. **Always emit both counts.** `PermissionsCount` and `ForbiddenPermissionsCount` MUST be emitted as decimal-string non-negative integers, including `"0"`. The consumer parses with `NumberStyles.None`, `CultureInfo.InvariantCulture`. Whitespace, sign characters, hex prefixes, or non-ASCII digits cause `Maybe<Actor>.None`.
3. **Always emit `JwtId`.** Fresh per token, used for audit-log correlation. Reusing a `jti` across tokens defeats the correlation-without-leaking-identity guarantee.
4. **Permissions and ForbiddenPermissions are multi-valued, never joined.** The consumer's `StrictClaimShape = true` (default) rejects values containing `,` or starting with `[` / `{`. A JSON-array-of-strings serialization would put the array between `[ ]` and fail this guard.
5. **Counts must equal observed multi-valued occurrences.** The consumer compares `PermissionsCount` to the number of `Permissions` claims it observed (and likewise for the forbidden set). A mismatch — even off by one — yields `Maybe<Actor>.None`.

The sentinel + count combination is the contract integrity invariant. A misbehaving proxy that strips one or more multi-valued `ForbiddenPermissions` claims would leave `ForbiddenPermissionsCount` higher than the observed count, and the consumer fails closed. A proxy that strips the entire deny set (without rewriting the count) leaves count `> 0` and observed `== 0` — fails closed. A proxy that strips both the count AND the deny entries is detected by the sentinel + the missing count claim (`PermissionsCount` and `ForbiddenPermissionsCount` are BOTH required).

## Version compatibility

This package is **versioned independently** from the gateway and consumer packages. Within a single contract version (`CurrentContractVersion = "1"`), the literals are immutable — they will not change in any v1.x release. A future v2 will:

- Ship a new major version of `Trellis.Microservices.Abstractions` with `CurrentContractVersion = "2"` (and potentially renamed / added claim members for forward extension).
- Ship matching major versions of `Trellis.Yarp` and `Trellis.Microservices.AspNetCore` that depend on the new abstractions major.
- Provide a migration runbook for operators standing up a heterogeneous-version fleet during rollout.

Until then, a third-party gateway / consumer can rely on every literal in this file remaining bit-for-bit stable.

## Cross-references

- [`trellis-api-yarp.md`](trellis-api-yarp.md#use-this-file-when) — `Trellis.Yarp.TrellisActorJwtMinter` and the [Internal JWT contract v1 table](trellis-api-yarp.md#internal-jwt-contract-v1) that lists every emitted claim along with its source-of-truth literal from this package.
- [`trellis-api-internal-jwt.md`](trellis-api-internal-jwt.md#use-this-file-when) — `TrellisInternalJwtActorProvider` and the consumer-side `TrellisInternalJwtActorOptions` defaults (each default literal cross-references the matching member here).
- [Recipe 2 — Microservices behind YARP, end-to-end](trellis-api-microservices-cookbook.md#recipe-2--microservices-behind-yarp-end-to-end) — worked example showing both sides referencing the same constants.
