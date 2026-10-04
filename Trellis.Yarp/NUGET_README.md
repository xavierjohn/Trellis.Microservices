# Trellis.Yarp

YARP gateway integration for Trellis. Re-mints a per-cluster internal JWT from the full Trellis `Actor` (id + permissions + forbidden permissions + ABAC attributes), exposes an OIDC discovery + JWKS endpoint pair so downstream services can use `AddJwtBearer(o => o.Authority = gatewayUrl)` for transparent key rotation, and emits redacted audit telemetry on every mint.

Pairs with the consumer-side `TrellisInternalJwtActorProvider` in `Trellis.Microservices.AspNetCore`.

## Key features

- **`AddTrellisActorForwarding`** — `IReverseProxyBuilder` extension; per-request transform that mints a fresh per-cluster JWT from the full `Actor` and overwrites the upstream `Authorization` header.
- **`MapTrellisDiscoveryEndpoint`** — exposes `/.well-known/openid-configuration` + `/.well-known/jwks.json`. JWKS includes every key in the active rotation ring.
- **Asymmetric-only signing**, `kid` required on every key (startup-validated).
- **Sentinel + count claims** — `trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count` (always emitted, even when zero) + fresh `jti` per token. Detects the privilege-escalation footgun where a misbehaving proxy strips the deny-permission set.
- **Redacted audit telemetry** — every mint emits a `[LoggerMessage]` event carrying only low-cardinality metadata: `kid`, `jti`, `iss`, `aud`, `exp` (unix-seconds), and projected permission / forbidden counts. NEVER the raw JWT, raw claim values, or actor IDs.

## Security boundary

Signing-key compromise = full identity spoof until key revocation propagates. Mitigations: short token lifetimes (capped `[1m, 30m]` at startup), `kid`-aware overlapping JWKS rotation, audit-log redaction, emergency revocation procedure.

**Not AOT-compatible** (YARP itself is not AOT-clean).

See the [Trellis Microservices cookbook](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md) (Recipe 2 — "Microservices behind YARP, end-to-end") for the full operational walkthrough.

## Optional AgentDocs setup

This package includes a versioned API reference, but restoring it does not
install agent instructions. To opt in, restore your consuming project or
solution, then run from its Git root:

```powershell
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentDocs --version 0.1.0-preview.20 --tool-manifest .config/dotnet-tools.json
dotnet tool run agentdocs init <solution-or-project>
```

If the repository already has `.config/dotnet-tools.json`, reuse it. Restoring
the package never activates its guide: `init` lists `Trellis.Yarp` as pending
and prints the package IDs to add to `approvedPackages` in
`.agentdocs/policy.json`. Add the package IDs you trust, then run
`dotnet tool run agentdocs sync`. After a package upgrade, restore and run
`sync` again.

Companion links use declared cross-package references, not copied guides.
Approve the other trusted, restored guidance publishers as needed; `Trellis.Core`
owns the upstream guides. References never add dependencies or grant approval.
Unavailable or unapproved targets become notices; use
`dotnet tool run agentdocs sync --strict-references` to reject them instead.
