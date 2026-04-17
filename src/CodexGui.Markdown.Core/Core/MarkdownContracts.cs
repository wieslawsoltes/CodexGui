using Markdig;
using Markdig.Syntax;

namespace CodexGui.Markdown.Core;

public interface IMarkdownPlugin
{
    void Register(MarkdownPluginRegistry registry);
}

public interface IMarkdownParserPlugin
{
    int Order => 0;

    void Configure(MarkdownPipelineBuilder builder)
    {
    }

    string TransformMarkdown(string markdown) => markdown;
}

public interface IMarkdownCodeHighlighter
{
    int Order => 0;

    bool CanHighlight(string? languageHint);

    MarkdownCodeHighlightResult Highlight(string code, string? languageHint);
}

public sealed class MarkdownPluginRegistry
{
    private readonly List<IMarkdownParserPlugin> _parserPlugins = [];
    private readonly List<IMarkdownCodeHighlighter> _codeHighlighters = [];

    public IReadOnlyList<IMarkdownParserPlugin> ParserPlugins => _parserPlugins;

    public IReadOnlyList<IMarkdownCodeHighlighter> CodeHighlighters => _codeHighlighters;

    public MarkdownPluginRegistry AddPlugin(IMarkdownPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        plugin.Register(this);
        return this;
    }

    public MarkdownPluginRegistry AddParserPlugin(IMarkdownParserPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _parserPlugins.Add(plugin);
        return this;
    }

    public MarkdownPluginRegistry AddCodeHighlighter(IMarkdownCodeHighlighter highlighter)
    {
        ArgumentNullException.ThrowIfNull(highlighter);
        _codeHighlighters.Add(highlighter);
        return this;
    }

    public MarkdownPluginRegistry Clone()
    {
        var clone = new MarkdownPluginRegistry();
        clone._parserPlugins.AddRange(_parserPlugins);
        clone._codeHighlighters.AddRange(_codeHighlighters);
        return clone;
    }
}

public sealed class MarkdownParseResult(
    MarkdownDocument document,
    string originalMarkdown,
    string parsedMarkdown,
    IReadOnlyDictionary<MarkdownObject, MarkdownObject?>? parentMap = null)
{
    public static MarkdownParseResult Empty { get; } = new(
        new MarkdownDocument(),
        string.Empty,
        string.Empty,
        new Dictionary<MarkdownObject, MarkdownObject?>());

    public MarkdownDocument Document { get; } = document;

    public string OriginalMarkdown { get; } = originalMarkdown;

    public string ParsedMarkdown { get; } = parsedMarkdown;

    public IReadOnlyDictionary<MarkdownObject, MarkdownObject?> ParentMap { get; } =
        parentMap ?? new Dictionary<MarkdownObject, MarkdownObject?>();

    public bool UsesOriginalSourceSpans => string.Equals(OriginalMarkdown, ParsedMarkdown, StringComparison.Ordinal);

    public bool TryGetParent(MarkdownObject markdownObject, out MarkdownObject? parent)
    {
        ArgumentNullException.ThrowIfNull(markdownObject);
        return ParentMap.TryGetValue(markdownObject, out parent);
    }
}

public readonly record struct MarkdownSourceSpan(int Start, int Length)
{
    public static MarkdownSourceSpan Empty { get; } = new(-1, 0);

    public int End => Length > 0 ? Start + Length - 1 : Start;

    public int EndExclusive => Length > 0 ? Start + Length : Start;

    public bool IsEmpty => Start < 0 || Length <= 0;

    public bool Contains(int position) => !IsEmpty && position >= Start && position < EndExclusive;

    public string Slice(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (IsEmpty || Start >= sourceText.Length)
        {
            return string.Empty;
        }

        var start = Math.Max(Start, 0);
        var length = Math.Min(Length, sourceText.Length - start);
        return length <= 0 ? string.Empty : sourceText.Substring(start, length);
    }

    public static MarkdownSourceSpan FromMarkdig(SourceSpan sourceSpan)
    {
        return sourceSpan.IsEmpty ? Empty : new MarkdownSourceSpan(sourceSpan.Start, sourceSpan.Length);
    }
}

