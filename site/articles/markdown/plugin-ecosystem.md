---
title: "Plugin Ecosystem"
---

# Plugin Ecosystem

The repository currently includes these markdown-related projects:

- `CodexGui.Markdown` - core rendering and editing services
- `CodexGui.Markdown.Plugin.Alerts` - alert or callout blocks
- `CodexGui.Markdown.Plugin.CustomContainers` - generic custom container support
- `CodexGui.Markdown.Plugin.DefinitionLists` - definition list rendering and editing
- `CodexGui.Markdown.Plugin.Figures` - figure blocks and captions
- `CodexGui.Markdown.Plugin.Footers` - footer metadata blocks
- `CodexGui.Markdown.Plugin.Math` - mathematical notation support
- `CodexGui.Markdown.Plugin.Mermaid` - Mermaid diagram rendering
- `CodexGui.Markdown.Plugin.SyntaxHighlighting` - syntax highlighting integration
- `CodexGui.Markdown.Plugin.TextMate` - TextMate-based editing and highlighting integration
- `CodexGui.Markdown.Sample` - sample application wiring all of the above together

## Design direction

This split mirrors the repository's broader architecture approach:

- keep the core focused
- isolate optional or specialized behavior in small projects
- make registration explicit in consumers
- support reuse outside the main Codex desktop application
