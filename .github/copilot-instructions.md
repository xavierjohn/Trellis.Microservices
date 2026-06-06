# GitHub Copilot Instructions for Trellis.Microservices

## Project overview

`Trellis.Microservices` ships the Path B (Trellis internal JWT) microservice authorization story for the [Trellis framework](https://github.com/xavierjohn/Trellis). The repo contains three NuGet packages: `Trellis.Yarp` (gateway-side JWT minter, YARP transform), `Trellis.Microservices.AspNetCore` (consumer-side actor hydration), and `Trellis.Microservices.Abstractions` (shared internal-JWT contract constants).

These instructions are for repository workflow and contribution conventions only. They are not the source of truth for how to use the APIs.

## API usage source of truth

Before writing or changing code that uses these packages, read the relevant files in `docs/docfx_project/api_reference/`.

Start with [`docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md`](../docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md). Use its task lookup table to find the right recipe, then read the package reference files for exact signatures, overloads, namespaces, and examples. Do not infer API behavior from these Copilot instructions.

### Recommended context size

This repo is significantly narrower than the main Trellis repo — the full set of API references is ~80 KB (~20K tokens). With required upstream docs (Trellis main repo's authorization + asp + servicedefaults + Recipe 7/32) the working set is **120–180K tokens**.

| Tier | Context | When this is enough |
|---|---|---|
| **Minimum** | 100K | Single-side tasks (gateway-only OR consumer-only); can drop the cross-side reference. |
| **Recommended** | 200K | Any cross-side work (the contract spans both halves; must hold both references in context). |
| **Comfortable** | 400K+ | Cookbook recipe authoring, security review, or designing new sibling packages (APIM, Envoy, Mtls). |

### Mandatory loads at session start

For any non-trivial work in this repo, load these **before** writing the first line of code:

1. `docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md` — always. Its task lookup table is the entry point and lists the cross-repo deps.
2. The area-specific reference for the package being modified (table below).
3. The companion side's reference if the change crosses the gateway↔consumer boundary (the contract MUST stay in lock-step on both sides; modifying `Trellis.Yarp`'s mint without checking `Trellis.Microservices.AspNetCore`'s hydration silently breaks the contract).
4. The upstream `Trellis` repo's `trellis-api-authorization.md` if the work touches `Actor` / `IActorProvider` semantics, and `trellis-api-asp.md` if it touches `AddJwtBearer` / `ClaimsActorProvider` / `EntraActorProvider` integration.

| When touching... | Read first |
|---|---|
| YARP transform, JWT minter, audience projection, ActorIdResolver | `docs/docfx_project/api_reference/trellis-api-yarp.md` |
| Discovery endpoint, JWKS publishing, key rotation | `docs/docfx_project/api_reference/trellis-api-yarp.md` |
| Consumer-side `TrellisInternalJwtActorProvider` (hydration, strict-claim-shape validation, `RequiredAttributes`) | `docs/docfx_project/api_reference/trellis-api-internal-jwt.md` |
| `TrellisInternalJwtClaimNames` (contract constants) | `docs/docfx_project/api_reference/trellis-api-microservices-abstractions.md` |
| Strict `AddJwtBearer` validation profile | `docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md` Recipe 1 |
| Multi-tenant ABAC enforcement, cross-cluster permission projection | `docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md` Recipe 2 |
| `Actor`, `IActorProvider`, `IAuthorizeResource<>` | [Upstream](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-authorization.md) |
| `AddJwtBearer` / `ClaimsActorProvider` / `EntraActorProvider` integration | [Upstream](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-asp.md) |
| `TrellisServiceBuilder` slot patterns | [Upstream](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/api_reference/trellis-api-servicedefaults.md) |

### Preflight verification — required before generating non-trivial code

Before producing any non-trivial code, **explicitly answer these in your reasoning** (one or two lines is enough, but skipping the step is not allowed):

1. **Which task am I doing?** Name the task in the cookbook's task-lookup table.
2. **Which recipe applies?** Cite recipe number — *"Recipe 1 — Strict AddJwtBearer profile"* or *"Recipe 2 — Microservices end-to-end"*. If no recipe applies, name the package reference section that does.
3. **Does my change cross the gateway↔consumer boundary?** If yes, list the matching change required on the other side. The internal-JWT contract is symmetric: a new sentinel claim, a renamed structural claim, a strict-shape rule — all need to land in BOTH `Trellis.Yarp` (minter) AND `Trellis.Microservices.AspNetCore` (validator) in lock-step, or one side will silently produce tokens the other rejects.
4. **Am I touching a reserved claim name?** Attribute keys returned by `ProjectAttributes` MUST NOT collide with `iss`/`aud`/`exp`/`nbf`/`iat`/`jti`/`sub` or with the Trellis structural names (`permissions`/`forbidden_permissions`/`trellis_actor_contract_version`/`trellis_permissions_count`/`trellis_forbidden_permissions_count`). The minter throws `InvalidOperationException` on collision; the consumer validator rejects reserved names at startup. Use `external_iss` for issuer namespacing.
5. **Am I about to invent an API?** If you cannot point at a specific reference file + line range for the method/extension/attribute you are about to use, stop and load that reference. Do not synthesize the signature from prior knowledge.

If you cannot answer any of these, stop and load the missing reference before continuing.

### Adding a new option to `TrellisActorForwardingOptions` or `TrellisInternalJwtActorOptions`

When adding a new option to either options class, the work is **not complete** until:

1. The matching validator rule is added (`TrellisActorForwardingOptionsValidator` or `TrellisInternalJwtActorOptionsValidator`) with a clear error message describing the security consequence of mis-configuration.
2. A validator test covers both the "valid" and "invalid" paths.
3. The corresponding API reference file (`trellis-api-yarp.md` or `trellis-api-internal-jwt.md`) is updated to include the new option in the options table.
4. If the option affects the minted JWT contract or the consumer's hydration of it, BOTH `Trellis.Yarp`'s minter test suite AND `Trellis.Microservices.AspNetCore`'s provider test suite get matching coverage.
5. The cookbook recipe is updated if the option changes the recommended configuration shape.

### Validating sub-agent findings

Sub-agents (rubber-duck, code-review) are recommendation engines, not ground truth. Before adopting a finding:

- Verify the claim against the relevant API reference, source code, or existing test. Most non-trivial findings are testable in 30 seconds.
- Push back on claims that contradict verified docs/source or existing intentional design. Reference earlier PRs (e.g., via `git log -S 'token'`) when the claim implies undoing prior work.
- Adopt findings that survive verification — and adopt them confidently, because verification means you understand the bug, not just the reviewer's claim about it.

If an API reference contradicts these instructions, treat the API reference as authoritative for API usage.

## Code style

- Omit braces for single-line `if`/`return` statements when consistent with nearby code.
- Use `char` overloads for single-character operations.
- Use collection expressions in tests where appropriate.
- Use `ConfigureAwait(false)` in library source code; do not add it in test code.
- Prefer `ValueTask<T>` for high-frequency operations that may complete synchronously; prefer `Task<T>` for I/O-bound work.
- Avoid broad `try`/`catch` blocks and silent fallbacks. Surface or propagate errors using the existing patterns documented in the API references.
- Keep public APIs documented with XML comments.

## Security tier conventions specific to this repo

This repo ships JWT-minting and JWT-validation code that downstream services trust as authoritative identity. **Signing-key compromise = full identity spoof until key revocation propagates.** Code review must hold this PR class to a higher bar than the main Trellis repo:

- **Two reviewers required** on any change to `TrellisActorJwtMinter`, `TrellisActorForwardingOptionsValidator`, `TrellisInternalJwtActorProvider`, or `TrellisInternalJwtActorOptionsValidator`. One reviewer should specifically check for fail-closed-on-misconfiguration posture (validator throws fail-closed, runtime silent-skip is fail-closed, no fail-open default added).
- **Audit-log redaction must hold across changes.** Every `[LoggerMessage]` event in `TrellisActorForwardingTransformProvider` carries low-cardinality metadata only — never raw JWT, never claim values, never actor IDs. Any new log message added must be reviewed against the `ApplyAsync_AuditLog_*` hard-assertion tests; they assert no claim-value string ever appears in any log entry.
- **Reserved-claim-name guards must hold.** The minter's `ReservedJwtClaimNames` + `TrellisStructuralClaimNames` HashSets must include every RFC 7519 registered claim + every claim the minter itself emits. Adding a new structural claim to the minted JWT means adding it to the guard set in the same PR.

### P4 invariants — never regress

These shipped in main repo's PR #582 / #583 / #584 and are non-negotiable. Any code change that touches a file enforcing one of these must add or update a test asserting the invariant still holds. The matching tests live in `Trellis.Yarp/tests/` and `Trellis.Microservices.AspNetCore/tests/` — if a PR touches an invariant cell below and does not touch a test, request changes.

| # | Invariant | Where enforced | Don't allow a PR to |
|---|---|---|---|
| 1 | Asymmetric-only signing — symmetric keys + HMAC algorithms rejected at startup, INCLUDING `JsonWebKey { Kty: "oct" }` wrappers | `TrellisActorForwardingOptionsValidator` | Add `SymmetricSecurityKey` support, accept `HS*` algorithms, or unwrap `JsonWebKey` without re-checking `Kty` |
| 2 | Every `SigningCredentials` MUST include a non-empty `Kid` | `TrellisActorForwardingOptionsValidator` | Default a synthetic kid silently or skip the empty-check |
| 3 | JWKS rotation ring (active + every `PreviousSigningKeys` entry, with kid uniqueness check across the ring) | `TrellisJwksConverter`, JWKS endpoint, validator | Add a key to the ring without uniqueness assertion, or drop `PreviousSigningKeys` from the published JWKS |
| 4 | `ActorIdResolver` MUST be overridden for multi-IdP fronts — default produces collisions | `TrellisActorForwardingOptions` + cookbook Recipe 2 | Add a multi-IdP sample that omits the override |
| 5 | `Lifetime` capped to `[1m, 30m]` at startup | `TrellisActorForwardingOptionsValidator` | Widen the cap without a public-API-change PR and 2-reviewer signoff |
| 6 | Sentinel + count claims + `jti` mandatory — `trellis_actor_contract_version=1`, `trellis_permissions_count`, `trellis_forbidden_permissions_count` (always emitted, including `"0"`), `jti` | `TrellisActorJwtMinter` | Omit `trellis_forbidden_permissions_count` when forbidden list is empty (breaks deny-overrides-allow integrity invariant) |
| 7 | JWKS endpoint silent-skips unsupported `SecurityKey` types (defense-in-depth fail-closed); discovery doc advertises only the active `SigningCredentials.Algorithm` (not a hardcoded list); JWKS `alg` field normalized to active algorithm for every key in ring | JWKS + discovery endpoint handlers | Hardcode an algorithm list, or throw on unsupported key types instead of skipping |
| 8 | Discovery + JWKS endpoints use `.AllowAnonymous()` to survive a fallback `[Authorize]` policy | `MapTrellisDiscoveryEndpoint` | Drop the `AllowAnonymous` chain in a "cleanup" refactor |
| 9 | Minter throws on attribute keys colliding (ordinal-ignore-case) with reserved JWT claim names (`iss`/`aud`/`exp`/`nbf`/`iat`/`jti`/`sub`) or the EXACT Trellis structural claim names (`permissions`, `forbidden_permissions`, `trellis_actor_contract_version`, `trellis_permissions_count`, `trellis_forbidden_permissions_count`) — NOT a `trellis_*` prefix match | `TrellisActorJwtMinter` | Add an attribute pass-through that bypasses the `ReservedJwtClaimNames` / `TrellisStructuralClaimNames` guard sets, or expand a guard set without a coordinated `Trellis.Microservices.Abstractions` version bump |
| 10 | Registration validator enforces EXACTLY ONE `IActorProvider` (not just `>= 1`) | `TrellisServiceBuilder` actor-provider slot logic | Loosen to `>= 1` or silently overwrite |
| 11 | No-actor path CLEARS the upstream `Authorization` header (fail-closed posture) | `TrellisActorForwardingTransformProvider` | "Forward upstream Authorization when no actor" — that is the silent-spoof bug we shipped a fix for |
| 12 | Audit log uses PROJECTED counts (post-`ProjectActor`), emits `kid`/`jti`/`iss`/`aud`/`exp`/counts only, NEVER claim values or actor IDs | `TrellisActorForwardingTransformProvider` LoggerMessage events | Add structured logging that includes any claim value or actor ID, or use source-actor counts instead of post-projection counts |
| 13 | Downstream JWT consumer config MUST set `MapInboundClaims = false` AND `TryAllIssuerSigningKeys = false` | Recipe 1 in cookbook; consumer-side `AddJwtBearer` examples | Add an example with either flag set to `true` (breaks attribute lookup or breaks rotation-isolation respectively) |
| 14 | Reserved-claim-name guard set is the union of RFC 7519 registered claims + every claim the minter itself emits — must be kept in sync as new claims are added to the contract | `TrellisActorJwtMinter` `ReservedJwtClaimNames` + `TrellisStructuralClaimNames` HashSets | Add a new structural claim to the minted JWT without adding it to the guard set in the same PR |

## Test-driven development

Follow TDD when fixing bugs or adding features:

1. Add or update a failing test that proves the bug or specifies the new behavior.
2. Implement the smallest correct change.
3. Refactor while keeping tests green.

Do not skip the red step for bug fixes or new behavior.

## Test organization

| Area | Source | Tests |
|---|---|---|
| Shared abstractions | `Trellis.Microservices.Abstractions/src/` | `Trellis.Microservices.Abstractions/tests/` |
| YARP gateway | `Trellis.Yarp/src/` | `Trellis.Yarp/tests/` |
| Consumer-side hydration | `Trellis.Microservices.AspNetCore/src/` | `Trellis.Microservices.AspNetCore/tests/` |
| End-to-end harness | `Examples/E2EHarness/` (single project) | same project |

Test method names should follow `[Method]_[Variant]_[Scenario]_[Expectation]`.

For the gateway↔consumer contract: every contract change (new sentinel claim, new strict-shape rule, etc.) must have BOTH a `Trellis.Yarp` minter test AND a `Trellis.Microservices.AspNetCore` provider test verifying the same scenario from each side.

## Documentation standards

When adding or changing public API surface, update the relevant API reference file in `docs/docfx_project/api_reference/`. The package README.md, NUGET_README.md, and the cookbook may also need updates when the change affects the recommended configuration shape.

DocFX artifact checklist for package or public API changes:

| Artifact | Location |
|---|---|
| DocFX metadata | `docs/docfx_project/docfx.json` |
| Package README | `Trellis.{Package}/README.md` |
| NuGet README | `Trellis.{Package}/NUGET_README.md` |
| AI API reference | `docs/docfx_project/api_reference/trellis-api-{name}.md` |
| Cookbook | `docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md` (when behavior change affects recipes) |

## File encoding and PowerShell

All repository files must be UTF-8 with BOM (enforced via `.editorconfig` `charset = utf-8-bom`).

When using PowerShell for file writes, preserve the BOM:

```powershell
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($path, $content, $utf8Bom)
```

Avoid `Set-Content` for repository files because it can change encoding.

## Validation before handoff

Before considering code work complete:

1. Run `dotnet build` from the repository root.
2. Run `dotnet test` from the repository root.
3. **Run `pwsh ./docs/docfx_project/api_reference/audit-stale-docs.ps1`** after any `.cs` or `.md` edit. The script flags deprecated vocabulary; CI `publish-docs` runs it as a separate step from `docfx build`, so docfx clean ≠ audit clean.
4. Confirm public API changes are reflected in the API references AND the cookbook AND both package READMEs.
5. For changed code, use a code-review agent with `model: gpt-5.5` before committing. The microservices security tier warrants 2-3 review rounds minimum on any change to minter / validator / provider code.

Documentation-only changes do not require a build or test run unless they affect generated docs, examples that are compiled, or documented public API behavior.

## Git and PR rules

- Do not commit without explicit user approval.
- Do not push branches.
- Do not create or merge pull requests.
- **Do not amend commits, rebase pushed history, or force-push unless the user explicitly asks and confirms the history is safe to rewrite.** After a PR is open, fixes go as additive commits on top of the branch, then a normal `git push`. Each fix becomes a discrete commit reviewers can evaluate independently, the original CI history stays intact, and inline review comments remain anchored to the SHAs they were written against.
- If asked for a PR summary, output this copy-paste-ready format:

````markdown
**Title:** <short PR title>

```markdown
<full PR body>
```
````

## Pre-commit checklist

Before committing any changes after explicit approval:

1. Confirm required validation has passed (build + test + **audit-stale-docs** + BOM).
2. Confirm the diff contains only intended changes.
3. Run a code-review agent with `model: gpt-5.5` for changed code; for changes touching minter/validator/provider, expect 2-3 review rounds.
4. Present the final summary to the user.
