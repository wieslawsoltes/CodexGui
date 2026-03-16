---
title: "Build, Test, and Docs"
---

# Build, Test, and Docs

## Solution commands

```bash
dotnet build CodexGui.slnx
dotnet test CodexGui.slnx
```

## Lunet docs commands

```bash
dotnet tool restore
bash ./build-docs.sh
bash ./check-docs.sh
bash ./serve-docs.sh
```

PowerShell equivalents are also available:

```powershell
./build-docs.ps1
./check-docs.ps1
./serve-docs.ps1
```

## CI layout

- `build.yml` validates build, test, docs generation, and NuGet packaging on pushes and pull requests.
- `docs.yml` deploys the Lunet site to GitHub Pages for the main or master branch.
- `release.yml` builds tagged releases, packs the reusable libraries, publishes them to NuGet.org, and creates the GitHub release.

## Site structure

The Lunet site lives under `site/` and follows the template-first pattern:

- `site/config.scriban` for project metadata and theme configuration
- `site/menu.yml` for top navigation
- `site/articles/` for documentation content and section menus
- `site/.lunet/css/template-main.css` for the precompiled template stylesheet used by the macOS-safe docs bundle
- `site/.lunet/css/site-overrides.css` for project-specific branding, home-page polish, and site-wide Lunet visual refinements
- `site/.lunet/includes/_builtins/bundle.sbn-html` for bundle link resolution when pages opt into the custom docs bundle

The generated output goes to `site/.lunet/build/www/` and should never be committed. The docs validation scripts also verify that the generated `css/lite.css` bundle includes the CodexGui home-page selectors so styling regressions are caught in CI.
