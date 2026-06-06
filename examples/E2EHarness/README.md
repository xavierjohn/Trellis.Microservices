# E2EHarness — Release-gate scenarios for the Trellis.Microservices contract

This project is the **release-gate** for publishing a new preview of the `Trellis.Microservices.*` NuGet packages. CI runs it on every push to `main`; if any of the 8 scenarios fail, the publish workflow does not run.

The scenarios validate **end-to-end behavior** — actual YARP transform pipeline + actual `AddJwtBearer` validation + actual `TrellisInternalJwtActorProvider` hydration — not just type shapes. Per-package unit tests (in `Trellis.Yarp/tests/`, `Trellis.Microservices.AspNetCore/tests/`, etc.) cover the smaller invariants; this harness covers what only an end-to-end flow can prove.

## Why under `examples/` and not `tests/`

The harness is intentionally **examples-as-tests**: each scenario reads as a single ~30-line vignette that documents a real consumer scenario. A future LLM-generated client that wants to know "what does the no-actor fail-closed posture look like in practice?" can read `Scenario2_NoActor_UpstreamAuthorizationHeaderCleared` directly. The `examples/` directory is the right home for that documentation-first framing; the `Tests` csproj suffix is a build-convention concession so the repo's `Directory.Build.props` `IsTestProject` machinery picks the project up.

## Architecture

Each scenario uses in-process `Microsoft.AspNetCore.TestHost` instances wired through a custom `IForwarderHttpClientFactory` so YARP's outbound HTTP requests route through the destination's `HttpMessageHandler` instead of the network. No docker-compose, no sockets, deterministic across machines, < 5 seconds total in CI.

```
gateway TestServer (YARP + AddTrellisActorForwarding)
       │
       │  HttpRequestMessage
       │  (Authorization: Bearer <gateway-minted JWT>)
       ▼
destination TestServer (AddJwtBearer + AddTrellisInternalJwtActorProvider)
       │
       ▼
  /probe endpoint  →  ProbeResponse { id, permissions, forbiddenPermissions, attributes }
```

For "attack token" scenarios (sentinel-stripped, count-mismatch, comma-joined shape, etc.) the harness skips the gateway entirely: the test hand-crafts a malformed JWT signed with the harness's RSA key and POSTs it directly to the destination. This isolates the contract-integrity check on the consumer side.

## The 8 scenarios

| # | Scenario | What it validates |
|---|---|---|
| 1 | `HappyPath_GatewayMintsActorRoundtripsToDownstream` | Baseline — full `Actor` (id + permissions + forbidden + attributes) roundtrips gateway → JWT → downstream. |
| 2 | `NoActor_UpstreamAuthorizationHeaderCleared` | No-actor fail-closed posture (P4 round-1 security review). External bearer tokens MUST NOT reach the downstream. |
| 3 | `CrossAudienceMismatch_DownstreamRejects401` | Defense-in-depth via downstream `ValidAudience` pin. Misconfigured gateway minting wrong aud → 401. |
| 4 | `SentinelStripped_CountMismatchFailsClosed` | Misbehaving proxy strips `forbidden_permissions` claims but leaves the count → consumer fails closed. Deny-overrides-allow contract integrity invariant. |
| 5 | `ContractVersionSentinelMissing_FailsClosed` | Missing `trellis_actor_contract_version` claim → fail closed. Defends against pre-v1 / non-conformant gateways. |
| 6 | `PermissionsCountMismatch_FailsClosed` | Allow-side count mismatch → fail closed. The gateway must not lie about its projected counts. |
| 7 | `StrictClaimShape_CommaJoinedPermissionsRejected` | Permission value with comma → fail closed. Defends against gateway-side bug of comma-joining a list. |
| 8 | `ExpectedIssuerMismatch_ActorProviderFailsClosed` | `TrellisInternalJwtActorOptions.ExpectedIssuer` runtime check (defense-in-depth complement to JwtBearer's `ValidIssuer`). |

## Running

```powershell
# From repo root:
dotnet test examples/E2EHarness/E2EHarness.Tests.csproj -c Release

# Or filter to a single scenario:
dotnet test examples/E2EHarness/E2EHarness.Tests.csproj -c Release -- --filter-method "*Scenario4*"
```

## Mapping to P4 invariants

The scenarios cover the consumer-facing P4 invariants from `.github/copilot-instructions.md` "P4 invariants — never regress" table. Gateway-side startup-validation invariants (asymmetric-only, kid-required, lifetime cap, JWKS rotation ring) are covered by per-package unit tests in `Trellis.Yarp/tests/`; replicating them here would be redundant and slow the release gate down with no additional coverage.

| P4 invariant | Covered by |
|---|---|
| Asymmetric-only signing rejection at startup | `Trellis.Yarp/tests/TrellisActorForwardingOptionsValidatorTests.cs` |
| `Kid` required | Same |
| JWKS rotation ring | `Trellis.Yarp/tests/TrellisDiscoveryEndpointTests.cs` |
| Lifetime cap | `Trellis.Yarp/tests/TrellisActorForwardingOptionsValidatorTests.cs` |
| Sentinel + count claims | **This harness, Scenarios 4-6** |
| `AllowAnonymous` on discovery/JWKS endpoints | `Trellis.Yarp/tests/TrellisDiscoveryEndpointTests.cs` |
| Reserved claim-name guard (gateway) | `Trellis.Yarp/tests/TrellisActorJwtMinterTests.cs` |
| Exactly-one `IActorProvider` registration | `Trellis.Yarp/tests/TrellisActorForwardingRegistrationValidatorTests.cs` |
| No-actor `Authorization` clear | **This harness, Scenario 2** |
| Audit-log redaction | `Trellis.Yarp/tests/TrellisActorForwardingRequestTransformTests.cs` |
| Strict claim shape | **This harness, Scenario 7** |
| Mandatory consumer flags (`MapInboundClaims=false`, `TryAllIssuerSigningKeys=false`) | **This harness fixture configures both (`HarnessFixtures.StartDestinationAsync`)** as part of every gateway/downstream scenario; their effects are exercised by Scenarios 1-3 indirectly (the contract claim-name resolution + signing-key resolution paths only succeed when the flags are correctly set). The wrong-`kid` / multi-key-ring fallback path that `TryAllIssuerSigningKeys=false` specifically protects against is NOT exercised end-to-end by these 8 scenarios — that invariant is covered by `Trellis.Yarp/tests/TrellisDiscoveryEndpointTests.cs` (gateway side) and would warrant a dedicated future scenario here against a JwtBearer instance configured with a multi-key `IssuerSigningKeys` collection. |

## License

[MIT](../../LICENSE).
