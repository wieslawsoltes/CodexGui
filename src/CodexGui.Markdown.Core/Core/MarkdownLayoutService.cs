using System.Collections.Concurrent;
using System.Text;
using Pretext;

namespace CodexGui.Markdown.Core;

public sealed record MarkdownLayoutOptions(
    string BodyFontFamily,
    string HeadingFontFamily,
    string MonospaceFontFamily,
    double BodyFontSize,
    double HeadingOneFontSize,
    double HeadingTwoFontSize,
    double HeadingThreeFontSize,
    double BodyLineHeight,
    double HeadingOneLineHeight,
    double HeadingTwoLineHeight,
    double HeadingThreeLineHeight,
    double CodeFontSize,
    double CodeLineHeight,
    double BlockSpacing,
    double FlowSpacing,
    double RuleHeight)
{
    public static MarkdownLayoutOptions Default { get; } = new(
        "\"Segoe UI\", sans-serif",
        "\"Georgia\", serif",
        "\"Cascadia Mono\", Consolas, monospace",
        15,
        30,
        24,
        20,
        22,
        34,
        28,
        24,
        13,
        19,
        16,
        4,
        18);
}

public sealed class MarkdownLayoutService
{
    private readonly struct InlineTokenCacheKey(string text, string font) : IEquatable<InlineTokenCacheKey>
    {
        public string Text { get; } = text;

        public string Font { get; } = font;

        public bool Equals(InlineTokenCacheKey other)
        {
            return string.Equals(Text, other.Text, StringComparison.Ordinal) &&
                   string.Equals(Font, other.Font, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Text, Font);
        }
    }

    private sealed class CachedInlineTokenTemplate
    {
        public required PreparedTextWithSegments Prepared { get; init; }

        public required LayoutCursor End { get; init; }

        public required double NaturalWidth { get; init; }
    }

    private sealed class InlineToken
    {
        public required MarkdownInlineFragment Source { get; init; }

        public required string Text { get; init; }

        public required PreparedTextWithSegments Prepared { get; init; }

        public required LayoutCursor End { get; init; }

        public required double NaturalWidth { get; init; }

        public required bool IsWhitespace { get; init; }

        public required bool IsAtomic { get; init; }

        public LayoutCursor Cursor { get; set; }
    }

    private static readonly ConcurrentDictionary<InlineTokenCacheKey, CachedInlineTokenTemplate> InlineTokenCache = new();
    private static readonly object PretextSync = new();

    private readonly MarkdownPluginRegistry _registry;
    private readonly IMarkdownCodeHighlighter[] _orderedCodeHighlighters;

    public MarkdownLayoutService(MarkdownPluginRegistry? registry = null)
    {
        _registry = registry ?? MarkdownRuntimeConfiguration.Snapshot();
        _orderedCodeHighlighters = _registry.CodeHighlighters
            .OrderBy(static highlighter => highlighter.Order)
            .ToArray();
    }

    public MarkdownLayoutDocument Layout(MarkdownDocumentModel document, double availableWidth, MarkdownLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= MarkdownLayoutOptions.Default;
        var effectiveWidth = Math.Max(availableWidth, 1);
        var top = 0d;
        var blocks = new List<MarkdownBlockLayout>(document.Blocks.Count);
        var hitRegions = new List<MarkdownHitRegion>();

        foreach (var block in document.Blocks)
        {
            var layout = LayoutBlock(block, effectiveWidth, top, options, hitRegions);
            blocks.Add(layout);
            top += layout.Height + options.BlockSpacing;
        }

        if (blocks.Count > 0)
        {
            top -= options.BlockSpacing;
        }

        return new MarkdownLayoutDocument(effectiveWidth, Math.Max(top, 0), blocks, hitRegions);
    }

    public MarkdownCodeHighlightResult HighlightCode(string code, string? languageHint)
    {
        MarkdownCodeHighlightResult? plainFallback = null;
        foreach (var highlighter in _orderedCodeHighlighters)
        {
            if (highlighter.CanHighlight(languageHint))
            {
                var result = highlighter.Highlight(code, languageHint);
                if (!string.Equals(result.Engine, "plain", StringComparison.OrdinalIgnoreCase))
                {
                    return result;
                }

                plainFallback = result;
            }
        }

        return plainFallback ?? new MarkdownCodeHighlightResult(
            "plain",
            [new MarkdownStyledTextRun(code, new MarkdownTextStyle())]);
    }

