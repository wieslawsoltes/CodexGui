using CodexGui.Markdown.Core;
using CodexGui.Markdown.Plugin.Mermaid;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Xunit;

namespace CodexGui.Markdown.Core.Tests;

public sealed class MarkdownCoreTests
{
    [Fact]
    public void Parse_builds_parent_map_for_inline_content()
    {
        var parser = new MarkdownParsingService();

        var result = parser.Parse("# Heading\n\nParagraph with [Uno](https://platform.uno/) link.");

        Assert.True(result.UsesOriginalSourceSpans);

        var paragraph = Assert.IsType<ParagraphBlock>(result.Document[1]);
        var current = paragraph.Inline!.FirstChild;
        while (current is not null && current is not LinkInline)
        {
            current = current.NextSibling;
        }

        var link = Assert.IsType<LinkInline>(current);

        Assert.True(result.TryGetParent(link, out var parent));
        Assert.Same(paragraph.Inline, parent);
    }

    [Fact]
    public void Source_editing_operations_keep_markdown_structured()
    {
        const string markdown = "# Title\n\nOld paragraph";
        var paragraphSpan = new MarkdownSourceSpan(9, markdown.Length - 9);

        var updated = MarkdownSourceEditing.Replace(markdown, paragraphSpan, "New paragraph");
        var fence = MarkdownSourceEditing.BuildCodeFence("csharp", "Console.WriteLine(\"uno\");");
        var inlineMath = MarkdownSourceEditing.BuildInlineMath("a^2 + b^2 = c^2");

        Assert.Equal("# Title\n\nNew paragraph", updated);
        Assert.Equal("```csharp\nConsole.WriteLine(\"uno\");\n```", fence);
        Assert.Equal("$a^2 + b^2 = c^2$", inlineMath);
    }

    [Fact]
    public void Document_builder_maps_extension_and_plugin_blocks_into_ir()
    {
        var registry = new MarkdownPluginRegistry()
            .AddPlugin(new MermaidMarkdownPlugin());
        var builder = new MarkdownDocumentBuilder(registry);

        var document = builder.Build(
            """
            > [!WARNING]
            > Alert body

            :::info Release lane
            Custom container body
            :::

            Term
            :   Definition text

            ^^^ Figure title
            Figure body
            ^^^ Figure caption

            ^^ Footer detail

            $$
            a^2 + b^2 = c^2
            $$

            ```mermaid
            flowchart LR
                A --> B
            ```

            | Name | Value |
            | --- | --- |
            | Uno | Skia |
            """);

        Assert.Contains(document.Blocks, static block => block is MarkdownAlertBlock { Kind: "warning" });
        Assert.Contains(document.Blocks, static block => block is MarkdownCustomContainerBlock { Kind: "info" });
        Assert.Contains(document.Blocks, static block => block is MarkdownDefinitionListBlock);
        Assert.Contains(document.Blocks, static block => block is MarkdownFigureBlock);
        Assert.Contains(document.Blocks, static block => block is MarkdownFooterBlock);
        Assert.Contains(document.Blocks, static block => block is MarkdownMathBlock);
        Assert.Contains(document.Blocks, static block => block is MarkdownMermaidBlock);
        Assert.Contains(document.Blocks, static block => block is MarkdownTableBlock);

        var container = Assert.IsType<MarkdownCustomContainerBlock>(document.Blocks.First(static block => block is MarkdownCustomContainerBlock { Kind: "info" }));
        Assert.Equal("Release lane", container.Arguments);

        var mermaid = Assert.IsType<MarkdownMermaidBlock>(document.Blocks.First(static block => block is MarkdownMermaidBlock));
        Assert.Equal(MarkdownMermaidBlockSyntax.CodeFence, mermaid.Syntax);

        var definitions = Assert.IsType<MarkdownDefinitionListBlock>(document.Blocks.First(static block => block is MarkdownDefinitionListBlock));
        Assert.Contains(definitions.Items.SelectMany(static item => item.Terms), static term => term.Markdown == "Term");

        var figure = Assert.IsType<MarkdownFigureBlock>(document.Blocks.First(static block => block is MarkdownFigureBlock));
        Assert.Equal("Figure title", figure.LeadingCaptionMarkdown);
        Assert.Equal("Figure caption", figure.TrailingCaptionMarkdown);
    }

