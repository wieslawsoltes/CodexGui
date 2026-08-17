---
title: "Repository Structure"
---

# Repository Structure

## Top-level layout

- `src/` - application and transport projects
- `external/ProMarkdown/` - pinned Markdown library Git submodule
- `site/` - Lunet documentation site content and configuration
- `.github/workflows/` - build and docs automation
- `.config/dotnet-tools.json` - local tool manifest for Lunet

## Solution projects

- `CodexGui.App` - Avalonia desktop shell
- `CodexGui.AppServer` - generated DTOs and JSON-RPC transport

The app references `external/ProMarkdown/src/ProMarkdown/ProMarkdown.csproj` for its Markdown control.

## Practical reading order

1. `README.md`
2. `site/articles/`
3. `src/CodexGui.App`
4. `src/CodexGui.AppServer`
5. the [ProMarkdown repository](https://github.com/wieslawsoltes/ProMarkdown) for Markdown library development