    private MarkdownBlockLayout LayoutBlock(
        MarkdownBlockNode block,
        double availableWidth,
        double top,
        MarkdownLayoutOptions options,
        List<MarkdownHitRegion> hitRegions)
    {
        return block switch
        {
            MarkdownParagraphBlock paragraphBlock => LayoutInlineBlock(
                paragraphBlock,
                paragraphBlock.Flows,
                availableWidth,
                top,
                BuildFont(options.BodyFontSize, options.BodyFontFamily),
                options.BodyLineHeight,
                options.FlowSpacing,
                hitRegions),
            MarkdownHeadingBlock headingBlock => LayoutInlineBlock(
                headingBlock,
                headingBlock.Flows,
                availableWidth,
                top,
                BuildHeadingFont(headingBlock.Level, options),
                ResolveHeadingLineHeight(headingBlock.Level, options),
                options.FlowSpacing,
                hitRegions),
            MarkdownCodeBlock codeBlock => LayoutCodeBlock(codeBlock, availableWidth, top, options),
            MarkdownYamlFrontMatterBlock yamlFrontMatterBlock => LayoutSummaryBlock(yamlFrontMatterBlock, yamlFrontMatterBlock.Yaml, top, options.CodeLineHeight),
            MarkdownRuleBlock ruleBlock => new MarkdownRuleBlockLayout(ruleBlock, top, options.RuleHeight),
            MarkdownMathBlock mathBlock => LayoutSummaryBlock(mathBlock, mathBlock.Expression, top, options.BodyLineHeight),
            MarkdownMermaidBlock mermaidBlock => LayoutSummaryBlock(mermaidBlock, mermaidBlock.DiagramSource, top, options.BodyLineHeight),
            MarkdownHtmlBlock htmlBlock => LayoutSummaryBlock(htmlBlock, htmlBlock.Html, top, options.BodyLineHeight),
            MarkdownFallbackBlock fallbackBlock => LayoutSummaryBlock(fallbackBlock, fallbackBlock.BodyMarkdown, top, options.BodyLineHeight),
            MarkdownQuoteBlock quoteBlock => LayoutSummaryBlock(quoteBlock, DescribeBlockCount(quoteBlock.Blocks.Count, "quoted block"), top, options.BodyLineHeight),
            MarkdownListBlock listBlock => LayoutSummaryBlock(listBlock, DescribeBlockCount(listBlock.Items.Count, "list item"), top, options.BodyLineHeight),
            MarkdownTableBlock tableBlock => LayoutSummaryBlock(tableBlock, DescribeBlockCount(tableBlock.Rows.Count, "table row"), top, options.BodyLineHeight),
            MarkdownAlertBlock alertBlock => LayoutSummaryBlock(alertBlock, alertBlock.BodyMarkdown, top, options.BodyLineHeight),
            MarkdownCustomContainerBlock customContainerBlock => LayoutSummaryBlock(customContainerBlock, customContainerBlock.BodyMarkdown, top, options.BodyLineHeight),
            MarkdownDefinitionListBlock definitionListBlock => LayoutSummaryBlock(definitionListBlock, DescribeBlockCount(definitionListBlock.Items.Count, "definition"), top, options.BodyLineHeight),
            MarkdownFigureBlock figureBlock => LayoutSummaryBlock(figureBlock, figureBlock.BodyMarkdown, top, options.BodyLineHeight),
            MarkdownLinkReferenceBlock linkReferenceBlock => LayoutSummaryBlock(linkReferenceBlock, $"{linkReferenceBlock.Label} -> {linkReferenceBlock.Url}", top, options.BodyLineHeight),
            MarkdownAbbreviationBlock abbreviationBlock => LayoutSummaryBlock(abbreviationBlock, abbreviationBlock.Meaning, top, options.BodyLineHeight),
            MarkdownFootnoteBlock footnoteBlock => LayoutSummaryBlock(footnoteBlock, footnoteBlock.BodyMarkdown, top, options.BodyLineHeight),
            MarkdownFooterBlock footerBlock => LayoutSummaryBlock(footerBlock, footerBlock.BodyMarkdown, top, options.BodyLineHeight),
            _ => LayoutSummaryBlock(block, block.GetType().Name, top, options.BodyLineHeight)
        };
    }

