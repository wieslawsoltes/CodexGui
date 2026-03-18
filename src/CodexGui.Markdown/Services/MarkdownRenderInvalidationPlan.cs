namespace CodexGui.Markdown.Services;

internal enum MarkdownRenderInvalidationScope
{
    None,
    Inline,
    Block,
    Full
}

internal readonly record struct MarkdownRenderTextChange(
    int Start,
    int OldLength,
    int NewLength,
    int OldLineBreakCount,
    int NewLineBreakCount)
{
    public int OldEndExclusive => Start + OldLength;

    public int NewEndExclusive => Start + NewLength;
}

internal readonly record struct MarkdownRenderInvalidationPlan(
    MarkdownRenderInvalidationScope Scope,
    MarkdownRenderTextChange? Change,
    bool ReuseCachedParseResult,
    string Reason)
{
    public static MarkdownRenderInvalidationPlan Create(string? previousMarkdown, string? currentMarkdown)
    {
        currentMarkdown ??= string.Empty;

        if (previousMarkdown is null)
        {
            return new MarkdownRenderInvalidationPlan(
                MarkdownRenderInvalidationScope.Full,
                new MarkdownRenderTextChange(
                    0,
                    0,
                    currentMarkdown.Length,
                    0,
                    CountLineBreaks(currentMarkdown.AsSpan())),
                ReuseCachedParseResult: false,
                Reason: "initial render");
        }

        if (string.Equals(previousMarkdown, currentMarkdown, StringComparison.Ordinal))
        {
            return new MarkdownRenderInvalidationPlan(
                MarkdownRenderInvalidationScope.None,
                Change: null,
                ReuseCachedParseResult: true,
                Reason: "markdown unchanged");
        }

        var change = ComputeChange(previousMarkdown.AsSpan(), currentMarkdown.AsSpan());
        var scope = Classify(change);

        return new MarkdownRenderInvalidationPlan(
            scope,
            change,
            ReuseCachedParseResult: false,
            Reason: scope switch
            {
                MarkdownRenderInvalidationScope.Inline => "single-line mutation",
                MarkdownRenderInvalidationScope.Block => "bounded multi-line mutation",
                _ => "full-document mutation"
            });
    }

    private static MarkdownRenderTextChange ComputeChange(ReadOnlySpan<char> previousMarkdown, ReadOnlySpan<char> currentMarkdown)
    {
        var prefixLength = 0;
        var sharedPrefixLimit = Math.Min(previousMarkdown.Length, currentMarkdown.Length);
        while (prefixLength < sharedPrefixLimit &&
               previousMarkdown[prefixLength] == currentMarkdown[prefixLength])
        {
            prefixLength++;
        }

        var sharedSuffixLimit = Math.Min(previousMarkdown.Length - prefixLength, currentMarkdown.Length - prefixLength);
        var suffixLength = 0;
        while (suffixLength < sharedSuffixLimit &&
               previousMarkdown[previousMarkdown.Length - suffixLength - 1] == currentMarkdown[currentMarkdown.Length - suffixLength - 1])
        {
            suffixLength++;
        }

        var oldLength = previousMarkdown.Length - prefixLength - suffixLength;
        var newLength = currentMarkdown.Length - prefixLength - suffixLength;

        return new MarkdownRenderTextChange(
            Start: prefixLength,
            OldLength: Math.Max(oldLength, 0),
            NewLength: Math.Max(newLength, 0),
            OldLineBreakCount: CountLineBreaks(previousMarkdown.Slice(prefixLength, Math.Max(oldLength, 0))),
            NewLineBreakCount: CountLineBreaks(currentMarkdown.Slice(prefixLength, Math.Max(newLength, 0))));
    }

    private static MarkdownRenderInvalidationScope Classify(MarkdownRenderTextChange change)
    {
        if (change.OldLength == 0 && change.NewLength == 0)
        {
            return MarkdownRenderInvalidationScope.None;
        }

        if (change.OldLineBreakCount == 0 && change.NewLineBreakCount == 0)
        {
            return MarkdownRenderInvalidationScope.Inline;
        }

        return Math.Max(change.OldLineBreakCount, change.NewLineBreakCount) <= 3
            ? MarkdownRenderInvalidationScope.Block
            : MarkdownRenderInvalidationScope.Full;
    }

    private static int CountLineBreaks(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
