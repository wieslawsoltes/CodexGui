using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Controls;

public static class MarkdownBuiltInEditorIds
{
    public const string Code = "built-in-code-editor";
    public const string YamlFrontMatter = "built-in-yaml-front-matter-editor";
    public const string Abbreviation = "built-in-abbreviation-editor";
    public const string LinkReference = "built-in-link-reference-editor";
    public const string Footnote = "built-in-footnote-editor";
}

internal static class MarkdownEditorCatalog
{
    public static IReadOnlyList<MarkdownBlockTemplate> GetTemplates(MarkdownEditorFeature feature)
    {
        return feature switch
        {
            MarkdownEditorFeature.Alert => AlertTemplates,
            MarkdownEditorFeature.CustomContainer => CustomContainerTemplates,
            MarkdownEditorFeature.DefinitionList => DefinitionListTemplates,
            MarkdownEditorFeature.Figure => FigureTemplates,
            MarkdownEditorFeature.Footer => FooterTemplates,
            MarkdownEditorFeature.Math => MathTemplates,
            MarkdownEditorFeature.Mermaid => MermaidTemplates,
            MarkdownEditorFeature.YamlFrontMatter => MetadataTemplates,
            MarkdownEditorFeature.LinkReference => MetadataTemplates,
            MarkdownEditorFeature.Footnote => MetadataTemplates,
            MarkdownEditorFeature.Abbreviation => MetadataTemplates,
            _ => DefaultTemplates
        };
    }

