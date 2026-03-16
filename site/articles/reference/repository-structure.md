---
title: "Repository Structure"
---

# Repository Structure

## Top-level layout

- `src/` - application, transport, markdown libraries, and sample projects
- `docs/` - tracked internal design and implementation notes
- `site/` - Lunet documentation site content and configuration
- `.github/workflows/` - build and docs automation
- `.config/dotnet-tools.json` - local tool manifest for Lunet

## Solution projects

- `CodexGui.App` - Avalonia desktop shell
- `CodexGui.AppServer` - generated DTOs and JSON-RPC transport
- `CodexGui.Markdown` - core markdown library
- `CodexGui.Markdown.Plugin.*` - plugin packages for optional markdown features
- `CodexGui.Markdown.Sample` - sample application for the markdown stack

## Practical reading order

1. `README.md`
2. `site/articles/`
3. `src/CodexGui.App`
4. `src/CodexGui.AppServer`
5. markdown libraries and plugin projects under `src/`
