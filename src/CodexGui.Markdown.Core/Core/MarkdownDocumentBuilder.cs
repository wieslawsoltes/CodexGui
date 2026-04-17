using System.Globalization;
using System.Net;
using System.Text;
using CodexGui.Markdown.Core;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Emoji;
using Markdig.Extensions.Figures;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Footers;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.SmartyPants;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CodexGui.Markdown.Core;

public sealed class MarkdownDocumentBuilder
{
    private static readonly IReadOnlyDictionary<SmartyPantType, string> SmartyPantMapping =
        new Dictionary<SmartyPantType, string>(
            new SmartyPantOptions()
                .Mapping
                .ToDictionary(static pair => pair.Key, static pair => WebUtility.HtmlDecode(pair.Value)));

    private readonly MarkdownPluginRegistry _registry;
    private readonly IMarkdownParsingService _parsingService;

    public MarkdownDocumentBuilder(MarkdownPluginRegistry? registry = null)
    {
        _registry = registry ?? MarkdownRuntimeConfiguration.Snapshot();
        _parsingService = new MarkdownParsingService(_registry.ParserPlugins);
    }

    public MarkdownDocumentModel Build(string? markdown)
    {
        var parseResult = _parsingService.Parse(markdown ?? string.Empty);
        return Build(parseResult);
    }

    public MarkdownDocumentModel Build(MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        var blocks = new List<MarkdownBlockNode>();
        foreach (var block in parseResult.Document)
        {
            if (block is FootnoteGroup footnoteGroup)
            {
                blocks.AddRange(BuildFootnoteBlocks(footnoteGroup, parseResult));
                continue;
            }

            if (BuildBlock(block, parseResult) is { } node)
            {
                blocks.Add(node);
            }
        }

        AppendDeferredMetadataBlocks(parseResult, blocks);

        return new MarkdownDocumentModel(parseResult, blocks);
    }

    private MarkdownBlockNode? BuildBlock(Block block, MarkdownParseResult parseResult)
    {
        return block switch
        {
            BlankLineBlock => null,
            HeadingBlock headingBlock => new MarkdownHeadingBlock(
                Math.Clamp(headingBlock.Level, 1, 6),
                BuildFlows(headingBlock.Inline, parseResult),
                MarkdownSourceSpan.FromMarkdig(headingBlock.Span)),
            ParagraphBlock paragraphBlock => new MarkdownParagraphBlock(
                BuildFlows(paragraphBlock.Inline, parseResult),
                MarkdownSourceSpan.FromMarkdig(paragraphBlock.Span)),
            AlertBlock alertBlock => BuildAlertBlock(alertBlock, parseResult),
            QuoteBlock quoteBlock => new MarkdownQuoteBlock(
                BuildBlocks(quoteBlock, parseResult),
                MarkdownSourceSpan.FromMarkdig(quoteBlock.Span)),
            ListBlock listBlock => BuildListBlock(listBlock, parseResult),
            FencedCodeBlock fencedCodeBlock when IsMermaidBlock(fencedCodeBlock) => BuildMermaidBlock(fencedCodeBlock),
            YamlFrontMatterBlock yamlFrontMatterBlock => new MarkdownYamlFrontMatterBlock(
                NormalizeCode(yamlFrontMatterBlock.Lines.ToString()),
                MarkdownSourceSpan.FromMarkdig(yamlFrontMatterBlock.Span)),
            MathBlock mathBlock => new MarkdownMathBlock(
                MarkdownSourceEditing.NormalizeBlockText(mathBlock.Lines.ToString()),
                Display: true,
                MarkdownSourceSpan.FromMarkdig(mathBlock.Span)),
            CodeBlock codeBlock => new MarkdownCodeBlock(
                NormalizeCode(codeBlock.Lines.ToString()),
                codeBlock is FencedCodeBlock fencedCode ? MarkdownSourceEditing.NormalizeLanguageHint(fencedCode.Info) : null,
                MarkdownSourceSpan.FromMarkdig(codeBlock.Span)),
            ThematicBreakBlock thematicBreakBlock => new MarkdownRuleBlock(MarkdownSourceSpan.FromMarkdig(thematicBreakBlock.Span)),
            HtmlBlock htmlBlock => new MarkdownHtmlBlock(GetSource(parseResult, htmlBlock), MarkdownSourceSpan.FromMarkdig(htmlBlock.Span)),
            Table table => BuildTableBlock(table, parseResult),
            CustomContainer customContainer => BuildCustomContainerBlock(customContainer, parseResult),
            DefinitionList definitionList => BuildDefinitionListBlock(definitionList, parseResult),
            Figure figure => BuildFigureBlock(figure, parseResult),
            FootnoteGroup => null,
            LinkReferenceDefinitionGroup => null,
            FooterBlock footerBlock => new MarkdownFooterBlock(
                NormalizeBodyMarkdown(GetInnerSource(footerBlock, parseResult)),
                MarkdownSourceSpan.FromMarkdig(footerBlock.Span)),
            _ => BuildFallbackBlock(block, parseResult)
        };
    }