    private static readonly IReadOnlyList<MarkdownBlockTemplate> DefaultTemplates =
    [
        new(
            "built-in-block-paragraph",
            "Paragraph",
            MarkdownEditorFeature.Paragraph,
            "New paragraph text.",
            "Insert a plain markdown paragraph."),
        new(
            "built-in-block-heading",
            "Heading",
            MarkdownEditorFeature.Heading,
            "## New heading",
            "Insert a second-level heading."),
        new(
            "built-in-block-bullet-list",
            "Bullet list",
            MarkdownEditorFeature.List,
            "- First item\n- Second item",
            "Insert a bullet list."),
        new(
            "built-in-block-task-list",
            "Task list",
            MarkdownEditorFeature.List,
            "- [ ] Pending task\n- [x] Completed task",
            "Insert a markdown task list."),
        new(
            "built-in-block-table",
            "Table",
            MarkdownEditorFeature.Table,
            "| Column | Value |\n| --- | --- |\n| Item | Detail |",
            "Insert a simple markdown table."),
        new(
            "built-in-block-code",
            "Code block",
            MarkdownEditorFeature.Code,
            MarkdownSourceEditing.BuildCodeFence("text", "// code"),
            "Insert a fenced code block.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> MetadataTemplates =
    [
        new(
            "built-in-block-yaml-front-matter",
            "YAML front matter",
            MarkdownEditorFeature.YamlFrontMatter,
            MarkdownSourceEditing.BuildYamlFrontMatter("title: New document\nsummary: Add summary here"),
            "Insert YAML front matter metadata."),
        new(
            "built-in-block-link-reference",
            "Link reference",
            MarkdownEditorFeature.LinkReference,
            MarkdownSourceEditing.BuildLinkReferenceDefinition("docs", "https://example.com", "Reference title"),
            "Insert a reference-style markdown link definition."),
        new(
            "built-in-block-footnote",
            "Footnote",
            MarkdownEditorFeature.Footnote,
            MarkdownSourceEditing.BuildFootnoteMarkdown("note", "Footnote text."),
            "Insert a markdown footnote definition."),
        new(
            "built-in-block-abbreviation",
            "Abbreviation",
            MarkdownEditorFeature.Abbreviation,
            MarkdownSourceEditing.BuildAbbreviationMarkdown("API", "Application programming interface"),
            "Insert an abbreviation definition.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> AlertTemplates =
    [
        new("alert-note", "Note alert", MarkdownEditorFeature.Alert, BuildAlertBlock("note", "Capture supporting context or implementation notes."), "Insert a neutral note alert."),
        new("alert-tip", "Tip alert", MarkdownEditorFeature.Alert, BuildAlertBlock("tip", "Highlight a best practice or a productivity shortcut."), "Insert a helpful tip alert."),
        new("alert-important", "Important alert", MarkdownEditorFeature.Alert, BuildAlertBlock("important", "Call out a decision or guidance that should stand out."), "Insert an important alert."),
        new("alert-warning", "Warning alert", MarkdownEditorFeature.Alert, BuildAlertBlock("warning", "Explain the risk and the follow-up action before continuing."), "Insert a warning alert."),
        new("alert-danger", "Danger alert", MarkdownEditorFeature.Alert, BuildAlertBlock("danger", "Document the breaking impact or high-risk condition."), "Insert a danger alert.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> CustomContainerTemplates =
    [
        new("custom-container-info", "Info container", MarkdownEditorFeature.CustomContainer, BuildCustomContainer("info", "Renderer status", "Custom containers can preserve a **type**, optional arguments, and nested markdown body content."), "Insert an info-style custom container."),
        new("custom-container-warning", "Warning container", MarkdownEditorFeature.CustomContainer, BuildCustomContainer("warning", "Migration note", "- keep Mermaid-specific containers in the Mermaid plugin\n- use generic containers for documentation callouts"), "Insert a warning-style custom container."),
        new("custom-container-neutral", "Neutral container", MarkdownEditorFeature.CustomContainer, BuildCustomContainer("details", string.Empty, "Use a neutral custom container for grouped notes or supporting context."), "Insert a neutral custom container.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> DefinitionListTemplates =
    [
        new("definition-list-basic", "Definition list", MarkdownEditorFeature.DefinitionList, "Rendering engine\n:   Maps `Markdig` nodes to native Uno controls.", "Insert a simple definition list."),
        new("definition-list-glossary", "Glossary entry", MarkdownEditorFeature.DefinitionList, "AST\nAbstract syntax tree\n:   A structured representation that separates **terms** from their definition body.\n\n    - supports multiple labels\n    - supports nested markdown", "Insert a glossary-style definition list block.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> FigureTemplates =
    [
        new("figure-basic", "Figure block", MarkdownEditorFeature.Figure, BuildFigure("Preview surface", "Supporting markdown can live inside the figure body.\n\n- captions stay separate from body content\n- preview editing preserves the fence syntax", "Figure captions can also live on the closing fence."), "Insert a figure with opening and closing fence captions."),
        new("figure-image", "Image figure", MarkdownEditorFeature.Figure, BuildFigure("Product preview", "![Alt text](https://example.com/image.png)\n\nReplace the placeholder image URL with your real media asset.", "Use the closing fence caption for supporting notes."), "Insert an image-oriented figure."),
        new("figure-code", "Code figure", MarkdownEditorFeature.Figure, BuildFigure("Render contract", """
            ```json
            {
              "feature": "figures",
              "mode": "plugin-backed"
            }
            ```
            """, "Figures can wrap code, notes, or media."), "Insert a figure that showcases non-image body content.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> FooterTemplates =
    [
        new("footer-basic", "Footer block", MarkdownEditorFeature.Footer, BuildFooter("Footer blocks can carry release notes, provenance, or closing context."), "Insert a simple footer block."),
        new("footer-links", "Footer with links", MarkdownEditorFeature.Footer, BuildFooter("Closing notes can still include [links](https://docs.avaloniaui.net) and inline markdown."), "Insert a footer that carries closing links.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> MathTemplates =
    [
        new("built-in-block-math", "Math block", MarkdownEditorFeature.Math, BuildMathBlock(@"\frac{a}{b} = \sqrt{x_1 + x_2}"), "Insert a fenced block math expression."),
        new("built-in-block-matrix", "Matrix", MarkdownEditorFeature.Math, BuildMathBlock(@"\begin{bmatrix} a & b \\ c & d \end{bmatrix}"), "Insert a matrix math block.")
    ];

    private static readonly IReadOnlyList<MarkdownBlockTemplate> MermaidTemplates =
    [
        new("mermaid-diagram-block", "Mermaid diagram", MarkdownEditorFeature.Mermaid, BuildMermaidFence("flowchart TD\n    Start[Start] --> Decide{Choice}\n    Decide -->|Yes| Ship[Ship it]\n    Decide -->|No| Revise[Revise]"), "Insert a Mermaid diagram block.")
    ];

    private static string BuildAlertBlock(string kind, string body)
    {
        var normalizedBody = MarkdownSourceEditing.NormalizeBlockText(body);
        var lines = normalizedBody.Split('\n', StringSplitOptions.None);
        var builder = new System.Text.StringBuilder();
        builder.Append("> [!").Append(kind.ToUpperInvariant()).Append(']').Append('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            builder.Append("> ").Append(lines[index]);
            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string BuildCustomContainer(string info, string arguments, string body)
    {
        var header = string.IsNullOrWhiteSpace(arguments)
            ? $":::{MarkdownSourceEditing.NormalizeInlineText(info)}"
            : $":::{MarkdownSourceEditing.NormalizeInlineText(info)} {MarkdownSourceEditing.NormalizeInlineText(arguments)}";
        return $"{header}\n{MarkdownSourceEditing.NormalizeBlockText(body)}\n:::";
    }

    private static string BuildFigure(string leadingCaption, string body, string trailingCaption)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("^^^ ").Append(MarkdownSourceEditing.NormalizeInlineText(leadingCaption)).Append('\n');
        builder.Append(MarkdownSourceEditing.NormalizeBlockText(body)).Append('\n');
        builder.Append("^^^ ").Append(MarkdownSourceEditing.NormalizeInlineText(trailingCaption));
        return builder.ToString();
    }

    private static string BuildFooter(string body)
    {
        var normalizedBody = MarkdownSourceEditing.NormalizeBlockText(body);
        if (normalizedBody.Length == 0)
        {
            return "^^";
        }

        var lines = normalizedBody.Split('\n', StringSplitOptions.None);
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            builder.Append("^^");
            if (lines[index].Length > 0)
            {
                builder.Append(' ').Append(lines[index]);
            }
        }

        return builder.ToString();
    }

    private static string BuildMathBlock(string expression)
    {
        return $"$$\n{MarkdownSourceEditing.NormalizeBlockText(expression)}\n$$";
    }

    private static string BuildMermaidFence(string source)
    {
        return MarkdownSourceEditing.BuildCodeFence("mermaid", source);
    }
}
