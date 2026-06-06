# Changelog

All notable changes to this repository will be documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- **`Trellis.Microservices.Abstractions` package** — first package in this repo. Ships one public static class `TrellisInternalJwtClaimNames` with the canonical contract literals (`Subject`, `JwtId`, `Permissions`, `ForbiddenPermissions`, `ContractVersion`, `PermissionsCount`, `ForbiddenPermissionsCount`, `CurrentContractVersion = "1"`). Promotes the previously-internal `TrellisInternalJwtClaimNames` (from `xavierjohn/Trellis`'s `Trellis.Yarp`) to `public`, eliminating the duplication where the consumer side (`Trellis.Asp.Authorization.TrellisInternalJwtActorOptions` defaults) hard-coded the same strings by convention. AOT-compatible, no runtime dependencies.
- Tests: `Trellis.Microservices.Abstractions.Tests` — pins every literal value, snapshots the public const surface (catches silent additions / removals), and asserts non-empty + unique values.

### Notes

- The pre-existing `internal` copy of `TrellisInternalJwtClaimNames` (currently in `xavierjohn/Trellis`'s `Trellis.Yarp/src/`) is the duplication risk this package eliminates. When `Trellis.Yarp` lands in this repo via PR C, that version will reference `Trellis.Microservices.Abstractions.TrellisInternalJwtClaimNames` instead of carrying its own internal copy. **Removal of the internal copy from the upstream `xavierjohn/Trellis` repository is a separate PR (PR D) against that repo**, documented in that repo's CHANGELOG — not this one.

## 0.1-alpha — Initial bootstrap

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