public enum MarkdownEditorFeature
{
    Paragraph,
    Heading,
    TextStyle,
    List,
    Table,
    YamlFrontMatter,
    Alert,
    CustomContainer,
    Figure,
    DefinitionList,
    Abbreviation,
    Footer,
    Code,
    LinkReference,
    Footnote,
    Math,
    Mermaid
}

public enum MarkdownEditorPresentationMode
{
    Inline,
    Card
}

public sealed class MarkdownEditorPreferences
{
    private readonly Dictionary<MarkdownEditorFeature, string> _preferredEditors = [];

    public IReadOnlyDictionary<MarkdownEditorFeature, string> PreferredEditors => _preferredEditors;

    public MarkdownEditorPreferences PreferEditor(MarkdownEditorFeature feature, string editorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorId);
        _preferredEditors[feature] = editorId;
        return this;
    }

    public MarkdownEditorPreferences ClearPreference(MarkdownEditorFeature feature)
    {
        _preferredEditors.Remove(feature);
        return this;
    }

    public bool TryGetPreferredEditor(MarkdownEditorFeature feature, out string? editorId)
    {
        return _preferredEditors.TryGetValue(feature, out editorId);
    }

    public MarkdownEditorPreferences Clone()
    {
        var clone = new MarkdownEditorPreferences();
        foreach (var pair in _preferredEditors)
        {
            clone._preferredEditors[pair.Key] = pair.Value;
        }

        return clone;
    }
}

public sealed class MarkdownBlockTemplate(
    string templateId,
    string label,
    MarkdownEditorFeature feature,
    string markdown,
    string? description = null)
{
    public string TemplateId { get; } = templateId;

    public string Label { get; } = label;

    public MarkdownEditorFeature Feature { get; } = feature;

    public string Markdown { get; } = markdown;

    public string? Description { get; } = description;
}

public readonly record struct MarkdownTextStyle(
    bool Bold = false,
    bool Italic = false,
    bool Strikethrough = false,
    bool Underline = false,
    bool Code = false,
    bool Marked = false,
    bool Inserted = false,
    bool Superscript = false,
    bool Subscript = false,
    string? Foreground = null);

public sealed record MarkdownInlineFragment(
    string Text,
    MarkdownTextStyle Style,
    MarkdownSourceSpan SourceSpan,
    string? SemanticClass = null,
    string? LinkTarget = null,
    bool IsAtomic = false,
    double ExtraWidth = 0);

public sealed record MarkdownInlineFlow(
    IReadOnlyList<MarkdownInlineFragment> Fragments,
    MarkdownSourceSpan SourceSpan);

public abstract record MarkdownBlockNode(MarkdownSourceSpan SourceSpan);