    private static MarkdownInlineBlockLayout LayoutInlineBlock(
        MarkdownBlockNode block,
        IReadOnlyList<MarkdownInlineFlow> flows,
        double availableWidth,
        double top,
        string defaultFont,
        double lineHeight,
        double flowSpacing,
        List<MarkdownHitRegion> hitRegions)
    {
        var lines = new List<MarkdownInlineLayoutLine>();
        var localTop = 0d;
        foreach (var flow in flows)
        {
            if (flow.Fragments.Count == 0)
            {
                continue;
            }

            var tokens = TokenizeFlow(flow, defaultFont);
            var tokenIndex = 0;
            while (tokenIndex < tokens.Count)
            {
                var x = 0d;
                var pendingGap = 0d;
                var pendingWhitespace = string.Empty;
                var fragments = new List<MarkdownInlineLayoutFragment>();
                var lineText = new StringBuilder();

                while (tokenIndex < tokens.Count)
                {
                    var token = tokens[tokenIndex];
                    if (token.IsWhitespace)
                    {
                        if (fragments.Count == 0)
                        {
                            tokenIndex++;
                            continue;
                        }

                        pendingGap += token.NaturalWidth;
                        pendingWhitespace += token.Text;
                        tokenIndex++;
                        continue;
                    }

                    var remainingWidth = Math.Max(availableWidth - x - pendingGap, 1);
                    var isTokenAtStart = fragments.Count == 0;
                    var useWholeToken = token.IsAtomic || token.Source.IsAtomic;
                    if (useWholeToken)
                    {
                        if (!isTokenAtStart && x + pendingGap + token.NaturalWidth > availableWidth)
                        {
                            break;
                        }

                        x += pendingGap;
                        fragments.Add(CreateInlineFragment(token, pendingWhitespace, token.Text, pendingGap, x, token.NaturalWidth));
                        lineText.Append(pendingWhitespace);
                        lineText.Append(token.Text);
                        AddHitRegion(hitRegions, token, top + localTop, lineHeight, x, token.NaturalWidth);
                        x += token.NaturalWidth;
                        pendingGap = 0;
                        pendingWhitespace = string.Empty;
                        tokenIndex++;
                        continue;
                    }

                    var line = UsePretext(() => PretextLayout.LayoutNextLine(token.Prepared, token.Cursor, remainingWidth));
                    if (line is null)
                    {
                        tokenIndex++;
                        pendingGap = 0;
                        pendingWhitespace = string.Empty;
                        continue;
                    }

                    if (!isTokenAtStart && pendingGap > 0 && line.Width + x + pendingGap > availableWidth)
                    {
                        break;
                    }

                    x += pendingGap;
                    fragments.Add(CreateInlineFragment(token, pendingWhitespace, line.Text, pendingGap, x, line.Width));
                    lineText.Append(pendingWhitespace);
                    lineText.Append(line.Text);
                    AddHitRegion(hitRegions, token, top + localTop, lineHeight, x, line.Width);
                    x += line.Width;
                    pendingGap = 0;
                    pendingWhitespace = string.Empty;
                    token.Cursor = line.End;

                    if (!IsEnd(token))
                    {
                        break;
                    }

                    tokenIndex++;
                }

                if (fragments.Count == 0)
                {
                    break;
                }

                lines.Add(new MarkdownInlineLayoutLine(lineText.ToString(), x, fragments));
                localTop += lineHeight;
            }

            localTop += flowSpacing;
        }

        if (lines.Count > 0)
        {
            localTop -= flowSpacing;
        }

        return new MarkdownInlineBlockLayout(block, top, Math.Max(localTop, lineHeight), lineHeight, lines);
    }

