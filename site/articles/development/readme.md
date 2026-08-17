---
title: "Development"
---

# Development

CodexGui is a .NET 10 repository with a desktop app, a transport library, and ProMarkdown as a Git submodule. Contributor workflows should initialize dependencies and validate both the solution and docs site.

## Core validation path

- build the solution
- package the CodexGui app and app-server projects
- regenerate and validate the Lunet docs site

## Key files

- `.github/workflows/build.yml`
- `.github/workflows/docs.yml`
- `.config/dotnet-tools.json`
- `build-docs.sh` / `check-docs.sh` / `serve-docs.sh`
- `site/`

Continue with [Build, Package, and Docs](build-package-and-docs/) for the exact commands.
