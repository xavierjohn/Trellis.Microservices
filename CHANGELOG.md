# Changelog

All notable changes to this repository will be documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- **`Trellis.Yarp` package** — gateway-side YARP integration MOVED from `xavierjohn/Trellis` (`Trellis.Yarp` package, same NuGet ID, non-breaking move). Re-mints a per-cluster internal JWT from the full Trellis `Actor`, exposes OIDC discovery + JWKS endpoints, emits redacted audit telemetry on every mint. Now depends on `Trellis.Microservices.Abstractions` for the contract claim literals (removed the internal duplicate `TrellisInternalJwtClaimNames.cs` that lived in the package source).
- **`Trellis.Microservices.AspNetCore` package** — consumer-side counterpart CARVED OUT from `xavierjohn/Trellis`'s `Trellis.Asp.Authorization.TrellisInternalJwt*` types. **BREAKING namespace move** for P3 preview-stage adopters: `Trellis.Asp.Authorization` → `Trellis.Microservices.AspNetCore`. Type names unchanged (`TrellisInternalJwtActorProvider`, `TrellisInternalJwtActorOptions`, `TrellisInternalJwtActorOptionsValidator`, `ServiceCollectionExtensions.AddTrellisInternalJwtActorProvider`). Migration: replace `using Trellis.Asp.Authorization;` with `using Trellis.Microservices.AspNetCore;` and add a `Trellis.Microservices.AspNetCore` NuGet reference.
- Tests: 120 from `Trellis.Yarp.Tests` + 84 from `Trellis.Microservices.AspNetCore.Tests` (216 total across all three packages).

### Notes

- Both new packages reference upstream `xavierjohn/Trellis` packages (`Trellis.Authorization`, `Trellis.Core`, `Trellis.Asp`, `Trellis.Testing`) via NuGet at version `3.0.0-alpha.342` — the latest published preview at the time of this PR. Future bumps stay in lock-step with upstream releases that change consumed APIs.
- The `internal` copies of `TrellisInternalJwtClaimNames` (in upstream `Trellis.Yarp`) and the `Trellis.Asp.Authorization.TrellisInternalJwt*` types (in upstream `Trellis.Asp`) are not removed from the upstream repo in this PR — that's a separate cleanup PR against `xavierjohn/Trellis` (PR D, tracked in that repo's CHANGELOG).
- `Trellis.Microservices.AspNetCore.TrellisInternalJwtActorProvider` continues to implement `IProvideActorVaryHeaders` from upstream `Trellis.Asp.Authorization` (the interface stays in upstream). Cross-package dependency is deliberate.

## 0.1-alpha.b — Abstractions package

### Added

- **`Trellis.Microservices.Abstractions` package** — first package in this repo. Ships one public static class `TrellisInternalJwtClaimNames` with the canonical contract literals (`Subject`, `JwtId`, `Permissions`, `ForbiddenPermissions`, `ContractVersion`, `PermissionsCount`, `ForbiddenPermissionsCount`, `CurrentContractVersion = "1"`). Promotes the previously-internal `TrellisInternalJwtClaimNames` (from `xavierjohn/Trellis`'s `Trellis.Yarp`) to `public`, eliminating the duplication where the consumer side (`Trellis.Asp.Authorization.TrellisInternalJwtActorOptions` defaults) hard-coded the same strings by convention. AOT-compatible, no runtime dependencies.
- Tests: `Trellis.Microservices.Abstractions.Tests` — pins every literal value, snapshots the public const surface (catches silent additions / removals), and asserts non-empty + unique values + `IsPublic`/`IsAbstract`/`IsSealed` type modifiers.

## 0.1-alpha.a — Initial bootstrap

### Added

- Initial repository scaffolding: `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `.gitattributes`, `global.json`, `version.json`, `nuget.config`, `LICENSE`, `README.md`, `CHANGELOG.md`, `Trellis.Microservices.slnx`, `build/test.props`, `build/Trellis.ApiReference.targets`, `icon.png`.
- LLM-discoverability documentation under `docs/docfx_project/api_reference/`:
  - `trellis-api-microservices-cookbook.md` — entry-point cookbook with task-lookup table and recipe placeholders (Recipes 1 and 2 will be inlined verbatim from `xavierjohn/Trellis` Recipes 33 + 34 when the source files land).
  - `trellis-api-yarp.md` — gateway-side API reference (moved from `xavierjohn/Trellis`).
  - `trellis-api-internal-jwt.md` — consumer-side API reference (carved from `xavierjohn/Trellis` `trellis-api-asp.md` `TrellisInternalJwt*` sections, namespace renamed from `Trellis.Asp.Authorization` to `Trellis.Microservices.AspNetCore`).
  - `trellis-api-microservices-abstractions.md` — new abstractions package reference.
- `.github/copilot-instructions.md` — agent instructions with the "P4 invariants — never regress" 14-row checklist for any change touching minter / validator / provider code.
- `.github/dependabot.yml` — weekly GitHub Actions and NuGet updates.
- `docs/lint-api-reference.{ps1,md}` — API-reference doc lint (opt-in per project via `<TrellisEnableApiReferenceLint>true</TrellisEnableApiReferenceLint>`).

