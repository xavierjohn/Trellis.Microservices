# Sample — Trellis.Microservices end-to-end (Aspire)

A working, Aspire-orchestrated sample of the Trellis internal-JWT contract with a YARP gateway and **two** downstream microservices. One `dotnet run` boots everything plus the Aspire dashboard so you can watch logs, traces, and metrics fan out across services in real time.

> **Want the deep dive?** [`ARCHITECTURE.md`](./ARCHITECTURE.md) has the same content with proper Mermaid diagrams — request sequences, dependency graphs, the validation pipeline, fail-closed flows for cross-audience and missing-`tenant_id` attacks, and the Aspire telemetry plumbing.

```
                                                ┌─→ /api/orders/{**}   → Orders  (aud="orders")
   curl  ─── X-Test-Actor ───►  Gateway (:5001) ┤
                                YARP +          └─→ /api/billing/{**}  → Billing (aud="billing")
                                AddTrellisActorForwarding
                                MapTrellisDiscoveryEndpoint
                                                        ▲
                                                        │  OIDC discovery + JWKS
                                                        └── fetched by Orders + Billing
                                                            on first request

   All three services share Sample.ServiceDefaults → OTEL → Aspire dashboard
```

## What it demonstrates

- **Gateway** mints a fresh **per-cluster** JWT carrying the FULL `Actor` surface (id + permissions + forbidden permissions + ABAC attributes). The route `/api/orders/*` gets `audience="orders"`; `/api/billing/*` gets `audience="billing"` — both via `AudiencePerCluster = c => c.ClusterId`.
- **Orders + Billing** each run the strict Recipe 1 `AddJwtBearer` profile (signature via dynamically-fetched JWKS, issuer, audience, lifetime, RS256 pin, tight `ClockSkew`). Each pins its own `ValidAudience` so a token minted for the other cluster fails closed at the boundary.
- **`TrellisInternalJwtActorProvider`** enforces the sentinel + count contract claims and rejects any token that doesn't conform.
- **`RequiredAttributes = ["tenant_id"]`** on both services makes `tenant_id` a hard requirement — fail-closed defense against a gateway-side mint accidentally omitting a tenant-isolation claim.
- The hydrated `Actor` on each service matches what the Gateway started with — round-trip integrity proof.
- **YARP routing** (path-based, two clusters) — same `Gateway:5001` host, different downstream depending on path prefix.
- **Aspire telemetry** — every request flows through OTEL spans so the dashboard shows `Gateway -> Orders` and `Gateway -> Billing` traces with timings, logs, and metrics.

## Run it

**Prereq:** .NET 10 SDK (10.0.300+). No Aspire workload installation needed — Aspire 13+ is pure NuGet.

```powershell
cd examples/Sample
dotnet run --project Sample.AppHost
```

Aspire boots:

| Resource     | Port             | Notes |
|--------------|------------------|-------|
| Dashboard    | https://localhost:17051 (default) | Opens automatically in your browser. |
| Gateway      | http://localhost:5001 | Stable port (pinned in `Sample.AppHost/Program.cs` via `WithHttpEndpoint(port: 5001)`). |
| Orders       | Aspire-assigned  | Reached only via Gateway (service-discovered as `https+http://orders`). |
| Billing      | Aspire-assigned  | Reached only via Gateway (service-discovered as `https+http://billing`). |

> The dashboard requires HTTPS by default. If you're running inside a sandbox where the dev cert isn't trusted, set `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` and use the `http` launch profile in `Sample.AppHost/Properties/launchSettings.json`.

### Drive it with curl

The exact same Actor envelope works against either service — the Gateway re-mints per cluster:

```powershell
$actor = '{"Id":"alice","Permissions":["orders:read","billing:read"],"ForbiddenPermissions":[],"Attributes":{"tenant_id":"acme-corp"}}'

# Orders
Invoke-RestMethod http://localhost:5001/api/orders  -Headers @{ "X-Test-Actor" = $actor } | ConvertTo-Json -Depth 10

# Billing
Invoke-RestMethod http://localhost:5001/api/billing -Headers @{ "X-Test-Actor" = $actor } | ConvertTo-Json -Depth 10
```

Each returns the round-tripped Actor plus a `"service"` field identifying which service handled the request:

```json
{
  "service": "orders",
  "message": "hello from the orders service",
  "actor": {
    "id": "alice",
    "permissions": ["billing:read", "orders:read"],
    "forbiddenPermissions": [],
    "attributes": { "tenant_id": "acme-corp" }
  }
}
```

> **Prefer a request runner?** [`Sample.http`](./Sample.http) has every scenario below (orders + billing happy paths, different tenants, missing-tenant_id, no-actor, unknown route, OIDC discovery, JWKS) as click-to-send requests. Works with the VS Code REST Client extension, Visual Studio 2022 17.8+ built-in `.http` support, and JetBrains Rider's HTTP client.

### Watch it in the Aspire dashboard

Open the dashboard URL Aspire printed at startup. You'll see:

- **Resources** — `gateway`, `orders`, `billing` all "Running" with their dynamic endpoints listed.
- **Structured logs** — pick any service from the dropdown; you'll see the Trellis mint events tagged with `kid`, `iss`, `aud`, `permissions_count` from the Gateway, and the JwtBearer validation outcomes from Orders/Billing.
- **Traces** — every curl produces a multi-span trace: `Gateway POST` → `HttpClient GET orders` → `Orders GET /api/orders`. Click a trace to see the propagated `traceparent` flow through the system. This is the visible proof of cross-service request correlation.
- **Metrics** — request counts, latencies, GC/CPU/runtime metrics per service.

