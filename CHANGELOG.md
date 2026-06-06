# Changelog

All notable changes to this repository will be documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- Initial repository scaffolding: `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `.gitattributes`, `global.json`, `version.json`, `nuget.config`, `LICENSE`, `README.md`, `CHANGELOG.md`, `Trellis.Microservices.slnx`, `build/test.props`, `build/Trellis.ApiReference.targets`, `icon.png`.
- LLM-discoverability documentation under `docs/docfx_project/api_reference/`:
  - `trellis-api-microservices-cookbook.md` — entry-point cookbook with task-lookup table and recipe placeholders (Recipes 1 and 2 will be inlined verbatim from `xavierjohn/Trellis` Recipes 33 + 34 when the source files land).
  - `trellis-api-yarp.md` — gateway-side API reference (moved from `xavierjohn/Trellis`).
  - `trellis-api-internal-jwt.md` — consumer-side API reference (carved from `xavierjohn/Trellis` `trellis-api-asp.md` `TrellisInternalJwt*` sections, namespace renamed from `Trellis.Asp.Authorization` to `Trellis.Microservices.AspNetCore`).
  - `trellis-api-microservices-abstractions.md` — new abstractions package reference.
- `.github/copilot-instructions.md` — agent instructions with the "P4 invariants — never regress" 14-row checklist for any change touching minter / validator / provider code.
- `.github/dependabot.yml` — weekly GitHub Actions and NuGet updates.

### Notes

- No `.cs` files yet. Source for the three packages lands in follow-up PRs: B (`Trellis.Microservices.Abstractions`), then C (`Trellis.Yarp` move + `Trellis.Microservices.AspNetCore` carve-out from `xavierjohn/Trellis`).
- Package preview NuGets will be published once PR C lands.
