using System.Text;
using System.Text.RegularExpressions;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Collapsible;

internal static class MarkdownCollapsibleParser
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build());

    public static MarkdownCollapsibleDocument Parse(CustomContainer customContainer, MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(customContainer);
        ArgumentNullException.ThrowIfNull(parseResult);

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(customContainer.Span);
        var source = sourceSpan.Slice(parseResult.OriginalMarkdown);
        if (string.IsNullOrWhiteSpace(source) && !parseResult.UsesOriginalSourceSpans)
        {
            source = sourceSpan.Slice(parseResult.ParsedMarkdown);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            var fallbackBody = ResolveCombinedSpan(customContainer)
                .Slice(parseResult.UsesOriginalSourceSpans ? parseResult.OriginalMarkdown : parseResult.ParsedMarkdown);
            MarkdownCollapsibleSyntax.TryParseArguments(customContainer.Arguments, out var summary, out var isExpanded);
            source = MarkdownCollapsibleSyntax.BuildCollapsible(
                customContainer.Info,
                summary,
                fallbackBody,
                isExpanded,
                preferredFenceLength: customContainer.OpeningFencedCharCount);
        }

        return Parse(source, customContainer.Info, customContainer.Arguments);
    }

    public static MarkdownCollapsibleDocument Parse(string? markdown)
    {
        return Parse(markdown, fallbackInfo: null, fallbackArguments: null);
    }

    private static MarkdownCollapsibleDocument Parse(string? markdown, string? fallbackInfo, string? fallbackArguments)
    {
        var source = MarkdownCollapsibleSyntax.NormalizeBlockText(markdown);
        List<MarkdownCollapsibleDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new MarkdownCollapsibleDiagnostic("Collapsible content is empty.", MarkdownSourceSpan.Empty));
            return new MarkdownCollapsibleDocument(
                string.Empty,
                MarkdownCollapsibleSyntax.DefaultKind,
                string.Empty,
                false,
                string.Empty,
                3,
                diagnostics);
        }

        var document = Markdig.Markdown.Parse(source, Pipeline.Value);
        CustomContainer? collapsibleContainer = null;
        foreach (var block in document)
        {
            switch (block)
            {
                case CustomContainer parsedContainer when MarkdownCollapsibleSyntax.IsCollapsibleInfo(parsedContainer.Info) && collapsibleContainer is null:
                    collapsibleContainer = parsedContainer;
                    break;
                case BlankLineBlock:
                    break;
                default:
                    diagnostics.Add(new MarkdownCollapsibleDiagnostic(
                        $"Unexpected block '{block.GetType().Name}' inside collapsible source.",
                        MarkdownSourceSpan.FromMarkdig(block.Span)));
                    break;
            }
        }

        var kind = MarkdownCollapsibleSyntax.NormalizeInfo(collapsibleContainer?.Info ?? fallbackInfo);
        var arguments = collapsibleContainer?.Arguments ?? fallbackArguments;
        MarkdownCollapsibleSyntax.TryParseArguments(arguments, out var summary, out var isExpanded);

        if (collapsibleContainer is null)
        {
            diagnostics.Add(new MarkdownCollapsibleDiagnostic(
                "Markdig did not produce a collapsible custom container from the provided markdown.",
                MarkdownSourceSpan.Empty));
        }

        var bodyMarkdown = MarkdownCollapsibleSyntax.ExtractBodyMarkdown(source);
        if (string.IsNullOrWhiteSpace(summary))
        {
            diagnostics.Add(new MarkdownCollapsibleDiagnostic(
                "Collapsible summary is empty.",
                MarkdownSourceSpan.Empty));
        }

        if (string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            diagnostics.Add(new MarkdownCollapsibleDiagnostic(
                "Collapsible body is empty.",
                new MarkdownSourceSpan(0, source.Length)));
        }

        var fenceLength = collapsibleContainer is null
            ? MarkdownCollapsibleSyntax.ReadFenceLength(source)
            : Math.Max(collapsibleContainer.OpeningFencedCharCount, MarkdownCollapsibleSyntax.ReadFenceLength(source));

        return new MarkdownCollapsibleDocument(
            source,
            kind,
            summary,
            isExpanded,
            bodyMarkdown,
            fenceLength,
            diagnostics);
    }

    private static MarkdownSourceSpan ResolveCombinedSpan(CustomContainer customContainer)
    {
        var blocks = customContainer
            .OfType<Block>()
            .Where(static block => block is not BlankLineBlock)
            .ToArray();
        if (blocks.Length == 0)
        {
            return MarkdownSourceSpan.Empty;
        }

        var first = MarkdownSourceSpan.FromMarkdig(blocks[0].Span);
        var start = first.Start;
        var endExclusive = first.EndExclusive;
        for (var index = 1; index < blocks.Length; index++)
        {
            var span = MarkdownSourceSpan.FromMarkdig(blocks[index].Span);
            if (span.IsEmpty)
            {
                continue;
            }

            start = Math.Min(start, span.Start);
            endExclusive = Math.Max(endExclusive, span.EndExclusive);
        }

        return endExclusive <= start ? MarkdownSourceSpan.Empty : new MarkdownSourceSpan(start, endExclusive - start);
    }
}

