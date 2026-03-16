---
title: "Development"
---

# Development

CodexGui is a .NET 10 repository with a desktop app, a transport library, and multiple markdown libraries. Contributor workflows should validate both the solution and the docs site.

## Core validation path

- build the solution
- run the solution test baseline
- regenerate and validate the Lunet docs site

## Key files

- `.github/workflows/build.yml`
- `.github/workflows/docs.yml`
- `.config/dotnet-tools.json`
- `build-docs.sh` / `check-docs.sh` / `serve-docs.sh`
- `site/`

Continue with [Build, Test, and Docs](build-test-and-docs/) for the exact commands.
