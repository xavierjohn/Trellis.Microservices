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

## Optional AgentDocs setup

This package includes versioned API reference guidance and the cross-package
microservices cookbook, but restoring it does not install agent instructions.
To opt in, restore your consuming project or solution, then run from its Git root:

```powershell
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.20 --tool-manifest .config/dotnet-tools.json
dotnet tool run agentdocs init <solution-or-project>
```

If the repository already has `.config/dotnet-tools.json`, reuse it. Restoring
the package never activates its guides: `init` lists
`Trellis.Microservices.Abstractions` as pending and prints the package IDs to add
to `approvedPackages` in `.agentdocs/policy.json`. Add the package IDs you trust,
then run `dotnet tool run agentdocs sync`. After a package upgrade, restore and
run `sync` again.

Companion links use declared cross-package references, not copied guides.
Approve the other trusted, restored guidance publishers as needed; `Trellis.Core`
owns the upstream guides. References never add dependencies or grant approval.
Unavailable or unapproved targets become notices; use
`dotnet tool run agentdocs sync --strict-references` to reject them instead.

## License

MIT.
