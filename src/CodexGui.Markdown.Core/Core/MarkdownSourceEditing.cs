using System.Text.RegularExpressions;

namespace CodexGui.Markdown.Core;

public static partial class MarkdownSourceEditing
{
    public static string Replace(string markdown, MarkdownSourceSpan span, string replacement)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(replacement);

        if (span.IsEmpty)
        {
            return markdown;
        }

        var start = Math.Clamp(span.Start, 0, markdown.Length);
        var end = Math.Clamp(span.EndExclusive, start, markdown.Length);
        return string.Concat(markdown.AsSpan(0, start), replacement, markdown.AsSpan(end));
    }

    public static string Remove(string markdown, MarkdownSourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Replace(markdown, span, string.Empty);
    }

    public static string NormalizeLineEndings(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
    }

    public static string NormalizeBlockText(string? text)
    {
        return NormalizeLineEndings(text).TrimEnd('\n');
    }

    public static string NormalizeInlineText(string? text)
    {
        return NormalizeBlockText(text).Replace('\n', ' ');
    }

    public static string NormalizeInlineMarkdown(string? markdown)
    {
        return NormalizeLineEndings(markdown).Replace('\n', ' ').Trim();
    }

    public static string StripListMarker(string? text)
    {
        var normalized = NormalizeBlockText(text);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        lines[0] = ListMarkerRegex().Replace(lines[0], string.Empty, count: 1);
        for (var index = 1; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimStart();
        }

        return string.Join('\n', lines).Trim();
    }

    public static string ExtractHeadingText(string sourceText, int level)
    {
        var normalized = NormalizeBlockText(sourceText);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        if (lines[0].TrimStart().StartsWith('#'))
        {
            var content = Regex.Replace(lines[0], @"^\s*#{1,6}\s*", string.Empty);
            content = Regex.Replace(content, @"\s+#+\s*$", string.Empty);
            return content.Trim();
        }

        if (lines.Length > 1 && UnderlineHeadingRegex().IsMatch(lines[^1]))
        {
            return string.Join('\n', lines[..^1]).Trim();
        }

        return normalized.Trim();
    }

    public static string BuildHeadingMarkdown(int level, string text)
    {
        return $"{new string('#', Math.Clamp(level, 1, 6))} {NormalizeInlineMarkdown(text)}".TrimEnd();
    }

    public static string BuildParagraphMarkdown(string? text)
    {
        return NormalizeInlineMarkdown(text);
    }

    public static string NormalizeLanguageHint(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return string.Empty;
        }

        var trimmed = languageHint.Trim();
        var separatorIndex = trimmed.IndexOfAny([' ', '\t', ',', ';', '{', '(']);
        var normalized = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        return normalized.Trim().Trim('.').ToLowerInvariant();
    }

    public static string ResolveCodeFenceDelimiter(string code)
    {
        var normalized = NormalizeLineEndings(code);
        return normalized.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
    }

    public static string BuildCodeFence(string languageHint, string code)
    {
        var normalizedCode = NormalizeBlockText(code);
        var normalizedLanguage = NormalizeLanguageHint(languageHint);
        var fence = ResolveCodeFenceDelimiter(normalizedCode);
        return string.IsNullOrWhiteSpace(normalizedLanguage)
            ? $"{fence}\n{normalizedCode}\n{fence}"
            : $"{fence}{normalizedLanguage}\n{normalizedCode}\n{fence}";
    }

    public static string BuildAlertBlock(string? kind, string? bodyMarkdown)
    {
        var normalizedKind = NormalizeInlineText(kind)
            .Trim()
            .TrimStart('!')
            .ToLowerInvariant();
        if (normalizedKind.Length == 0)
        {
            normalizedKind = "note";
        }

        var normalizedBody = NormalizeLineEndings(bodyMarkdown).Trim('\n');
        var builder = new System.Text.StringBuilder();
        builder.Append("> [!").Append(normalizedKind.ToUpperInvariant()).Append(']');

        if (normalizedBody.Length == 0)
        {
            return builder.ToString();
        }

        foreach (var line in NormalizeLineEndings(normalizedBody).Split('\n', StringSplitOptions.None))
        {
            builder.Append('\n');
            if (line.Length == 0)
            {
                builder.Append('>');
            }
            else
            {
                builder.Append("> ").Append(line);
            }
        }

        return builder.ToString();
    }

    public static string BuildCustomContainer(string? info, string? arguments, string? bodyMarkdown, int preferredFenceLength = 3)
    {
        var normalizedInfo = NormalizeInlineText(info).ToLowerInvariant();
        var normalizedArguments = normalizedInfo.Length == 0 ? string.Empty : NormalizeInlineText(arguments);
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        var fenceLength = ResolveContainerFenceLength(normalizedBody, preferredFenceLength);
        var fence = new string(':', fenceLength);
        var builder = new System.Text.StringBuilder();
        builder.Append(fence);
        if (normalizedInfo.Length > 0)
        {
            builder.Append(normalizedInfo);
            if (normalizedArguments.Length > 0)
            {
                builder.Append(' ').Append(normalizedArguments);
            }
        }

        builder.Append('\n');
        if (normalizedBody.Length > 0)
        {
            builder.Append(normalizedBody).Append('\n');
        }

        builder.Append(fence);
        return builder.ToString();
    }

    public static string BuildDefinitionList(IReadOnlyList<MarkdownDefinitionItemNode> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var builder = new System.Text.StringBuilder();
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (itemIndex > 0)
            {
                builder.Append("\n\n");
            }

            var item = items[itemIndex];
            foreach (var term in item.Terms)
            {
                builder.Append(NormalizeInlineMarkdown(term.Markdown)).Append('\n');
            }

            var definitionLines = NormalizeLineEndings(item.DefinitionMarkdown).Split('\n', StringSplitOptions.None);
            if (definitionLines.Length == 0 || (definitionLines.Length == 1 && definitionLines[0].Length == 0))
            {
                builder.Append(":   Definition");
                continue;
            }

            builder.Append(":");
            if (definitionLines[0].Length > 0)
            {
                builder.Append("   ").Append(definitionLines[0]);
            }

            for (var lineIndex = 1; lineIndex < definitionLines.Length; lineIndex++)
            {
                builder.Append('\n');
                if (definitionLines[lineIndex].Length > 0)
                {
                    builder.Append("    ").Append(definitionLines[lineIndex]);
                }
            }
        }

        return builder.ToString();
    }

    public static string BuildFigure(string? leadingCaption, string? bodyMarkdown, string? trailingCaption, int preferredFenceLength = 3)
    {
        var normalizedLeadingCaption = NormalizeInlineMarkdown(leadingCaption);
        var normalizedTrailingCaption = NormalizeInlineMarkdown(trailingCaption);
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        var fenceLength = ResolveRepeatedFenceLength(normalizedBody, '^', preferredFenceLength);
        var fence = new string('^', fenceLength);
        var builder = new System.Text.StringBuilder();
        builder.Append(fence);
        if (normalizedLeadingCaption.Length > 0)
        {
            builder.Append(' ').Append(normalizedLeadingCaption);
        }

        builder.Append('\n');
        if (normalizedBody.Length > 0)
        {
            builder.Append(normalizedBody).Append('\n');
        }

        builder.Append(fence);
        if (normalizedTrailingCaption.Length > 0)
        {
            builder.Append(' ').Append(normalizedTrailingCaption);
        }

        return builder.ToString();
    }

    public static string BuildFooter(string? bodyMarkdown)
    {
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        if (normalizedBody.Length == 0)
        {
            return "^^ Footer text.";
        }

        var builder = new System.Text.StringBuilder();
        var lines = NormalizeLineEndings(normalizedBody).Split('\n', StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index++)
        {
            builder.Append("^^");
            if (lines[index].Length > 0)
            {
                builder.Append(' ').Append(lines[index]);
            }

            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    public static string BuildMathBlock(string? expression)
    {
        var normalizedExpression = NormalizeBlockText(expression);
        return $"$$\n{normalizedExpression}\n$$";
    }

    public static string NormalizeInlineMath(string? expression)
    {
        var normalized = NormalizeInlineText(expression).Trim();
        if (normalized.Length >= 2 &&
            normalized[0] == '$' &&
            normalized[^1] == '$')
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized;
    }

    public static string BuildInlineMath(string? expression)
    {
        return $"${NormalizeInlineMath(expression)}$";
    }

    public static string BuildMermaidFence(string? source)
    {
        return BuildCodeFence("mermaid", NormalizeBlockText(source));
    }

    public static string BuildBlockInsertionReplacement(
        string currentBlockMarkdown,
        string insertedBlockMarkdown,
        bool insertBefore,
        out int revealStart,
        out int revealLength)
    {
        var normalizedCurrentBlock = NormalizeBlockText(currentBlockMarkdown);
        var normalizedInsertedBlock = NormalizeBlockText(insertedBlockMarkdown);

        if (normalizedInsertedBlock.Length == 0)
        {
            revealStart = 0;
            revealLength = 0;
            return normalizedCurrentBlock;
        }

        if (normalizedCurrentBlock.Length == 0)
        {
            revealStart = 0;
            revealLength = normalizedInsertedBlock.Length;
            return normalizedInsertedBlock;
        }

        const string separator = "\n\n";
        if (insertBefore)
        {
            revealStart = 0;
            revealLength = normalizedInsertedBlock.Length;
            return string.Concat(normalizedInsertedBlock, separator, normalizedCurrentBlock);
        }

        revealStart = normalizedCurrentBlock.Length + separator.Length;
        revealLength = normalizedInsertedBlock.Length;
        return string.Concat(normalizedCurrentBlock, separator, normalizedInsertedBlock);
    }

    public static string SanitizeTableCell(string? text)
    {
        return NormalizeBlockText(text)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace('\n', ' ');
    }

    [GeneratedRegex(@"^\s*(?:[-+*]|\d+[.)])\s+(?:\[(?: |x|X)\]\s+)?", RegexOptions.CultureInvariant)]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"^\s*[=-]{2,}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UnderlineHeadingRegex();

    private static int ResolveContainerFenceLength(string normalizedBody, int preferredFenceLength)
    {
        var requiredFenceLength = Math.Max(preferredFenceLength, 3);
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

    private static int ResolveRepeatedFenceLength(string normalizedBody, char fenceChar, int preferredFenceLength)
    {
        var requiredFenceLength = Math.Max(preferredFenceLength, 3);
        foreach (var line in NormalizeLineEndings(normalizedBody).Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.TrimStart();
            var count = 0;
            while (count < trimmed.Length && trimmed[count] == fenceChar)
            {
                count++;
            }

            if (count >= requiredFenceLength)
            {
                requiredFenceLength = count + 1;
            }
        }

        return requiredFenceLength;
    }
}