    [Fact]
    public void Document_builder_preserves_mermaid_container_metadata()
    {
        var registry = new MarkdownPluginRegistry()
            .AddPlugin(new MermaidMarkdownPlugin());
        var builder = new MarkdownDocumentBuilder(registry);

        var document = builder.Build(
            """
            :::mermaid compact
            flowchart LR
                A --> B
            :::
            """);

        var mermaid = Assert.IsType<MarkdownMermaidBlock>(Assert.Single(document.Blocks));
        Assert.Equal(MarkdownMermaidBlockSyntax.CustomContainer, mermaid.Syntax);
        Assert.Equal("mermaid", mermaid.Descriptor);
        Assert.Equal("compact", mermaid.Arguments);
    }

    [Fact]
    public void Document_builder_preserves_diagram_mermaid_container_metadata()
    {
        var registry = new MarkdownPluginRegistry()
            .AddPlugin(new MermaidMarkdownPlugin());
        var builder = new MarkdownDocumentBuilder(registry);

        var document = builder.Build(
            """
            :::diagram mermaid compact
            flowchart LR
                Source --> Preview
            :::
            """);

        var mermaid = Assert.IsType<MarkdownMermaidBlock>(Assert.Single(document.Blocks));
        Assert.Equal(MarkdownMermaidBlockSyntax.CustomContainer, mermaid.Syntax);
        Assert.Equal("mermaid", mermaid.Descriptor);
        Assert.Equal("compact", mermaid.Arguments);
    }

    [Fact]
    public void Layout_is_deterministic_and_hit_testing_tracks_links()
    {
        var builder = new MarkdownDocumentBuilder();
        var service = new MarkdownLayoutService();
        var document = builder.Build("Paragraph with [Uno Platform](https://platform.uno/) and `inline code`.");

        var first = service.Layout(document, 360);
        var second = service.Layout(document, 360);

        Assert.Equal(first.Height, second.Height, 6);
        Assert.Equal(first.Blocks.Count, second.Blocks.Count);
        Assert.Equal(first.HitRegions.Count, second.HitRegions.Count);

        var linkRegions = first.HitRegions.Where(static region => region.LinkTarget == "https://platform.uno/").ToArray();
        Assert.NotEmpty(linkRegions);
        var linkRegion = linkRegions[0];
        var hit = first.HitTest(linkRegion.X + 1, linkRegion.Y + 1);

        Assert.NotNull(hit);
        Assert.Equal("https://platform.uno/", hit!.LinkTarget);
        Assert.Equal(linkRegion.SourceSpan, hit.SourceSpan);
    }

    [Fact]
    public void Layout_preserves_visible_spacing_between_words()
    {
        var builder = new MarkdownDocumentBuilder();
        var service = new MarkdownLayoutService();
        var document = builder.Build("Alpha beta gamma");

        var layout = service.Layout(document, 500);
        var block = Assert.IsType<MarkdownInlineBlockLayout>(Assert.Single(layout.Blocks));
        var line = Assert.Single(block.Lines);

        Assert.Equal("Alpha beta gamma", line.Text);
        Assert.Contains(line.Fragments, static fragment => fragment.GapBefore > 0);
        Assert.Contains(line.Fragments, static fragment => fragment.LeadingWhitespace == " ");
    }

    [Fact]
    public void Layout_exposes_inline_math_hit_regions()
    {
        var builder = new MarkdownDocumentBuilder();
        var service = new MarkdownLayoutService();
        var document = builder.Build("Inline math $a^2 + b^2 = c^2$ stays editable.");

        var layout = service.Layout(document, 500);
        var hitRegion = Assert.Single(layout.HitRegions.Where(static region => region.Kind == "inline-math"));

        Assert.True(hitRegion.SourceSpan.Length > 0);
        Assert.Null(hitRegion.LinkTarget);
    }

    [Fact]
    public void Highlight_code_falls_through_plain_result_to_next_highlighter()
    {
        var service = new MarkdownLayoutService(new MarkdownPluginRegistry()
            .AddCodeHighlighter(new PlainFirstHighlighter())
            .AddCodeHighlighter(new ColoredSecondHighlighter()));

        var result = service.HighlightCode("public class Demo { }", "csharp");

        Assert.Equal("fake-colored", result.Engine);
        Assert.Contains(result.Runs, static run => run.Style.Bold);
    }

    [Fact]
    public void Split_runs_by_line_preserves_blank_lines_and_styles()
    {
        var lines = MarkdownCodeHighlighting.SplitRunsByLine(
        [
            new MarkdownStyledTextRun("public", new MarkdownTextStyle(Bold: true, Foreground: "#1D4ED8")),
            new MarkdownStyledTextRun(" class Demo", new MarkdownTextStyle()),
            new MarkdownStyledTextRun("\n\n", new MarkdownTextStyle()),
            new MarkdownStyledTextRun("return 42;", new MarkdownTextStyle(Foreground: "#7C3AED"))
        ]);

        Assert.Equal(3, lines.Length);
        Assert.Collection(
            lines[0],
            run =>
            {
                Assert.Equal("public", run.Text);
                Assert.True(run.Style.Bold);
            },
            run => Assert.Equal(" class Demo", run.Text));
        Assert.Empty(lines[1]);
        Assert.Single(lines[2]);
        Assert.Equal("return 42;", lines[2][0].Text);
        Assert.Equal("#7C3AED", lines[2][0].Style.Foreground);
    }