public sealed record MarkdownParagraphBlock(
    IReadOnlyList<MarkdownInlineFlow> Flows,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownHeadingBlock(
    int Level,
    IReadOnlyList<MarkdownInlineFlow> Flows,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownQuoteBlock(
    IReadOnlyList<MarkdownBlockNode> Blocks,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownListItemNode(
    string Marker,
    IReadOnlyList<MarkdownBlockNode> Blocks,
    MarkdownSourceSpan SourceSpan);

public sealed record MarkdownListBlock(
    bool Ordered,
    IReadOnlyList<MarkdownListItemNode> Items,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownCodeBlock(
    string Code,
    string? LanguageHint,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownYamlFrontMatterBlock(
    string Yaml,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownRuleBlock(MarkdownSourceSpan SourceSpan) : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownHtmlBlock(
    string Html,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownTableRowNode(
    bool IsHeader,
    IReadOnlyList<string> Cells);

public sealed record MarkdownTableBlock(
    IReadOnlyList<MarkdownTableRowNode> Rows,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownAlertBlock(
    string Kind,
    string Title,
    string BodyMarkdown,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownCustomContainerBlock(
    string Kind,
    string Title,
    string? Arguments,
    string BodyMarkdown,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownDefinitionTermNode(
    string Markdown,
    string PlainText);

public sealed record MarkdownDefinitionItemNode(
    IReadOnlyList<MarkdownDefinitionTermNode> Terms,
    string DefinitionMarkdown);

public sealed record MarkdownDefinitionListBlock(
    IReadOnlyList<MarkdownDefinitionItemNode> Items,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownFigureBlock(
    string BodyMarkdown,
    string? LeadingCaptionMarkdown,
    string? TrailingCaptionMarkdown,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownLinkReferenceBlock(
    string Label,
    string Url,
    string? Title,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownAbbreviationBlock(
    string Label,
    string Meaning,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownFootnoteBlock(
    string Label,
    int Order,
    string BodyMarkdown,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownFooterBlock(
    string BodyMarkdown,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownMathBlock(
    string Expression,
    bool Display,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public enum MarkdownMermaidBlockSyntax
{
    CodeFence,
    CustomContainer
}

public sealed record MarkdownMermaidBlock(
    string DiagramSource,
    string? Descriptor,
    string? Arguments,
    MarkdownMermaidBlockSyntax Syntax,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownFallbackBlock(
    string Title,
    string BodyMarkdown,
    MarkdownSourceSpan SourceSpan)
    : MarkdownBlockNode(SourceSpan);

public sealed record MarkdownDocumentModel(
    MarkdownParseResult ParseResult,
    IReadOnlyList<MarkdownBlockNode> Blocks);

public sealed record MarkdownStyledTextRun(
    string Text,
    MarkdownTextStyle Style,
    string? Scope = null);

public sealed record MarkdownCodeHighlightResult(
    string Engine,
    IReadOnlyList<MarkdownStyledTextRun> Runs);

public sealed record MarkdownHitRegion(
    double X,
    double Y,
    double Width,
    double Height,
    MarkdownSourceSpan SourceSpan,
    string? LinkTarget,
    string Kind)
{
    public bool Contains(double x, double y)
    {
        return x >= X && y >= Y && x <= X + Width && y <= Y + Height;
    }
}

public abstract record MarkdownBlockLayout(
    MarkdownBlockNode Block,
    double Top,
    double Height);

public sealed record MarkdownInlineLayoutFragment(
    string LeadingWhitespace,
    string Text,
    MarkdownTextStyle Style,
    MarkdownSourceSpan SourceSpan,
    string? SemanticClass,
    string? LinkTarget,
    double GapBefore,
    double X,
    double Width);

public sealed record MarkdownInlineLayoutLine(
    string Text,
    double Width,
    IReadOnlyList<MarkdownInlineLayoutFragment> Fragments);

public sealed record MarkdownInlineBlockLayout(
    MarkdownBlockNode Block,
    double Top,
    double Height,
    double LineHeight,
    IReadOnlyList<MarkdownInlineLayoutLine> Lines)
    : MarkdownBlockLayout(Block, Top, Height);

public sealed record MarkdownCodeLayoutLine(
    string Text,
    double Width,
    IReadOnlyList<MarkdownStyledTextRun> Runs);

public sealed record MarkdownCodeBlockLayout(
    MarkdownBlockNode Block,
    double Top,
    double Height,
    double LineHeight,
    string Engine,
    IReadOnlyList<MarkdownCodeLayoutLine> Lines)
    : MarkdownBlockLayout(Block, Top, Height)
{
    public MarkdownCodeBlock CodeBlock => (MarkdownCodeBlock)Block;
}

public sealed record MarkdownRuleBlockLayout(
    MarkdownBlockNode Block,
    double Top,
    double Height)
    : MarkdownBlockLayout(Block, Top, Height)
{
    public MarkdownRuleBlock RuleBlock => (MarkdownRuleBlock)Block;
}

public sealed record MarkdownSummaryBlockLayout(
    MarkdownBlockNode Block,
    double Top,
    double Height,
    string Summary)
    : MarkdownBlockLayout(Block, Top, Height);

public sealed class MarkdownLayoutDocument(
    double width,
    double height,
    IReadOnlyList<MarkdownBlockLayout> blocks,
    IReadOnlyList<MarkdownHitRegion> hitRegions)
{
    public double Width { get; } = width;

    public double Height { get; } = height;

    public IReadOnlyList<MarkdownBlockLayout> Blocks { get; } = blocks;

    public IReadOnlyList<MarkdownHitRegion> HitRegions { get; } = hitRegions;

    public MarkdownHitRegion? HitTest(double x, double y)
    {
        foreach (var hitRegion in HitRegions)
        {
            if (hitRegion.Contains(x, y))
            {
                return hitRegion;
            }
        }

        return null;
    }
}
