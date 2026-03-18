using System.Text.RegularExpressions;

namespace CodexGui.Markdown.Services;

internal static partial class MarkdownSourceEditing
{
    internal static string DetectLineEnding(string? text, string fallback = "\n")
    {
        var normalizedFallback = string.IsNullOrWhiteSpace(fallback) ? "\n" : fallback;
        if (string.IsNullOrEmpty(text))
        {
            return normalizedFallback;
        }

        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    internal static string NormalizeLineEndings(string? text, string lineEnding)
    {
        if (string.IsNullOrWhiteSpace(lineEnding))
        {
            lineEnding = "\n";
        }

        return NormalizeLineEndings(text).Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    internal static string TrimTrailingLineBreaks(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var end = text.Length;
        while (end > 0 && text[end - 1] is '\r' or '\n')
        {
            end--;
        }

        return end == text.Length ? text : text[..end];
    }

    internal static string DetermineCodeFenceDelimiter(string code, string? existingSourceText)
    {
        var existingStyle = ParseCodeFenceStyle(existingSourceText);
        var backtickLength = Math.Max(3, FindMaxFenceRun(code, '`') + 1);
        var tildeLength = Math.Max(3, FindMaxFenceRun(code, '~') + 1);

        if (!existingStyle.IsEmpty)
        {
            return existingStyle.FenceCharacter == '`'
                ? new string('`', Math.Max(existingStyle.Fence.Length, backtickLength))
                : new string('~', Math.Max(existingStyle.Fence.Length, tildeLength));
        }

        return backtickLength <= tildeLength
            ? new string('`', backtickLength)
            : new string('~', tildeLength);
    }

    internal static MarkdownCodeFenceStyle ParseCodeFenceStyle(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return MarkdownCodeFenceStyle.Empty;
        }

        var normalized = NormalizeLineEndings(sourceText);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length < 2)
        {
            return MarkdownCodeFenceStyle.Empty;
        }

        var openingMatch = FenceLineRegex().Match(lines[0].Trim());
        if (!openingMatch.Success)
        {
            return MarkdownCodeFenceStyle.Empty;
        }

        var openingFence = openingMatch.Groups["fence"].Value;
        var closingFence = lines[^1].Trim();
        var lineEnding = DetectLineEnding(sourceText);

        if (closingFence.Length < openingFence.Length ||
            closingFence[0] != openingFence[0] ||
            closingFence.Trim(closingFence[0]).Length > 0)
        {
            return new MarkdownCodeFenceStyle(openingFence, lineEnding);
        }

        return new MarkdownCodeFenceStyle(
            closingFence.Length > openingFence.Length ? closingFence : openingFence,
            lineEnding);
    }

    internal static MarkdownYamlFrontMatterStyle ParseYamlFrontMatterStyle(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return MarkdownYamlFrontMatterStyle.Default;
        }

        var normalized = NormalizeLineEndings(sourceText);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length < 2 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
        {
            return MarkdownYamlFrontMatterStyle.Default with
            {
                LineEnding = DetectLineEnding(sourceText)
            };
        }

        var closingFence = lines[^1].Trim();
        if (!string.Equals(closingFence, "---", StringComparison.Ordinal) &&
            !string.Equals(closingFence, "...", StringComparison.Ordinal))
        {
            closingFence = MarkdownYamlFrontMatterStyle.Default.ClosingFence;
        }

        return new MarkdownYamlFrontMatterStyle(
            MarkdownYamlFrontMatterStyle.Default.OpeningFence,
            closingFence,
            DetectLineEnding(sourceText));
    }

    internal static MarkdownReferenceTitleStyle ParseReferenceTitleStyle(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return MarkdownReferenceTitleStyle.Default;
        }

        var match = ReferenceTitleRegex().Match(NormalizeLineEndings(sourceText));
        if (!match.Success)
        {
            return MarkdownReferenceTitleStyle.Default;
        }

        if (match.Groups["single"].Success)
        {
            return new MarkdownReferenceTitleStyle(MarkdownReferenceTitleDelimiter.SingleQuote);
        }

        return match.Groups["paren"].Success
            ? new MarkdownReferenceTitleStyle(MarkdownReferenceTitleDelimiter.Parentheses)
            : MarkdownReferenceTitleStyle.Default;
    }

    internal static MarkdownFootnoteStyle ParseFootnoteStyle(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return MarkdownFootnoteStyle.Default;
        }

        var normalized = NormalizeLineEndings(sourceText);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            var indentLength = 0;
            while (indentLength < line.Length && line[indentLength] is ' ' or '\t')
            {
                indentLength++;
            }

            if (indentLength > 0)
            {
                return new MarkdownFootnoteStyle(line[..indentLength], DetectLineEnding(sourceText));
            }
        }

        return MarkdownFootnoteStyle.Default with
        {
            LineEnding = DetectLineEnding(sourceText)
        };
    }

    private static int FindMaxFenceRun(string text, char fenceCharacter)
    {
        var maxRunLength = 0;
        var currentRunLength = 0;

        foreach (var character in NormalizeLineEndings(text))
        {
            if (character == fenceCharacter)
            {
                currentRunLength++;
                maxRunLength = Math.Max(maxRunLength, currentRunLength);
            }
            else
            {
                currentRunLength = 0;
            }
        }

        return maxRunLength;
    }

    [GeneratedRegex(@"^(?<fence>`{3,}|~{3,}).*$", RegexOptions.CultureInvariant)]
    private static partial Regex FenceLineRegex();

    [GeneratedRegex(@"^\[[^\]]+\]:\s+\S+\s+(?:(?<double>"".*"")|(?<single>'.*')|(?<paren>\(.*\)))\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceTitleRegex();
}

internal readonly record struct MarkdownCodeFenceStyle(string Fence, string LineEnding)
{
    public static MarkdownCodeFenceStyle Empty { get; } = new(string.Empty, "\n");

    public bool IsEmpty => string.IsNullOrEmpty(Fence);

    public char FenceCharacter => IsEmpty ? '`' : Fence[0];
}

internal readonly record struct MarkdownYamlFrontMatterStyle(string OpeningFence, string ClosingFence, string LineEnding)
{
    public static MarkdownYamlFrontMatterStyle Default { get; } = new("---", "---", "\n");
}

internal enum MarkdownReferenceTitleDelimiter
{
    DoubleQuote,
    SingleQuote,
    Parentheses
}

internal readonly record struct MarkdownReferenceTitleStyle(MarkdownReferenceTitleDelimiter Delimiter)
{
    public static MarkdownReferenceTitleStyle Default { get; } =
        new(MarkdownReferenceTitleDelimiter.DoubleQuote);
}

internal readonly record struct MarkdownFootnoteStyle(string ContinuationIndent, string LineEnding)
{
    public static MarkdownFootnoteStyle Default { get; } = new("    ", "\n");
}