    [Fact]
    public async Task Layout_is_safe_under_concurrent_pretext_calls()
    {
        var builder = new MarkdownDocumentBuilder();
        var service = new MarkdownLayoutService();
        var document = builder.Build(
            """
            # Parallel layout

            > [!IMPORTANT]
            > Markdown layout should remain stable even when multiple Uno markdown controls measure at once.

            ```mermaid
            flowchart LR
                A --> B --> C
            ```

            $$
            \frac{a}{b} = \sqrt{x_1 + x_2}
            $$
            """);

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() =>
            {
                for (var iteration = 0; iteration < 20; iteration++)
                {
                    var layout = service.Layout(document, 640);
                    Assert.NotEmpty(layout.Blocks);
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void Editor_preferences_clone_retains_preferred_editor()
    {
        var preferences = new MarkdownEditorPreferences()
            .PreferEditor(MarkdownEditorFeature.Code, "textmate-code-editor");

        var clone = preferences.Clone();

        Assert.True(clone.TryGetPreferredEditor(MarkdownEditorFeature.Code, out var editorId));
        Assert.Equal("textmate-code-editor", editorId);
    }

    [Fact]
    public void Document_builder_preserves_metadata_blocks_and_advanced_inline_semantics()
    {
        var builder = new MarkdownDocumentBuilder();

        var document = builder.Build(
            """
            ---
            title: Uno metadata sample
            summary: Advanced markdown semantics
            ---

            Read [Uno docs][uno-docs], expand API, check ==marked== text, ++inserted++ text, x^^2^^, H~2~O, and mail <hello@platform.uno>.[^note]

            [^note]: Footnote body

            [uno-docs]: https://platform.uno/ "Uno Platform"

            *[API]: Application programming interface
            """);

        Assert.Contains(document.Blocks, static block => block is MarkdownYamlFrontMatterBlock);
        Assert.Contains(document.Blocks, static block => block is MarkdownFootnoteBlock { Label: "note" });
        Assert.Contains(document.Blocks, static block => block is MarkdownLinkReferenceBlock { Label: "uno-docs" });
        Assert.Contains(document.Blocks, static block => block is MarkdownAbbreviationBlock { Label: "API" });

        var paragraph = Assert.IsType<MarkdownParagraphBlock>(document.Blocks.OfType<MarkdownParagraphBlock>().First());
        var fragments = paragraph.Flows.SelectMany(static flow => flow.Fragments).ToArray();

        Assert.Contains(fragments, static fragment => fragment.LinkTarget == "https://platform.uno/");
        Assert.Contains(fragments, static fragment => fragment.LinkTarget == "mailto:hello@platform.uno");
        Assert.Contains(fragments, static fragment => fragment.SemanticClass == "abbreviation" && fragment.Text == "API");
        Assert.Contains(fragments, static fragment => fragment.Style.Marked);
        Assert.Contains(fragments, static fragment => fragment.Style.Inserted);
        Assert.Contains(fragments, static fragment => fragment.Style.Superscript);
        Assert.Contains(fragments, static fragment => fragment.Style.Subscript);
        Assert.Contains(fragments, static fragment => fragment.SemanticClass == "footnote");
    }

    private sealed class PlainFirstHighlighter : IMarkdownCodeHighlighter
    {
        public int Order => 10;

        public bool CanHighlight(string? languageHint) => true;

        public MarkdownCodeHighlightResult Highlight(string code, string? languageHint)
        {
            return new MarkdownCodeHighlightResult("plain", [new MarkdownStyledTextRun(code, new MarkdownTextStyle())]);
        }
    }

    private sealed class ColoredSecondHighlighter : IMarkdownCodeHighlighter
    {
        public int Order => 20;

        public bool CanHighlight(string? languageHint) => true;

        public MarkdownCodeHighlightResult Highlight(string code, string? languageHint)
        {
            return new MarkdownCodeHighlightResult(
                "fake-colored",
                [new MarkdownStyledTextRun(code, new MarkdownTextStyle(Bold: true, Foreground: "#1D4ED8"))]);
        }
    }
}