    private IReadOnlyList<MarkdownBlockNode> BuildBlocks(ContainerBlock block, MarkdownParseResult parseResult)
    {
        var blocks = new List<MarkdownBlockNode>();
        foreach (var child in block)
        {
            if (BuildBlock(child, parseResult) is { } node)
            {
                blocks.Add(node);
            }
        }

        return blocks;
    }

    private MarkdownListBlock BuildListBlock(ListBlock listBlock, MarkdownParseResult parseResult)
    {
        var items = new List<MarkdownListItemNode>();
        var ordinal = 1;
        foreach (var child in listBlock)
        {
            if (child is not ListItemBlock listItem)
            {
                continue;
            }

            var blocks = new List<MarkdownBlockNode>();
            foreach (var itemChild in listItem)
            {
                if (itemChild is BlankLineBlock)
                {
                    continue;
                }

                if (BuildBlock(itemChild, parseResult) is { } itemNode)
                {
                    blocks.Add(itemNode);
                }
            }

            var marker = listBlock.IsOrdered ? $"{ordinal}." : "•";
            items.Add(new MarkdownListItemNode(
                marker,
                blocks,
                MarkdownSourceSpan.FromMarkdig(listItem.Span)));
            ordinal++;
        }

        return new MarkdownListBlock(
            listBlock.IsOrdered,
            items,
            MarkdownSourceSpan.FromMarkdig(listBlock.Span));
    }

    private static MarkdownTableBlock BuildTableBlock(Table table, MarkdownParseResult parseResult)
    {
        var rows = new List<MarkdownTableRowNode>();
        foreach (var rowObject in table)
        {
            if (rowObject is not TableRow row)
            {
                continue;
            }

            var cells = new List<string>();
            foreach (var cellObject in row)
            {
                if (cellObject is not TableCell cell)
                {
                    continue;
                }

                var source = MarkdownSourceSpan.FromMarkdig(cell.Span).Slice(parseResult.OriginalMarkdown);
                if (string.IsNullOrWhiteSpace(source))
                {
                    source = cell.ToString();
                }

                cells.Add(MarkdownSourceEditing.NormalizeBlockText(source));
            }

            rows.Add(new MarkdownTableRowNode(row.IsHeader, cells));
        }

        return new MarkdownTableBlock(rows, MarkdownSourceSpan.FromMarkdig(table.Span));
    }

    private static MarkdownAlertBlock BuildAlertBlock(AlertBlock alertBlock, MarkdownParseResult parseResult)
    {
        var source = GetSource(parseResult, alertBlock);
        var normalized = MarkdownSourceEditing.NormalizeLineEndings(source);
        var firstNewLine = normalized.IndexOf('\n');
        var body = firstNewLine >= 0 && firstNewLine + 1 < normalized.Length
            ? normalized[(firstNewLine + 1)..]
            : string.Empty;

        var unwrappedBody = new StringBuilder(body.Length);
        foreach (var line in body.Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('>'))
            {
                trimmed = trimmed.Length > 1 && trimmed[1] == ' ' ? trimmed[2..] : trimmed[1..];
            }

            unwrappedBody.Append(trimmed);
            unwrappedBody.Append('\n');
        }

