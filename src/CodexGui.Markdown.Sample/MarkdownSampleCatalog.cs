using System.Text;
using CodexGui.Markdown.Plugin.Alerts;
using CodexGui.Markdown.Plugin.Collapsible;
using CodexGui.Markdown.Plugin.CustomContainers;
using CodexGui.Markdown.Plugin.DefinitionLists;
using CodexGui.Markdown.Plugin.Embeds;
using CodexGui.Markdown.Plugin.Figures;
using CodexGui.Markdown.Plugin.Footers;
using CodexGui.Markdown.Plugin.Math;
using CodexGui.Markdown.Plugin.Mermaid;
using CodexGui.Markdown.Plugin.SyntaxHighlighting;
using CodexGui.Markdown.Plugin.TextMate;
using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Sample;

internal static class MarkdownSampleCatalog
{
    public static IReadOnlyList<MarkdownSamplePluginRegistration> RegisteredPlugins { get; } =
    [
        new(
            "AlertsMarkdownPlugin",
            "GitHub-style alert callouts with structured preview editing.",
            "Alerts and custom containers",
            "Keeps alert kind and quoted body markdown aligned without rewriting the raw `> [!...]` header.",
            static () => new AlertsMarkdownPlugin()),
        new(
            "CustomContainersMarkdownPlugin",
            "Generic `:::` containers with editable type tokens and arguments.",
            "Alerts and custom containers",
            "Use this for documentation callouts and panels that are not claimed by a more specific plugin such as Mermaid, Collapsible, or Embeds.",
            static () => new CustomContainersMarkdownPlugin()),
        new(
            "CollapsibleMarkdownPlugin",
            "Dedicated `:::details` and `:::collapsible` sections with summary and expansion-state editing.",
            "Collapsible sections",
            "Claims progressive-disclosure containers before the generic custom-container plugin so details blocks stay semantic.",
            static () => new CollapsibleMarkdownPlugin()),
        new(
            "DefinitionListMarkdownPlugin",
            "Glossary-style definition list rendering and structured editing.",
            "Definitions, abbreviations, and math",
            "Exercises richer block layouts than the core fallback renderer.",
            static () => new DefinitionListMarkdownPlugin()),
        new(
            "EmbedsMarkdownPlugin",
            "Safe media and document cards backed by fenced `:::embed` metadata blocks.",
            "Safe embeds",
            "Renders external resources as rich cards that link out instead of injecting remote iframe HTML into the preview.",
            static () => new EmbedsMarkdownPlugin()),
        new(
            "FiguresMarkdownPlugin",
            "Figure fences with separate leading and trailing caption regions.",
            "Figures",
            "Preserves caption fences while letting preview editing focus on semantic figure parts.",
            static () => new FiguresMarkdownPlugin()),
        new(
            "FootersMarkdownPlugin",
            "Footer blocks that keep the `^^` prefix out of the editor surface.",
            "Footer",
            "Useful for closing notes and provenance without forcing raw prefix management in the editor.",
            static () => new FootersMarkdownPlugin()),
        new(
            "MathMarkdownPlugin",
            "Inline and block math surfaces with plugin-backed editing hooks.",
            "Definitions, abbreviations, and math",
            "Keeps TeX-style payload editing behind a dedicated plugin boundary instead of expanding the core renderer.",
            static () => new MathMarkdownPlugin()),
        new(
            "MermaidMarkdownPlugin",
            "Mermaid custom containers and fenced diagrams.",
            "Mermaid diagram",
            "Owns diagram-specific parsing before generic custom-container handling kicks in.",
            static () => new MermaidMarkdownPlugin()),
        new(
            "SyntaxHighlightingMarkdownPlugin",
            "Built-in code highlighting fallback when TextMate does not claim a grammar.",
            "Code fence",
            "Covers aliases such as `postgresql` so the sample still validates a highlighted fallback path.",
            static () => new SyntaxHighlightingMarkdownPlugin()),
        new(
            "TextMateMarkdownPlugin",
            "Preferred TextMate renderer plus a richer code editor surface.",
            "Code fence",
            "Its lower plugin order keeps TextMate ahead of the built-in syntax-highlighting fallback when a grammar exists.",
            static () => new TextMateMarkdownPlugin())
    ];

