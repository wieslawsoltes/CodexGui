# Markdown Rendering Expansion Plan

## Current Engine

`CodexGui.Markdown` parses markdown with `Markdig` through `MarkdownParsingService`, using `UsePreciseSourceLocation()` and `UseAdvancedExtensions()`. Rendering is handled by `MarkdownInlineRenderingService`, which walks the parsed AST and produces Avalonia `InlineCollection` content for `MarkdownTextBlock`.

The renderer is already plugin-first:

- parser plugins can extend the `Markdig` pipeline
- block and inline rendering plugins can intercept AST nodes
- editor plugins and block template providers extend in-place editing

This architecture is solid. The remaining work is mostly about filling in native visuals for extension nodes the parser already emits.

## Supported Today

- headings, paragraphs, blockquotes, ordered and unordered lists
- task list markers
- tables
- fenced and indented code blocks
- inline emphasis, bold, and strikethrough
- links, autolinks, images, and footnote links
- footnote groups
- HTML blocks and inlines rendered safely as literal text
- Collapsible/details sections via plugin
- Safe embed/media cards via plugin
- Mermaid via plugin
- TextMate code highlighting via plugin

## Remaining Renderer Gaps

The parser emits additional node types that do not yet have first-class visuals:

### Block nodes

- `AlertBlock`
- `CustomContainer`
- `MathBlock`
- `DefinitionList`, `DefinitionItem`, `DefinitionTerm`
- `Figure`, `FigureCaption`
- `FooterBlock`
- metadata blocks that should be suppressed rather than shown literally:
  - `Abbreviation`
  - `YamlFrontMatterBlock`

### Inline nodes

- `AbbreviationInline`
- `MathInline`
- `EmphasisDelimiterInline` for superscript and subscript
- style helpers for punctuation-oriented extensions such as smart quotes

## Implementation Scope

This change will:

1. add native block rendering for alerts, custom containers, math, definition lists, figures, and footer content
2. add inline rendering for abbreviations, inline math, superscript, and subscript
3. suppress metadata-only blocks such as abbreviation definitions and YAML front matter
4. preserve safe HTML behavior
5. update the sample markdown showcase to exercise the new features

## File Plan

- `src/CodexGui.Markdown/Services/MarkdownInlineRenderingService.cs`
  - add block handlers
  - add inline handlers
  - add shared helper surfaces and styling
- `src/CodexGui.Markdown.Sample/Views/MainWindow.axaml.cs`
  - add showcase content for the new rendering coverage

## Validation Plan

- run `dotnet test CodexGui.slnx --nologo --verbosity minimal`
- confirm the sample markdown now demonstrates alerts, containers, math, abbreviations, and superscript/subscript rendering

## Safety and UX Constraints

- do not render raw HTML as executable or interactive web content
- stay within the existing `MarkdownTextBlock` and rich-block-host model
- keep visuals Fluent-aligned, restrained, and consistent with the current markdown surfaces

## WYSIWYG engine integration notes

`CodexGui.Markdown` now uses the rendered preview as an editing surface instead of treating preview rendering as a one-way display step. The source markdown still remains authoritative, but preview edits flow back through source-span-aware services so inline editing, hover highlighting, and source reveal stay coordinated.

Recommended integration pattern:

1. create the optional plugin set you want to enable
2. build one shared `MarkdownPluginRegistry` with `MarkdownRenderingServices.CreateRegistry(...)`
3. reuse that registry for both `CreateController(registry)` and `CreateEditingService(registry)`
4. assign the resulting services to `MarkdownTextBlock`
5. keep the source editor or document model authoritative and apply `MarkdownEdited` results back into it

In the sample app this shared-registry pattern keeps preview rendering, preview editing, hit testing, and template selection aligned to the same plugin surface.

## Validation guidance

Use the existing repository commands:

- `dotnet build CodexGui.slnx --nologo`
- `dotnet test CodexGui.slnx --nologo`
- `dotnet tool restore && bash ./check-docs.sh`

The markdown sample should also be checked manually for:

- hover-to-source highlighting
- click-to-reveal source behavior
- inline preview editing
- plugin-backed block rendering, including collapsible and embed cards
- file open/save flows that depend on `BaseUri`