        var kind = alertBlock.Kind.ToString().ToLowerInvariant();
        return new MarkdownAlertBlock(
            kind,
            CultureInfoInvariant(kind),
            MarkdownSourceEditing.NormalizeBlockText(unwrappedBody.ToString()),
            MarkdownSourceSpan.FromMarkdig(alertBlock.Span));
    }

    private static MarkdownCustomContainerBlock BuildCustomContainerBlock(CustomContainer customContainer, MarkdownParseResult parseResult)
    {
        var title = MarkdownSourceEditing.NormalizeInlineText(customContainer.Info);
        var subtitle = MarkdownSourceEditing.NormalizeInlineText(customContainer.Arguments);
        var label = title.Length == 0 ? "Container" : CultureInfoInvariant(title);

        return new MarkdownCustomContainerBlock(
            title.Length == 0 ? "container" : title.ToLowerInvariant(),
            label,
            subtitle.Length == 0 ? null : subtitle,
            NormalizeBodyMarkdown(GetInnerSource(customContainer, parseResult)),
            MarkdownSourceSpan.FromMarkdig(customContainer.Span));
    }

    private static MarkdownDefinitionListBlock BuildDefinitionListBlock(DefinitionList definitionList, MarkdownParseResult parseResult)
    {
        var items = new List<MarkdownDefinitionItemNode>();
        foreach (var child in definitionList)
        {
            if (child is not DefinitionItem definitionItem)
            {
                continue;
            }

            var terms = new List<MarkdownDefinitionTermNode>();
            var definitions = new List<string>();
            foreach (var itemChild in definitionItem)
            {
                switch (itemChild)
                {
                    case DefinitionTerm term:
                        var termSource = MarkdownSourceSpan.FromMarkdig(term.Span).Slice(parseResult.OriginalMarkdown);
                        if (string.IsNullOrWhiteSpace(termSource))
                        {
                            termSource = ExtractInlineText(term.Inline);
                        }

                        var normalizedTerm = MarkdownSourceEditing.NormalizeInlineMarkdown(termSource);
                        if (normalizedTerm.Length > 0)
                        {
                            terms.Add(new MarkdownDefinitionTermNode(
                                normalizedTerm,
                                MarkdownSourceEditing.NormalizeInlineText(ExtractInlineText(term.Inline))));
                        }

                        break;
                    case BlankLineBlock:
                        break;
                    default:
                        definitions.Add(GetSource(parseResult, itemChild));
                        break;
                }
            }

            items.Add(new MarkdownDefinitionItemNode(
                terms,
                MarkdownSourceEditing.NormalizeBlockText(string.Join("\n", definitions.Where(static x => !string.IsNullOrWhiteSpace(x))))));
        }

        return new MarkdownDefinitionListBlock(items, MarkdownSourceSpan.FromMarkdig(definitionList.Span));
    }

    private static MarkdownFigureBlock BuildFigureBlock(Figure figure, MarkdownParseResult parseResult)
    {
        var bodyBlocks = new List<string>();
        string? leadingCaption = null;
        string? trailingCaption = null;
        var encounteredBody = false;
        foreach (var child in figure)
        {
            if (child is FigureCaption captionBlock)
            {
                var captionSource = GetSource(parseResult, captionBlock);
                if (string.IsNullOrWhiteSpace(captionSource))
                {
                    captionSource = captionBlock.Lines.ToString();
                }

                var normalizedCaption = MarkdownSourceEditing.NormalizeInlineMarkdown(captionSource);
                if (normalizedCaption.Length == 0)
                {
                    continue;
                }

                if (!encounteredBody && leadingCaption is null)
                {
                    leadingCaption = normalizedCaption;
                    continue;
                }

                trailingCaption ??= normalizedCaption;
                continue;
            }

            if (child is BlankLineBlock)
            {
                continue;
            }

            encounteredBody = true;
            bodyBlocks.Add(GetSource(parseResult, child));
        }

        return new MarkdownFigureBlock(
            MarkdownSourceEditing.NormalizeBlockText(string.Join("\n\n", bodyBlocks.Where(static x => !string.IsNullOrWhiteSpace(x)))),
            leadingCaption,
            trailingCaption,
            MarkdownSourceSpan.FromMarkdig(figure.Span));
    }

    private static IReadOnlyList<MarkdownFootnoteBlock> BuildFootnoteBlocks(FootnoteGroup footnoteGroup, MarkdownParseResult parseResult)
    {
        var blocks = new List<MarkdownFootnoteBlock>();
        foreach (var footnote in footnoteGroup.OfType<Footnote>().OrderBy(static footnote => footnote.Order))
        {
            blocks.Add(new MarkdownFootnoteBlock(
                string.IsNullOrWhiteSpace(footnote.Label)
                    ? footnote.Order.ToString(CultureInfo.InvariantCulture)
                    : footnote.Label.Trim().TrimStart('^'),
                footnote.Order,
                BuildFootnoteBodyMarkdown(footnote, parseResult),
                MarkdownSourceSpan.FromMarkdig(footnote.Span)));
        }

        return blocks;
    }

    private static string BuildFootnoteBodyMarkdown(Footnote footnote, MarkdownParseResult parseResult)
    {
        var blocks = new List<string>();
        foreach (var child in footnote)
        {
            if (child is BlankLineBlock)
            {
                continue;
            }

            var source = GetSource(parseResult, child);
            if (!string.IsNullOrWhiteSpace(source))
            {
                blocks.Add(source);
            }
        }

        return MarkdownSourceEditing.NormalizeBlockText(string.Join("\n\n", blocks));
    }

    private static void AppendDeferredMetadataBlocks(MarkdownParseResult parseResult, List<MarkdownBlockNode> blocks)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(blocks);

        blocks.AddRange(GetUserLinkReferenceDefinitions(parseResult.Document)
            .Select(static definition => new MarkdownLinkReferenceBlock(
                definition.Label ?? string.Empty,
                definition.Url ?? string.Empty,
                definition.Title,
                MarkdownSourceSpan.FromMarkdig(definition.Span))));

        blocks.AddRange(GetDocumentAbbreviations(parseResult.Document)
            .Select(static abbreviation => new MarkdownAbbreviationBlock(
                abbreviation.Label ?? string.Empty,
                abbreviation.Text.ToString(),
                MarkdownSourceSpan.FromMarkdig(abbreviation.Span))));
    }

    private static List<LinkReferenceDefinition> GetUserLinkReferenceDefinitions(MarkdownDocument document)
    {
        return document
            .OfType<LinkReferenceDefinitionGroup>()
            .SelectMany(static group => group.Links.Values)
            .Where(static definition => definition.GetType() == typeof(LinkReferenceDefinition) && !string.IsNullOrWhiteSpace(definition.Label))
            .OrderBy(static definition => definition.Span.Start)
            .ToList();
    }

    private static List<Abbreviation> GetDocumentAbbreviations(MarkdownDocument document)
    {
        var definitions = AbbreviationHelper.GetAbbreviations(document);
        return definitions is null
            ? []
            : definitions.Values
                .Where(static abbreviation => !string.IsNullOrWhiteSpace(abbreviation.Label) && abbreviation.Span.Start >= 0)
                .OrderBy(static abbreviation => abbreviation.Span.Start)
                .ToList();
    }

    private static MarkdownFallbackBlock BuildFallbackBlock(Block block, MarkdownParseResult parseResult)
    {
        var source = GetSource(parseResult, block);
        return new MarkdownFallbackBlock(
            block.GetType().Name,
            MarkdownSourceEditing.NormalizeBlockText(source),
            MarkdownSourceSpan.FromMarkdig(block.Span));
    }

    private static IReadOnlyList<MarkdownInlineFlow> BuildFlows(ContainerInline? inline, MarkdownParseResult parseResult)
    {
        var flows = new List<List<MarkdownInlineFragment>>
        {
            new()
        };

        AppendInlineChildren(inline, parseResult, flows, new MarkdownTextStyle(), linkTarget: null, semanticClass: null);

        return flows
            .Where(static flow => flow.Count > 0)
            .Select(flow =>
            {
                var start = flow[0].SourceSpan.Start;
                var end = flow[^1].SourceSpan.EndExclusive;
                var span = start < 0 || end <= start ? MarkdownSourceSpan.Empty : new MarkdownSourceSpan(start, end - start);
                return new MarkdownInlineFlow(flow, span);
            })
            .ToArray();
    }

    private static void AppendInlineChildren(
        ContainerInline? container,
        MarkdownParseResult parseResult,
        List<List<MarkdownInlineFragment>> flows,
        MarkdownTextStyle style,
        string? linkTarget,
        string? semanticClass)
    {
        if (container is null)
        {
            return;
        }

        HashSet<Inline> visitedSiblings = [];
        for (Inline? current = container.FirstChild; current is not null && visitedSiblings.Add(current); current = current.NextSibling)
        {
            AppendInline(current, parseResult, flows, style, linkTarget, semanticClass);
        }
    }

    private static void AppendInline(
        Inline inline,
        MarkdownParseResult parseResult,
        List<List<MarkdownInlineFragment>> flows,
        MarkdownTextStyle style,
        string? linkTarget,
        string? semanticClass)
    {
        switch (inline)
        {
            case EmojiInline emojiInline:
                AddFragment(flows, new MarkdownInlineFragment(
                    emojiInline.ToString(),
                    style,
                    MarkdownSourceSpan.FromMarkdig(emojiInline.Span),
                    semanticClass,
                    linkTarget));
                break;
            case LiteralInline literalInline:
                AddFragment(flows, new MarkdownInlineFragment(
                    literalInline.Content.ToString(),
                    style,
                    MarkdownSourceSpan.FromMarkdig(literalInline.Span),
                    semanticClass,
                    linkTarget));
                break;
            case CodeInline codeInline:
                AddFragment(flows, new MarkdownInlineFragment(
                    codeInline.Content.ToString(),
                    style with { Code = true, Foreground = "#8B1E3F" },
                    MarkdownSourceSpan.FromMarkdig(codeInline.Span),
                    SemanticClass: "code",
                    LinkTarget: linkTarget,
                    IsAtomic: true,
                    ExtraWidth: 12));
                break;
            case LineBreakInline:
                EnsureNewFlow(flows);
                break;
            case HtmlEntityInline htmlEntityInline:
                AddFragment(flows, new MarkdownInlineFragment(
                    htmlEntityInline.Transcoded.ToString(),
                    style,
                    MarkdownSourceSpan.FromMarkdig(htmlEntityInline.Span),
                    semanticClass,
                    linkTarget));
                break;
            case EmphasisInline emphasisInline:
                var nestedStyle = style with
                {
                    Bold = style.Bold || IsBoldEmphasis(emphasisInline),
                    Italic = style.Italic || IsItalicEmphasis(emphasisInline),
                    Strikethrough = style.Strikethrough || IsStrikethroughEmphasis(emphasisInline),
                    Underline = style.Underline || IsUnderlineEmphasis(emphasisInline),
                    Marked = style.Marked || IsMarkedEmphasis(emphasisInline),
                    Inserted = style.Inserted || IsInsertedEmphasis(emphasisInline),
                    Superscript = style.Superscript || IsSuperscriptEmphasis(emphasisInline),
                    Subscript = style.Subscript || IsSubscriptEmphasis(emphasisInline)
                };
                AppendInlineChildren(emphasisInline, parseResult, flows, nestedStyle, linkTarget, semanticClass);
                break;
            case EmphasisDelimiterInline emphasisDelimiter:
                AppendEmphasisDelimiterInline(emphasisDelimiter, parseResult, flows, style, linkTarget, semanticClass);
                break;
            case LinkInline linkInline when linkInline.IsImage:
                var altText = ExtractInlineText(linkInline);
                var imageText = string.IsNullOrWhiteSpace(altText) ? "[image]" : $"[image: {altText}]";
                AddFragment(flows, new MarkdownInlineFragment(
                    imageText,
                    style with { Bold = true, Foreground = "#475569" },
                    MarkdownSourceSpan.FromMarkdig(linkInline.Span),
                    SemanticClass: "image",
                    LinkTarget: ResolveLinkTarget(linkInline),
                    IsAtomic: true,
                    ExtraWidth: 14));
                break;
            case LinkInline linkInline:
                AppendInlineChildren(linkInline, parseResult, flows, style with { Underline = true }, ResolveLinkTarget(linkInline), "link");
                break;
            case AutolinkInline autolinkInline:
                AddFragment(flows, new MarkdownInlineFragment(
                    autolinkInline.Url,
                    style with { Underline = true, Foreground = "#2563EB" },
                    MarkdownSourceSpan.FromMarkdig(autolinkInline.Span),
                    SemanticClass: "link",
                    LinkTarget: autolinkInline.IsEmail ? $"mailto:{autolinkInline.Url}" : autolinkInline.Url));
                break;
            case AbbreviationInline abbreviationInline:
                var abbreviationText = abbreviationInline.Abbreviation?.Label ?? GetInlineText(abbreviationInline, parseResult);
                if (abbreviationText.Length > 0)
                {
                    AddFragment(flows, new MarkdownInlineFragment(
                        abbreviationText,
                        style with { Underline = true, Bold = true, Foreground = "#0F766E" },
                        MarkdownSourceSpan.FromMarkdig(abbreviationInline.Span),
                        SemanticClass: "abbreviation",
                        LinkTarget: linkTarget,
                        IsAtomic: true,
                        ExtraWidth: 2));
                }

                break;
            case ContainerInline containerInline:
                AppendInlineChildren(containerInline, parseResult, flows, style, linkTarget, semanticClass);
                break;
            case MathInline mathInline:
                AddFragment(flows, new MarkdownInlineFragment(
                    MarkdownSourceEditing.NormalizeInlineText(mathInline.Content.ToString()),
                    style with { Italic = true, Foreground = "#4C1D95" },
                    MarkdownSourceSpan.FromMarkdig(mathInline.Span),
                    SemanticClass: "math",
                    LinkTarget: linkTarget,
                    IsAtomic: true,
                    ExtraWidth: 8));
                break;
            case FootnoteLink footnoteLink:
                AddFragment(flows, new MarkdownInlineFragment(
                    footnoteLink.IsBackLink ? "↩" : $"[{footnoteLink.Index}]",
                    style with
                    {
                        Bold = true,
                        Superscript = !footnoteLink.IsBackLink,
                        Foreground = "#2563EB"
                    },
                    MarkdownSourceSpan.FromMarkdig(footnoteLink.Span),
                    SemanticClass: "footnote",
                    LinkTarget: linkTarget,
                    IsAtomic: true,
                    ExtraWidth: 4));
                break;
            case SmartyPant smartyPant:
                AddFragment(flows, new MarkdownInlineFragment(
                    ResolveSmartyPantText(smartyPant),
                    style,
                    MarkdownSourceSpan.FromMarkdig(smartyPant.Span),
                    semanticClass,
                    linkTarget));
                break;
            default:
                var fallbackText = GetInlineText(inline, parseResult);
                if (fallbackText.Length > 0)
                {
                    AddFragment(flows, new MarkdownInlineFragment(
                        fallbackText,
                        style,
                        MarkdownSourceSpan.FromMarkdig(inline.Span),
                        semanticClass,
                        linkTarget));
                }

                break;
        }
    }

    private static void AppendEmphasisDelimiterInline(
        EmphasisDelimiterInline emphasisDelimiter,
        MarkdownParseResult parseResult,
        List<List<MarkdownInlineFragment>> flows,
        MarkdownTextStyle style,
        string? linkTarget,
        string? semanticClass)
    {
        var text = ExtractInlineText(emphasisDelimiter);
        if (text.Length == 0)
        {
            text = GetInlineText(emphasisDelimiter, parseResult);
        }

        if (text.Length == 0)
        {
            return;
        }

        var delimiterStyle = emphasisDelimiter.DelimiterChar switch
        {
            '^' when emphasisDelimiter.DelimiterCount >= 2 => style with { Superscript = true },
            '~' when emphasisDelimiter.DelimiterCount == 1 => style with { Subscript = true },
            '=' when emphasisDelimiter.DelimiterCount >= 2 => style with { Marked = true },
            '+' when emphasisDelimiter.DelimiterCount >= 2 => style with { Inserted = true, Underline = true },
            _ => style
        };

        AddFragment(flows, new MarkdownInlineFragment(
            text,
            delimiterStyle,
            MarkdownSourceSpan.FromMarkdig(emphasisDelimiter.Span),
            semanticClass,
            linkTarget,
            IsAtomic: delimiterStyle.Marked || delimiterStyle.Inserted || delimiterStyle.Superscript || delimiterStyle.Subscript,
            ExtraWidth: 4));
    }

    private static bool IsBoldEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar is '*' or '_' && emphasisInline.DelimiterCount >= 2;
    }

    private static bool IsItalicEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar is '*' or '_' && emphasisInline.DelimiterCount == 1;
    }

    private static bool IsStrikethroughEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar == '~' && emphasisInline.DelimiterCount >= 2;
    }

    private static bool IsUnderlineEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar == '+' && emphasisInline.DelimiterCount >= 2;
    }

    private static bool IsMarkedEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar == '=' && emphasisInline.DelimiterCount >= 2;
    }

    private static bool IsInsertedEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar == '+' && emphasisInline.DelimiterCount >= 2;
    }

    private static bool IsSuperscriptEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar == '^' && emphasisInline.DelimiterCount >= 2;
    }

    private static bool IsSubscriptEmphasis(EmphasisInline emphasisInline)
    {
        return emphasisInline.DelimiterChar == '~' && emphasisInline.DelimiterCount == 1;
    }

    private static string? ResolveLinkTarget(LinkInline linkInline)
    {
        return linkInline.GetDynamicUrl?.Invoke() ?? linkInline.Url;
    }

    private static void AddFragment(List<List<MarkdownInlineFragment>> flows, MarkdownInlineFragment fragment)
    {
        if (string.IsNullOrEmpty(fragment.Text))
        {
            return;
        }

        flows[^1].Add(fragment);
    }

    private static void EnsureNewFlow(List<List<MarkdownInlineFragment>> flows)
    {
        if (flows[^1].Count == 0)
        {
            return;
        }

        flows.Add([]);
    }

    private static bool IsMermaidBlock(FencedCodeBlock fencedCodeBlock)
    {
        var language = MarkdownSourceEditing.NormalizeLanguageHint(fencedCodeBlock.Info);
        if (language is "mermaid" or "mmd" or "diagram-mermaid" or "mermaidjs")
        {
            return true;
        }

        return fencedCodeBlock.GetType().FullName == "CodexGui.Markdown.Plugin.Mermaid.MermaidDiagramBlock";
    }

    private static MarkdownMermaidBlock BuildMermaidBlock(FencedCodeBlock fencedCodeBlock)
    {
        var descriptor = MarkdownSourceEditing.NormalizeLanguageHint(fencedCodeBlock.Info);
        var arguments = string.Empty;
        var syntax = MarkdownMermaidBlockSyntax.CodeFence;

        if (TryResolveMermaidMetadata(fencedCodeBlock, out var normalizedInfo, out var diagramArguments, out var mermaidSyntax))
        {
            descriptor = normalizedInfo;
            arguments = diagramArguments;
            syntax = mermaidSyntax;
        }

        return new MarkdownMermaidBlock(
            NormalizeCode(fencedCodeBlock.Lines.ToString()),
            string.IsNullOrWhiteSpace(descriptor) ? null : descriptor,
            string.IsNullOrWhiteSpace(arguments) ? null : arguments,
            syntax,
            MarkdownSourceSpan.FromMarkdig(fencedCodeBlock.Span));
    }

    private static bool TryResolveMermaidMetadata(
        FencedCodeBlock fencedCodeBlock,
        out string normalizedInfo,
        out string diagramArguments,
        out MarkdownMermaidBlockSyntax syntax)
    {
        normalizedInfo = MarkdownSourceEditing.NormalizeLanguageHint(fencedCodeBlock.Info);
        diagramArguments = string.Empty;
        syntax = MarkdownMermaidBlockSyntax.CodeFence;

        var blockType = fencedCodeBlock.GetType();
        if (!string.Equals(blockType.FullName, "CodexGui.Markdown.Plugin.Mermaid.MermaidDiagramBlock", StringComparison.Ordinal))
        {
            return false;
        }

        normalizedInfo = blockType.GetProperty("NormalizedInfo")?.GetValue(fencedCodeBlock) as string ?? normalizedInfo;
        diagramArguments = blockType.GetProperty("DiagramArguments")?.GetValue(fencedCodeBlock) as string ?? string.Empty;
        var syntaxValue = blockType.GetProperty("Syntax")?.GetValue(fencedCodeBlock)?.ToString();
        syntax = string.Equals(syntaxValue, "CustomContainer", StringComparison.Ordinal)
            ? MarkdownMermaidBlockSyntax.CustomContainer
            : MarkdownMermaidBlockSyntax.CodeFence;
        return true;
    }

    private static string ResolveSmartyPantText(SmartyPant smartyPant)
    {
        return SmartyPantMapping.TryGetValue(smartyPant.Type, out var text)
            ? text
            : smartyPant.ToString();
    }

    private static string NormalizeCode(string? source)
    {
        return MarkdownSourceEditing.NormalizeLineEndings(source).TrimEnd('\n');
    }

    private static string NormalizeBodyMarkdown(string source)
    {
        return MarkdownSourceEditing.NormalizeBlockText(source);
    }

    private static string GetSource(MarkdownParseResult parseResult, MarkdownObject markdownObject)
    {
        var span = MarkdownSourceSpan.FromMarkdig(markdownObject.Span);
        var source = span.Slice(parseResult.OriginalMarkdown);
        if (string.IsNullOrWhiteSpace(source) && !parseResult.UsesOriginalSourceSpans)
        {
            source = span.Slice(parseResult.ParsedMarkdown);
        }

        return source;
    }

    private static string GetInnerSource(ContainerBlock block, MarkdownParseResult parseResult)
    {
        var builder = new StringBuilder();
        foreach (var child in block)
        {
            if (child is BlankLineBlock)
            {
                continue;
            }

            var source = GetSource(parseResult, child);
            if (source.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }

            builder.Append(source);
        }

        return builder.ToString();
    }

    private static string ExtractInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        HashSet<Inline> visitedSiblings = [];
        for (Inline? current = container.FirstChild; current is not null && visitedSiblings.Add(current); current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literalInline:
                    builder.Append(literalInline.Content.ToString());
                    break;
                case CodeInline codeInline:
                    builder.Append(codeInline.Content);
                    break;
                case HtmlEntityInline htmlEntityInline:
                    builder.Append(htmlEntityInline.Transcoded);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline nested:
                    builder.Append(ExtractInlineText(nested));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string GetInlineText(Inline inline, MarkdownParseResult parseResult)
    {
        var source = MarkdownSourceSpan.FromMarkdig(inline.Span).Slice(parseResult.OriginalMarkdown);
        return MarkdownSourceEditing.NormalizeInlineText(source);
    }

    private static string CultureInfoInvariant(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
