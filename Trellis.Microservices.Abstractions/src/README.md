# Trellis.Microservices.Abstractions

Shared contract constants for the [Trellis](https://github.com/xavierjohn/Trellis) internal-network JWT v1.

This package ships **one public static class** — `TrellisInternalJwtClaimNames` — that pairs the gateway-side minter ([`Trellis.Yarp`](../../docs/docfx_project/api_reference/trellis-api-yarp.md#use-this-file-when)) with the consumer-side actor provider ([`Trellis.Microservices.AspNetCore`](../../docs/docfx_project/api_reference/trellis-api-internal-jwt.md#use-this-file-when)). Both sides reference these literals so a future contract version bump is one coordinated change.

## Why it exists

Without this package, the canonical claim names lived as `internal const` literals inside `Trellis.Yarp`, with the consumer side hard-coding the same strings as defaults in `TrellisInternalJwtActorOptions`. Both sides agreed by **convention** — the only enforcement was code review and a contract test that loaded both projects and asserted equality. The risk was real: a typo or future-contract-version change to one side without the other would create a silent fail-open / fail-closed divergence (one side accepts a token, the other rejects, depending on the direction of the typo).

This package promotes those constants to `public`, gives them a stable namespace, and lets BOTH sides reference the same literals. Third-party gateway and consumer implementations now have a versioned NuGet contract to compile against.

## Properties

- AOT-compatible — ships only `public const string` literals
- No runtime dependencies
- Tiny — single class, ~80 lines

## Usage

Add a NuGet reference:

```xml
<PackageReference Include="Trellis.Microservices.Abstractions" />
```

Then reference the constants:

```csharp
using Trellis.Microservices.Abstractions;

// In a custom gateway or test:
identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ContractVersion,
                            TrellisInternalJwtClaimNames.CurrentContractVersion));
identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.PermissionsCount, "3"));
identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.Permissions, "orders:read"));
// ...
```

If you are using `Trellis.Yarp` AND `Trellis.Microservices.AspNetCore` (the standard pairing), you do NOT need to reference this package directly. Both packages reference it transitively and you can rely on the defaults in `TrellisActorForwardingOptions` / `TrellisInternalJwtActorOptions`.

## Contract integrity rules

These are enforced jointly by the gateway and consumer. A third-party implementation that omits any of them is **not** contract-conformant.

1. **Always emit the sentinel.** Every minted token MUST carry `ContractVersion = CurrentContractVersion`. The consumer fails closed (`Maybe<Actor>.None`) on missing or duplicated sentinel.
2. **Always emit both counts.** `PermissionsCount` and `ForbiddenPermissionsCount` MUST be emitted as decimal-string non-negative integers, including `"0"`.
3. **Always emit `JwtId`.** Fresh per token; the audit-correlation key.
4. **Permissions / ForbiddenPermissions are multi-valued, never joined.** The consumer's `StrictClaimShape = true` (default) rejects values containing `,` or starting with `[` / `{`.
5. **Counts must equal observed multi-valued occurrences.** Off-by-one yields `Maybe<Actor>.None`.

See the full spec in [`trellis-api-microservices-abstractions.md`](../../docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md).

## Version compatibility

This package is **versioned independently** from the gateway and consumer packages. Within a single contract version (`CurrentContractVersion = "1"`), the literals are immutable and will not change in any v1.x release.

A future v2 will:
- Ship a new major version of `Trellis.Microservices.Abstractions` with `CurrentContractVersion = "2"` (and potentially renamed / added claim members).
- Ship matching major versions of `Trellis.Yarp` and `Trellis.Microservices.AspNetCore` that depend on the new abstractions major.
- Provide a migration runbook for operators standing up a heterogeneous-version fleet during rollout.

## License

[MIT](../../LICENSE).