    public static IReadOnlyList<MarkdownSampleValidationScenario> ValidationScenarios { get; } =
    [
        new(
            "Inline preview editing",
            "Enable preview editing, double-click blocks in the preview, and confirm the source editor updates with preserved markdown structure."),
        new(
            "Preview keyboard workflow",
            "Select a block in the preview, use keyboard navigation and structural shortcuts, then confirm slash commands and heading breadcrumbs stay aligned with the source editor."),
        new(
            "Hover and source reveal",
            "Move across preview blocks to highlight their source spans in AvaloniaEdit, then click a block to reveal its backing range."),
        new(
            "Plugin block coverage",
            "Exercise alerts, custom containers, collapsible sections, embed cards, figures, footers, definition lists, inline math, block math, and Mermaid diagrams from one document."),
        new(
            "Code-fence precedence",
            "Check that TextMate handles common grammars while the built-in syntax highlighter still covers fallback aliases such as `postgresql`."),
        new(
            "Metadata and inline extras",
            "Verify YAML front matter, link references, footnotes, abbreviations, emoji, smart punctuation, marked text, and inserted text surfaces."),
        new(
            "Document file workflow",
            "Use File > New/Open/Save/Save As to validate BaseUri-aware file loading and markdown persistence in the sample app.")
    ];

    public static IReadOnlyList<MarkdownSamplePluginDirection> PlannedPluginDirections { get; } =
    [
        new(
            "Comments and annotations",
            "Phase 2 candidate",
            "Keep this as a pluginized follow-up once inline editing and undo/session recovery settle."),
        new(
            "Table authoring toolkit",
            "Phase 2 candidate",
            "Add once structural row and column editing settles enough to support a richer WYSIWYG table surface."),
        new(
            "Citations and bibliography",
            "Phase 2 candidate",
            "Add after source-span-aware reference editing is stable enough to support citation round-tripping.")
    ];

    public static IMarkdownPlugin[] CreatePreviewPlugins()
    {
        var plugins = new IMarkdownPlugin[RegisteredPlugins.Count];
        for (var index = 0; index < RegisteredPlugins.Count; index++)
        {
            plugins[index] = RegisteredPlugins[index].CreatePlugin();
        }

        return plugins;
    }

    public static string BuildSupplementalMarkdown()
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Plugin registration and integration");
        builder.AppendLine();
        builder.AppendLine("- The sample creates a shared `MarkdownPluginRegistry` once and reuses it for both the preview render controller and the inline editing service.");
        builder.AppendLine("- `MarkdownRenderingServices` registers built-in editor support first, then the optional plugins listed below.");
        builder.AppendLine("- Parser, render, and editor precedence comes from each plugin component's `Order` value, so `TextMateMarkdownPlugin` stays ahead of the built-in syntax-highlighting fallback when it recognizes a grammar.");
        builder.AppendLine();
        builder.AppendLine("| Plugin | Purpose | Default sample section | Integration note |");
        builder.AppendLine("| --- | --- | --- | --- |");

        foreach (var plugin in RegisteredPlugins)
        {
            builder
                .Append("| `")
                .Append(plugin.DisplayName)
                .Append("` | ")
                .Append(plugin.Purpose)
                .Append(" | ")
                .Append(plugin.SampleSection)
                .Append(" | ")
                .Append(plugin.IntegrationNotes)
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Validation scaffolding");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Verification flow |");
        builder.AppendLine("| --- | --- |");

        foreach (var scenario in ValidationScenarios)
        {
            builder
                .Append("| ")
                .Append(scenario.Title)
                .Append(" | ")
                .Append(scenario.VerificationFlow)
                .AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("## Future plugin directions");
        builder.AppendLine();
        builder.AppendLine("These follow-on plugins are intentionally documented here without being registered in this worktree. Keep them documented until their projects exist and compile cleanly.");
        builder.AppendLine();
        builder.AppendLine("| Direction | Current action | Integration guidance |");
        builder.AppendLine("| --- | --- | --- |");

        foreach (var direction in PlannedPluginDirections)
        {
            builder
                .Append("| ")
                .Append(direction.Title)
                .Append(" | ")
                .Append(direction.CurrentAction)
                .Append(" | ")
                .Append(direction.IntegrationGuidance)
                .AppendLine(" |");
        }

        return builder.ToString().TrimEnd();
    }
}

internal sealed record MarkdownSamplePluginRegistration(
    string DisplayName,
    string Purpose,
    string SampleSection,
    string IntegrationNotes,
    Func<IMarkdownPlugin> CreatePlugin);

internal sealed record MarkdownSampleValidationScenario(
    string Title,
    string VerificationFlow);

internal sealed record MarkdownSamplePluginDirection(
    string Title,
    string CurrentAction,
    string IntegrationGuidance);
