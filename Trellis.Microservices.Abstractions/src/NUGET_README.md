# Trellis.Microservices.Abstractions

Shared contract constants for the Trellis internal-network JWT v1.

This package ships **one public static class** — `TrellisInternalJwtClaimNames` — that pairs the gateway-side minter (`Trellis.Yarp`) with the consumer-side actor provider (`Trellis.Microservices.AspNetCore`). Both sides reference these literals so any future contract version bump is one coordinated change.

## Properties

- AOT-compatible — ships only `public const string` literals
- No runtime dependencies
- Tiny — single class

## Usage

```csharp
using Trellis.Microservices.Abstractions;

identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.ContractVersion,
                            TrellisInternalJwtClaimNames.CurrentContractVersion));
identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.PermissionsCount, "3"));
identity.AddClaim(new Claim(TrellisInternalJwtClaimNames.Permissions, "orders:read"));
```

If you are using `Trellis.Yarp` AND `Trellis.Microservices.AspNetCore` (the standard pairing), you do NOT need to reference this package directly — both reference it transitively.

## When to reference directly

- You are implementing a third-party gateway against the Trellis internal JWT contract.
- You are implementing a custom consumer-side actor provider.
- You are writing an integration test that hand-crafts JWTs.

## Documentation

Full reference: [`trellis-api-microservices-abstractions.md`](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md).

## License

MIT.