    private MarkdownCodeBlockLayout LayoutCodeBlock(
        MarkdownCodeBlock block,
        double availableWidth,
        double top,
        MarkdownLayoutOptions options)
    {
        var code = block.Code ?? string.Empty;
        var prepared = UsePretext(() => PretextLayout.PrepareWithSegments(
            code,
            BuildFont(options.CodeFontSize, options.MonospaceFontFamily),
            new PrepareOptions(WhiteSpace: WhiteSpaceMode.PreWrap)));
        var layout = UsePretext(() => PretextLayout.LayoutWithLines(prepared, availableWidth, options.CodeLineHeight));
        var highlight = HighlightCode(code, block.LanguageHint);
        var lines = SplitRunsByLines(highlight.Runs, layout.Lines);
        return new MarkdownCodeBlockLayout(
            block,
            top,
            Math.Max(layout.Height, options.CodeLineHeight),
            options.CodeLineHeight,
            highlight.Engine,
            lines);
    }

    private static MarkdownSummaryBlockLayout LayoutSummaryBlock(
        MarkdownBlockNode block,
        string summary,
        double top,
        double lineHeight)
    {
        var normalized = MarkdownSourceEditing.NormalizeBlockText(summary);
        var lineCount = CountLines(normalized);
        return new MarkdownSummaryBlockLayout(
            block,
            top,
            lineCount * lineHeight,
            normalized);
    }

    private static IReadOnlyList<MarkdownCodeLayoutLine> SplitRunsByLines(
        IReadOnlyList<MarkdownStyledTextRun> runs,
        IReadOnlyList<LayoutLine> lines)
    {
        var result = new List<MarkdownCodeLayoutLine>(lines.Count);
        var runIndex = 0;
        var runOffset = 0;

        foreach (var line in lines)
        {
            var remaining = line.Text.Length;
            var lineRuns = new List<MarkdownStyledTextRun>();

            while (remaining > 0 && runIndex < runs.Count)
            {
                var currentRun = runs[runIndex];
                if (runOffset >= currentRun.Text.Length)
                {
                    runIndex++;
                    runOffset = 0;
                    continue;
                }

                var take = Math.Min(remaining, currentRun.Text.Length - runOffset);
                lineRuns.Add(new MarkdownStyledTextRun(
                    currentRun.Text.Substring(runOffset, take),
                    currentRun.Style,
                    currentRun.Scope));

                runOffset += take;
                remaining -= take;

                if (runOffset >= currentRun.Text.Length)
                {
                    runIndex++;
                    runOffset = 0;
                }
            }

            if (runIndex < runs.Count && runOffset < runs[runIndex].Text.Length && runs[runIndex].Text[runOffset] == '\n')
            {
                runOffset++;
                if (runOffset >= runs[runIndex].Text.Length)
                {
                    runIndex++;
                    runOffset = 0;
                }
            }

            result.Add(new MarkdownCodeLayoutLine(line.Text, line.Width, lineRuns));
        }

        if (result.Count == 0)
        {
            result.Add(new MarkdownCodeLayoutLine(string.Empty, 0, []));
        }

        return result;
    }

    private static IReadOnlyList<InlineToken> TokenizeFlow(MarkdownInlineFlow flow, string defaultFont)
    {
        var tokens = new List<InlineToken>();
        foreach (var fragment in flow.Fragments)
        {
            if (fragment.IsAtomic || fragment.Style.Code)
            {
                tokens.Add(CreateToken(fragment, fragment.Text, defaultFont, isWhitespace: false, isAtomic: true));
                continue;
            }

            foreach (var part in SplitTextParts(fragment.Text))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                var isWhitespace = string.IsNullOrWhiteSpace(part);
                tokens.Add(CreateToken(fragment, part, defaultFont, isWhitespace, fragment.IsAtomic));
            }
        }

