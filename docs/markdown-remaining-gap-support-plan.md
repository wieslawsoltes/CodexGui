# Markdown Remaining Gap Support Plan

## Problem

The markdown engine is now plugin-first for many rich block features, but a small set of generic markdown gaps remain in core:

- emoji shortcodes and smileys are not enabled
- smart typography is parsed only when explicitly enabled and is not rendered natively today
- `==mark==` and `++insert++` are parsed through emphasis extras but currently fall back to generic bold styling
- YAML front matter is not enabled in the parser, so existing rendering/editor hooks can never activate
- link reference definitions and abbreviation definitions have no visible metadata surfaces or built-in editors/templates
- footnotes render, but they still lack a built-in structured editor/template

## Proposed Approach

1. Extend the parser pipeline with:
   - `UseEmojiAndSmiley()`
   - `UseSmartyPants()`
   - YAML front matter parsing that also works when inserted from preview block actions
2. Add core inline rendering support for:
   - `EmojiInline`
   - `SmartyPant`
   - marked text (`==...==`)
   - inserted text (`++...++`)
3. Render currently hidden metadata nodes with source-aware surfaces:
   - YAML front matter
   - user-defined link reference definitions
   - document abbreviation definitions
4. Add built-in editor features, templates, and editors for:
   - YAML front matter
   - link reference definitions
   - footnotes
   - abbreviations
5. Update the sample markdown to demonstrate the new behavior and preview-editing surfaces.

## Todos

- `plan-markdown-gap-support`: capture the gap-support rollout in repo docs and the session workspace.
- `implement-markdown-gap-support`: extend parsing, rendering, built-in editors, and sample content for the remaining markdown gaps.
- `validate-markdown-gap-support`: build and test the solution after implementation.

## Completion

- Extended the parser pipeline with emoji, SmartyPants, and YAML front matter support, including YAML parsing for metadata blocks inserted from preview editing.
- Added native inline rendering for `EmojiInline`, `SmartyPant`, `==mark==`, and `++insert++`.
- Added visible metadata rendering for YAML front matter, reference-style link definitions, and document abbreviation definitions.
- Added built-in editor features, templates, and editor surfaces for YAML front matter, link references, footnotes, and abbreviations.
- Updated the markdown sample to exercise the new metadata surfaces, inline typography, and reference-style link behavior.
- Validation succeeded with:
  - `dotnet build CodexGui.slnx --nologo --verbosity minimal`
  - `dotnet test CodexGui.slnx --nologo --verbosity minimal`

## Plugin registration notes

The current sample plugin set is:

- `AlertsMarkdownPlugin`
- `CustomContainersMarkdownPlugin`
- `DefinitionListMarkdownPlugin`
- `FiguresMarkdownPlugin`
- `FootersMarkdownPlugin`
- `MathMarkdownPlugin`
- `MermaidMarkdownPlugin`
- `SyntaxHighlightingMarkdownPlugin`
- `TextMateMarkdownPlugin`

Integration guidance:

- create one shared `MarkdownPluginRegistry` for a markdown surface and reuse it for both `CreateControllerFromRegistry(...)` and `CreateEditingServiceFromRegistry(...)`
- keep plugin references additive so the core markdown package still behaves sensibly without them
- rely on each plugin component's `Order` value for precedence, which is what keeps `TextMateMarkdownPlugin` ahead of the built-in syntax-highlighting fallback when a grammar exists

## Sample coverage scaffolding

The default markdown sample now carries generated sections for:

1. plugin registration and integration notes
2. validation scaffolding
3. future plugin directions

This keeps the showcase aligned with the actual sample wiring instead of maintaining a second hand-written checklist.

## Future plugin directions

The current sample and solution now register dedicated plugins for:

- collapsible/details blocks
- embeds/media cards

Keep these next candidates documented-only until their projects exist and compile cleanly:

- comments/annotations
- citations/bibliography

If a planned plugin project is not present in the current worktree, document the intended registration and sample coverage instead of forcing a broken project reference.