## What to try next

### Cross-audience attack — should fail closed

A token minted for `/api/orders/*` carries `aud="orders"`. Billing pins `ValidAudience = "billing"`. To prove the cross-audience reject, change `o.Audience = "billing"` to `"orders"` in `Billing/Program.cs`, restart Aspire, and call `/api/billing` — you'll see 401. The framework's per-cluster audience pin is what blocks token replay across services.

### Missing-tenant_id attack — should fail closed

Re-run the curl with the `Attributes` map empty:

```powershell
Invoke-WebRequest http://localhost:5001/api/orders `
  -Headers @{ "X-Test-Actor" = '{"Id":"alice","Permissions":["orders:read"],"ForbiddenPermissions":[],"Attributes":{}}' } `
  -SkipHttpErrorCheck
```

→ HTTP 401. The Orders service's `TrellisInternalJwtActorProvider` fails closed because `tenant_id` is in `RequiredAttributes` but absent from the JWT.

### Sentinel-strip attack — covered by E2EHarness

The contract claims (`trellis_actor_contract_version`, `trellis_permissions_count`, `trellis_forbidden_permissions_count`) are emitted by the gateway and validated by the consumer. If a malicious proxy strips the `forbidden_permissions` claims but leaves the count, the consumer fails closed (this is the deny-overrides-allow contract integrity invariant). The [`examples/E2EHarness/`](../E2EHarness/README.md) project's scenario 4 hand-crafts such a token to validate this end-to-end.

### Real IdP instead of `X-Test-Actor`

Swap `AddDevelopmentActorProvider` in `Gateway/Program.cs` for one of the production actor providers in upstream `Trellis.Asp.Authorization`:

- `AddClaimsActorProvider` — JWT bearer at the gateway boundary (consumer JWT validates against the external IdP)
- `AddEntraActorProvider` — Microsoft Entra / Azure AD
- `AddNestedJsonPathClaimsActorProvider` — Auth0 `app_metadata.roles`, B2C `extension_*`, Okta nested claims

The gateway-side strict `AddJwtBearer` profile (for the external token) has the same shape as the downstream's strict profile — see Recipe 2 of the [microservices cookbook](../../docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md#recipe-2--microservices-behind-yarp-end-to-end).

### Production-grade signing key rotation

The sample generates a fresh RSA key per gateway startup (zero-config). Production should:

1. Persist the active key (e.g., Azure Key Vault, AWS KMS).
2. Use `PreviousSigningKeys` during rotation: add the new key as `SigningCredentials`, keep the prior key in `PreviousSigningKeys` for the rotation overlap window (default 5-minute token lifetime + 30-second `ClockSkew` = drop after ~6 minutes).
3. Drop the retired key from `PreviousSigningKeys` once the overlap window expires.

See Recipe 1's "Key-rotation runbook" section for the operational walkthrough.

## Project layout

| Project                   | Purpose |
|---------------------------|---------|
| `Sample.AppHost`          | Aspire orchestrator. `dotnet run` here boots everything + dashboard. Wires service discovery (`WithReference(orders).WithReference(billing)`) so YARP can resolve `https+http://orders` and `https+http://billing`. |
| `Sample.ServiceDefaults`  | Shared `AddServiceDefaults()` + `MapDefaultEndpoints()` — OTEL (logs + metrics + traces), OTLP exporter for the dashboard, service discovery, HttpClient resilience, health checks at `/health` + `/alive` (Development only). |
| `Gateway`                 | YARP gateway. Two clusters (orders + billing), per-cluster audience pinning, OIDC discovery + JWKS published, `AddTrellisActorForwarding` re-mints the JWT, `AddServiceDiscoveryDestinationResolver` lets YARP destinations use service-discovery URIs. |
| `Orders`                  | First downstream microservice. `/api/orders` endpoint. Audience `"orders"`. `RequiredAttributes = ["tenant_id"]`. |
| `Billing`                 | Second downstream microservice. `/api/billing` endpoint. Audience `"billing"`. `RequiredAttributes = ["tenant_id"]`. |

## Pointers

| Topic | File |
|---|---|
| Cookbook (recipes 1 + 2) | [`docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md`](../../docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md) |
| Gateway API reference | [`docs/docfx_project/api_reference/trellis-api-yarp.md`](../../docs/docfx_project/api_reference/trellis-api-yarp.md) |
| Consumer API reference | [`docs/docfx_project/api_reference/trellis-api-internal-jwt.md`](../../docs/docfx_project/api_reference/trellis-api-internal-jwt.md) |
| Shared contract literals | [`docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md`](../../docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md) |
| Internal CI release-gate scenarios | [`examples/E2EHarness/README.md`](../E2EHarness/README.md) |

## Differences from the (future) template repo

This sample is for **reading**: 3 short Program.cs files, a few-line AppHost, one README. A reader scans it to understand the wiring.

The companion [`xavierjohn/Trellis.Microservices.Template`](https://github.com/xavierjohn/Trellis.Microservices.Template) repo (currently empty / parked) will host a richer Project Tracker demo (Projects + Members services, multi-tenant SaaS shape) installable via `dotnet new trellis-microservice`. Use that when you want to **scaffold** a new app; use this when you want to **understand** the pattern.