        return tokens;
    }

    private static InlineToken CreateToken(MarkdownInlineFragment source, string text, string defaultFont, bool isWhitespace, bool isAtomic)
    {
        var measurementText = isWhitespace ? new string('\u00A0', Math.Max(text.Length, 1)) : text;
        var font = ResolveInlineFont(source.Style, defaultFont);
        var template = InlineTokenCache.GetOrAdd(
            new InlineTokenCacheKey(measurementText, font),
            static key =>
            {
                var prepared = UsePretext(() => PretextLayout.PrepareWithSegments(key.Text, key.Font));
                var fullLine = UsePretext(() => PretextLayout.LayoutNextLine(prepared, new LayoutCursor(0, 0), double.PositiveInfinity));
                return new CachedInlineTokenTemplate
                {
                    Prepared = prepared,
                    End = fullLine?.End ?? new LayoutCursor(0, 0),
                    NaturalWidth = fullLine?.Width ?? 0
                };
            });
        return new InlineToken
        {
            Source = source with { Text = text },
            Text = text,
            Prepared = template.Prepared,
            Cursor = new LayoutCursor(0, 0),
            End = template.End,
            NaturalWidth = template.NaturalWidth,
            IsWhitespace = isWhitespace,
            IsAtomic = isAtomic
        };
    }

    private static IEnumerable<string> SplitTextParts(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var isWhitespace = char.IsWhiteSpace(text[start]);
            var end = start + 1;
            while (end < text.Length && char.IsWhiteSpace(text[end]) == isWhitespace)
            {
                end++;
            }

            yield return text[start..end];
            start = end;
        }
    }

    private static MarkdownInlineLayoutFragment CreateInlineFragment(InlineToken token, string leadingWhitespace, string text, double gapBefore, double x, double width)
    {
        return new MarkdownInlineLayoutFragment(
            leadingWhitespace,
            text,
            token.Source.Style,
            token.Source.SourceSpan,
            token.Source.SemanticClass,
            token.Source.LinkTarget,
            gapBefore,
            x,
            width);
    }

    private static void AddHitRegion(
        List<MarkdownHitRegion> hitRegions,
        InlineToken token,
        double y,
        double height,
        double x,
        double width)
    {
        if (width <= 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(token.Source.LinkTarget))
        {
            hitRegions.Add(new MarkdownHitRegion(
                x,
                y,
                width,
                height,
                token.Source.SourceSpan,
                token.Source.LinkTarget,
                "link"));
            return;
        }

        if (string.Equals(token.Source.SemanticClass, "math", StringComparison.Ordinal))
        {
            hitRegions.Add(new MarkdownHitRegion(
                x,
                y,
                width,
                height,
                token.Source.SourceSpan,
                LinkTarget: null,
                Kind: "inline-math"));
        }
    }

    private static bool IsEnd(InlineToken token)
    {
        return token.Cursor.Equals(token.End);
    }

    private static T UsePretext<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (PretextSync)
        {
            return action();
        }
    }

    private static string ResolveInlineFont(MarkdownTextStyle style, string defaultFont)
    {
        if (style.Code)
        {
            return defaultFont.Contains("Cascadia", StringComparison.Ordinal)
                ? defaultFont
                : $"600 13px \"Cascadia Mono\", Consolas, monospace";
        }

        var weight = style.Bold ? "700" : "400";
        var italic = style.Italic ? " italic" : string.Empty;
        var separatorIndex = defaultFont.IndexOf("px", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return $"{weight}{italic} {defaultFont}";
        }

        var sizeAndFamily = defaultFont[(separatorIndex + 2)..].TrimStart();
        var sizeStart = defaultFont.LastIndexOf(' ', separatorIndex);
        var sizeText = sizeStart >= 0 ? defaultFont[(sizeStart + 1)..(separatorIndex + 2)] : defaultFont[..(separatorIndex + 2)];
        return $"{weight}{italic} {sizeText} {sizeAndFamily}";
    }

    private static string BuildFont(double fontSize, string fontFamily)
    {
        return $"400 {fontSize:0.##}px {fontFamily}";
    }

    private static string BuildHeadingFont(int level, MarkdownLayoutOptions options)
    {
        return level switch
        {
            1 => $"700 {options.HeadingOneFontSize:0.##}px {options.HeadingFontFamily}",
            2 => $"700 {options.HeadingTwoFontSize:0.##}px {options.HeadingFontFamily}",
            _ => $"700 {options.HeadingThreeFontSize:0.##}px {options.HeadingFontFamily}"
        };
    }

    private static double ResolveHeadingLineHeight(int level, MarkdownLayoutOptions options)
    {
        return level switch
        {
            1 => options.HeadingOneLineHeight,
            2 => options.HeadingTwoLineHeight,
            _ => options.HeadingThreeLineHeight
        };
    }

    private static string DescribeBlockCount(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 1;
        }

        var lineCount = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }
}