internal static partial class MarkdownCollapsibleSyntax
{
    public const string DefaultKind = "details";

    private static readonly MarkdownCollapsibleKindOption[] KindOptions =
    [
        new("details", "Details", "Use a standard details disclosure for optional context, FAQs, or implementation notes."),
        new("collapsible", "Collapsible", "Use an explicit collapsible section for progressive disclosure in longer documents.")
    ];

    public static IReadOnlyList<MarkdownCollapsibleKindOption> AvailableKinds => KindOptions;

    public static string NormalizeLineEndings(string? source)
    {
        return (source ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string NormalizeBlockText(string? source)
    {
        return NormalizeLineEndings(source).TrimEnd('\n');
    }

    public static string NormalizeInlineText(string? source)
    {
        return NormalizeLineEndings(source).Replace('\n', ' ').Trim();
    }

    public static bool IsCollapsibleInfo(string? info)
    {
        var normalized = NormalizeInlineText(info).ToLowerInvariant();
        return normalized is "details" or "detail" or "collapsible" or "collapse";
    }

    public static string NormalizeInfo(string? info)
    {
        return NormalizeInlineText(info).ToLowerInvariant() switch
        {
            "detail" or "details" => "details",
            "collapse" or "collapsible" => "collapsible",
            _ => DefaultKind
        };
    }

    public static MarkdownCollapsibleKindOption ResolveKindOption(string? kind)
    {
        var normalized = NormalizeInfo(kind);
        foreach (var option in KindOptions)
        {
            if (string.Equals(option.Value, normalized, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return KindOptions[0];
    }

    public static bool TryParseArguments(string? arguments, out string summary, out bool isExpanded)
    {
        var normalized = NormalizeInlineText(arguments);
        if (normalized.Length == 0)
        {
            summary = string.Empty;
            isExpanded = false;
            return true;
        }

        var match = ExpandedStateRegex().Match(normalized);
        if (match.Success)
        {
            summary = NormalizeInlineText(match.Groups["summary"].Value);
            isExpanded = true;
            return true;
        }

        summary = normalized;
        isExpanded = false;
        return true;
    }

    public static int ReadFenceLength(string? source)
    {
        var normalized = NormalizeLineEndings(source);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 3;
        }

        var firstLine = normalized.Split('\n', 2, StringSplitOptions.None)[0].TrimStart();
        var count = 0;
        while (count < firstLine.Length && firstLine[count] == ':')
        {
            count++;
        }

        return Math.Max(count, 3);
    }

    public static string ExtractBodyMarkdown(string source)
    {
        var normalized = NormalizeLineEndings(source);
        var firstNewLine = normalized.IndexOf('\n');
        var lastNewLine = normalized.LastIndexOf('\n');
        if (firstNewLine < 0 || lastNewLine <= firstNewLine)
        {
            return string.Empty;
        }

        var body = normalized.Substring(firstNewLine + 1, lastNewLine - firstNewLine - 1);
        return NormalizeBlockText(body);
    }

    public static string BuildCollapsible(
        string? kind,
        string? summary,
        string? bodyMarkdown,
        bool isExpanded,
        int preferredFenceLength = 3,
        string? existingSourceText = null)
    {
        var normalizedKind = NormalizeInfo(kind);
        var normalizedSummary = NormalizeInlineText(summary);
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        var lineEnding = DetectLineEnding(existingSourceText);
        var arguments = isExpanded
            ? normalizedSummary.Length > 0 ? $"[open] {normalizedSummary}" : "[open]"
            : normalizedSummary;
        var fenceLength = ResolveFenceLength(normalizedBody, preferredFenceLength);
        var fence = new string(':', fenceLength);

        var builder = new StringBuilder();
        builder.Append(fence).Append(normalizedKind);
        if (arguments.Length > 0)
        {
            builder.Append(' ').Append(arguments);
        }

        builder.Append(lineEnding);
        if (normalizedBody.Length > 0)
        {
            builder.Append(NormalizeLineEndings(normalizedBody, lineEnding)).Append(lineEnding);
        }

        builder.Append(fence);
        return builder.ToString();
    }

    private static string NormalizeLineEndings(string? source, string lineEnding)
    {
        return NormalizeLineEndings(source).Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    private static string DetectLineEnding(string? source)
    {
        return string.IsNullOrEmpty(source) || !source.Contains("\r\n", StringComparison.Ordinal)
            ? "\n"
            : "\r\n";
    }

    private static int ResolveFenceLength(string normalizedBody, int preferredFenceLength)
    {
        var requiredFenceLength = Math.Max(preferredFenceLength, 3);
        if (normalizedBody.Length == 0)
        {
            return requiredFenceLength;
        }

        foreach (var line in NormalizeLineEndings(normalizedBody).Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.TrimStart();
            var colonCount = 0;
            while (colonCount < trimmed.Length && trimmed[colonCount] == ':')
            {
                colonCount++;
            }

            if (colonCount >= requiredFenceLength)
            {
                requiredFenceLength = colonCount + 1;
            }
        }

        return requiredFenceLength;
    }

    [GeneratedRegex(@"^\s*(?:\[(?:open|expanded)\]|(?:open|expanded)\b[:=\-]?)\s*(?<summary>.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExpandedStateRegex();
}
