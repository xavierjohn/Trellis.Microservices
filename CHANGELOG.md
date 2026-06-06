# Changelog

All notable changes to this repository will be documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Changed

- **Cookbook Recipes 1 + 2 are now real bodies, not placeholders.** PR #1 shipped Recipes 1 + 2 as placeholder stubs (carry-over from `xavierjohn/Trellis` Recipes 33 + 34 was deferred). This PR inlines the full content (~382 lines) with the namespace + extension-method rewrites applied:
  - `Trellis.Asp.Authorization` references rewritten to `Trellis.Microservices.AspNetCore`.
  - The two composition-root code blocks now call `services.AddTrellisInternalJwtActorProvider(...)` directly (the upstream `TrellisServiceBuilder.UseTrellisInternalJwtActor` slot is being removed in the coordinated upstream cleanup PR).
  - Recipe 1 heading rename: anchor changes from `#recipe-1--strict-addjwtbearer-validation-profile-for-usetrellisinternaljwtactor` to `#recipe-1--strict-addjwtbearer-validation-profile-for-addtrellisinternaljwtactorprovider`. Cross-doc links in `trellis-api-internal-jwt.md` and `trellis-api-yarp.md` updated.
  - Cross-references to upstream-only recipes (Recipe 7, Recipe 24, Recipe 32) point at full GitHub URLs against `xavierjohn/Trellis`.
- **`trellis-api-internal-jwt.md` composition-root guidance corrected.** PR #1's text recommended `services.AddTrellis(b => b.UseTrellisInternalJwtActor(...))` as the "preferred" entry point — but the upstream `TrellisServiceBuilder.UseTrellisInternalJwtActor` slot is being removed in coordinated v3 cleanup, so following that advice would compile-fail. Rewrote to show only `services.AddTrellisInternalJwtActorProvider(...)` and updated the migration table row to acknowledge the call-site change is real.
- **Top-level repo `README.md` Recipe 1 caption** + **`Trellis.Microservices.AspNetCore` package `README.md` / `NUGET_README.md` slot-status notes** updated to reflect post-cleanup reality (slot removed in v3; use the direct extension).
- **Test XmlDoc** in `AddTrellisInternalJwtActorProviderTests.cs` no longer claims companion tests live in `Trellis.ServiceDefaults.Tests.TrellisServiceBuilderTests` (those tests are deleted in the upstream v3 cleanup).

### Notes

- This PR unblocks the upstream cleanup PR in `xavierjohn/Trellis` that deletes `Trellis.Yarp/`, the `Trellis.Asp.Authorization.TrellisInternalJwt*` types, the `TrellisServiceBuilder.UseTrellisInternalJwtActor` slot, and the corresponding sections in main's cookbook (Recipes 33 + 34) and API references.

## 0.1-alpha.c — Trellis.Yarp move + Trellis.Microservices.AspNetCore carve-out

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

