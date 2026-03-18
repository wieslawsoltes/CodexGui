using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CodexGui.Markdown.Services;

internal enum MarkdownStructuralEditKind
{
    Replace,
    InsertBefore,
    InsertAfter,
    Remove,
    Duplicate,
    MoveUp,
    MoveDown,
    Promote,
    Demote,
    Split,
    Join
}

internal sealed class MarkdownStructuralEditResult(
    string updatedMarkdown,
    string replacementMarkdown,
    int revealStart,
    int revealLength,
    MarkdownStructuralEditKind kind,
    MarkdownSourceSpan? selectionSpan = null)
{
    public string UpdatedMarkdown { get; } = updatedMarkdown;

    public string ReplacementMarkdown { get; } = replacementMarkdown;

    public int RevealStart { get; } = revealStart;

    public int RevealLength { get; } = revealLength;

    public MarkdownStructuralEditKind Kind { get; } = kind;

    public MarkdownSourceSpan? SelectionSpan { get; } = selectionSpan;
}

internal sealed class MarkdownStructuralEditingService
{
    public IReadOnlyList<MarkdownSourceSpan> CollectBlockSpans(MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        List<MarkdownSourceSpan> spans = [];
        CollectBlockSpans(parseResult.Document, spans);
        return spans
            .Where(static span => !span.IsEmpty)
            .Distinct()
            .OrderBy(static span => span.Start)
            .ThenBy(static span => span.Length)
            .ToArray();
    }

    public bool TryFindNodeContainingSpan(
        MarkdownParseResult parseResult,
        MarkdownSourceSpan targetSpan,
        out MarkdownObject? markdownObject)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        MarkdownObject? best = null;
        var bestLength = int.MaxValue;

        VisitMarkdownObject(parseResult.Document, node =>
        {
            var candidateSpan = MarkdownSourceSpan.FromMarkdig(node.Span);
            if (candidateSpan.IsEmpty || !Contains(candidateSpan, targetSpan))
            {
                return;
            }

            if (candidateSpan.Length < bestLength)
            {
                best = node;
                bestLength = candidateSpan.Length;
            }
        });

        markdownObject = best;
        return markdownObject is not null;
    }

    public bool TryApplyTemplate(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        MarkdownBlockTemplate template,
        MarkdownCommandPaletteCommitMode mode,
        out MarkdownStructuralEditResult? result)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(template);

        if (!TryResolveBlockContext(parseResult, targetSpan, out var context))
        {
            result = null;
            return false;
        }

        var currentBlockMarkdown = context.SourceSpan.Slice(markdown);
        switch (mode)
        {
            case MarkdownCommandPaletteCommitMode.InsertBefore:
            {
                var replacement = MarkdownSourceEditing.BuildBlockInsertionReplacement(
                    currentBlockMarkdown,
                    template.Markdown,
                    insertBefore: true,
                    out var revealStart,
                    out var revealLength);
                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
                    replacement,
                    context.SourceSpan.Start + revealStart,
                    revealLength,
                    MarkdownStructuralEditKind.InsertBefore,
                    new MarkdownSourceSpan(context.SourceSpan.Start + revealStart, revealLength));
                return true;
            }

            case MarkdownCommandPaletteCommitMode.InsertAfter:
            {
                var replacement = MarkdownSourceEditing.BuildBlockInsertionReplacement(
                    currentBlockMarkdown,
                    template.Markdown,
                    insertBefore: false,
                    out var revealStart,
                    out var revealLength);
                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
                    replacement,
                    context.SourceSpan.Start + revealStart,
                    revealLength,
                    MarkdownStructuralEditKind.InsertAfter,
                    new MarkdownSourceSpan(context.SourceSpan.Start + revealStart, revealLength));
                return true;
            }

            default:
            {
                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, template.Markdown),
                    template.Markdown,
                    context.SourceSpan.Start,
                    template.Markdown.Length,
                    MarkdownStructuralEditKind.Replace,
                    new MarkdownSourceSpan(context.SourceSpan.Start, template.Markdown.Length));
                return true;
            }
        }
    }

    public bool TryDuplicateBlock(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        out MarkdownStructuralEditResult? result)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(markdown);

        if (!TryResolveBlockContext(parseResult, targetSpan, out var context))
        {
            result = null;
            return false;
        }

        var blockMarkdown = context.SourceSpan.Slice(markdown);
        if (string.IsNullOrWhiteSpace(blockMarkdown))
        {
            result = null;
            return false;
        }

        var separator = ResolveBlockSeparator(blockMarkdown, markdown);
        var replacement = string.Concat(blockMarkdown, separator, blockMarkdown);
        var revealStart = context.SourceSpan.Start + blockMarkdown.Length + separator.Length;
        result = new MarkdownStructuralEditResult(
            MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
            blockMarkdown,
            revealStart,
            blockMarkdown.Length,
            MarkdownStructuralEditKind.Duplicate,
            new MarkdownSourceSpan(revealStart, blockMarkdown.Length));
        return true;
    }

    public bool TryDeleteBlock(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        out MarkdownStructuralEditResult? result)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(markdown);

        if (!TryResolveBlockContext(parseResult, targetSpan, out var context))
        {
            result = null;
            return false;
        }

        var updatedMarkdown = MarkdownSourceEditing.Remove(markdown, context.SourceSpan);
        var revealStart = Math.Clamp(context.SourceSpan.Start, 0, updatedMarkdown.Length);
        result = new MarkdownStructuralEditResult(
            updatedMarkdown,
            string.Empty,
            revealStart,
            0,
            MarkdownStructuralEditKind.Remove);
        return true;
    }

    public bool TryMoveBlock(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        int direction,
        out MarkdownStructuralEditResult? result)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(markdown);

        if (direction is 0)
        {
            result = null;
            return false;
        }

        if (!TryResolveBlockContext(parseResult, targetSpan, out var context))
        {
            result = null;
            return false;
        }

        var adjacentIndex = context.SiblingIndex + Math.Sign(direction);
        if (adjacentIndex < 0 || adjacentIndex >= context.Siblings.Count)
        {
            result = null;
            return false;
        }

        var adjacentBlock = context.Siblings[adjacentIndex];
        var adjacentSpan = MarkdownSourceSpan.FromMarkdig(adjacentBlock.Span);
        if (adjacentSpan.IsEmpty)
        {
            result = null;
            return false;
        }

        var currentSpan = context.SourceSpan;
        var currentText = currentSpan.Slice(markdown);
        var adjacentText = adjacentSpan.Slice(markdown);
        if (adjacentSpan.Start < currentSpan.Start)
        {
            var prefix = markdown[..adjacentSpan.Start];
            var between = markdown[adjacentSpan.EndExclusive..currentSpan.Start];
            var suffix = markdown[currentSpan.EndExclusive..];
            var updated = string.Concat(prefix, currentText, between, adjacentText, suffix);
            result = new MarkdownStructuralEditResult(
                updated,
                currentText,
                adjacentSpan.Start,
                currentText.Length,
                MarkdownStructuralEditKind.MoveUp,
                new MarkdownSourceSpan(adjacentSpan.Start, currentText.Length));
            return true;
        }

        var leading = markdown[..currentSpan.Start];
        var separator = markdown[currentSpan.EndExclusive..adjacentSpan.Start];
        var trailing = markdown[adjacentSpan.EndExclusive..];
        var movedStart = currentSpan.Start + adjacentText.Length + separator.Length;
        result = new MarkdownStructuralEditResult(
            string.Concat(leading, adjacentText, separator, currentText, trailing),
            currentText,
            movedStart,
            currentText.Length,
            MarkdownStructuralEditKind.MoveDown,
            new MarkdownSourceSpan(movedStart, currentText.Length));
        return true;
    }

    public bool TryPromoteBlock(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        out MarkdownStructuralEditResult? result)
    {
        return TryAdjustStructure(parseResult, markdown, targetSpan, promote: true, out result);
    }

    public bool TryDemoteBlock(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        out MarkdownStructuralEditResult? result)
    {
        return TryAdjustStructure(parseResult, markdown, targetSpan, promote: false, out result);
    }

    public bool TrySplitBlock(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownEditorSession session,
        string editorText,
        int caretIndex,
        out MarkdownStructuralEditResult? result)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editorText);

        if (!TryResolveBlockContext(parseResult, session.SourceSpan, out var context))
        {
            result = null;
            return false;
        }

        var normalizedEditorText = MarkdownSourceEditing.NormalizeLineEndings(editorText);
        var splitIndex = Math.Clamp(caretIndex, 0, normalizedEditorText.Length);
        if (splitIndex <= 0 || splitIndex >= normalizedEditorText.Length)
        {
            result = null;
            return false;
        }

        switch (context.Block)
        {
            case ParagraphBlock:
            {
                var left = MarkdownSourceEditing.BuildParagraphMarkdown(normalizedEditorText[..splitIndex].TrimEnd());
                var right = MarkdownSourceEditing.BuildParagraphMarkdown(normalizedEditorText[splitIndex..].TrimStart());
                if (left.Length == 0 || right.Length == 0)
                {
                    result = null;
                    return false;
                }

                var separator = ResolveBlockSeparator(context.SourceSpan.Slice(markdown), markdown);
                var replacement = string.Concat(left, separator, right);
                var revealStart = context.SourceSpan.Start + left.Length + separator.Length;
                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
                    replacement,
                    revealStart,
                    right.Length,
                    MarkdownStructuralEditKind.Split,
                    new MarkdownSourceSpan(revealStart, right.Length));
                return true;
            }

            case HeadingBlock heading:
            {
                var left = MarkdownSourceEditing.NormalizeInlineMarkdown(normalizedEditorText[..splitIndex]);
                var right = MarkdownSourceEditing.NormalizeInlineMarkdown(normalizedEditorText[splitIndex..]);
                if (left.Length == 0 || right.Length == 0)
                {
                    result = null;
                    return false;
                }

                var separator = ResolveBlockSeparator(context.SourceSpan.Slice(markdown), markdown);
                var leftMarkdown = MarkdownSourceEditing.BuildHeadingMarkdown(heading.Level, left);
                var rightMarkdown = MarkdownSourceEditing.BuildParagraphMarkdown(right);
                var replacement = string.Concat(leftMarkdown, separator, rightMarkdown);
                var revealStart = context.SourceSpan.Start + leftMarkdown.Length + separator.Length;
                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
                    replacement,
                    revealStart,
                    rightMarkdown.Length,
                    MarkdownStructuralEditKind.Split,
                    new MarkdownSourceSpan(revealStart, rightMarkdown.Length));
                return true;
            }

            case CodeBlock:
            {
                var left = normalizedEditorText[..splitIndex].TrimEnd('\n');
                var right = normalizedEditorText[splitIndex..].TrimStart('\n');
                if (left.Length == 0 || right.Length == 0)
                {
                    result = null;
                    return false;
                }

                var existingSourceText = context.SourceSpan.Slice(markdown);
                var fenceInfo = ExtractCodeFenceInfo(existingSourceText);
                var separator = ResolveBlockSeparator(existingSourceText, markdown);
                var leftMarkdown = MarkdownSourceEditing.BuildCodeFenceWithInfoString(fenceInfo, left, existingSourceText);
                var rightMarkdown = MarkdownSourceEditing.BuildCodeFenceWithInfoString(fenceInfo, right, existingSourceText);
                var replacement = string.Concat(leftMarkdown, separator, rightMarkdown);
                var revealStart = context.SourceSpan.Start + leftMarkdown.Length + separator.Length;
                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
                    replacement,
                    revealStart,
                    rightMarkdown.Length,
                    MarkdownStructuralEditKind.Split,
                    new MarkdownSourceSpan(revealStart, rightMarkdown.Length));
                return true;
            }
        }

        result = null;
        return false;
    }

    public bool TryJoinWithNextParagraph(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        out MarkdownStructuralEditResult? result)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(markdown);

        if (!TryResolveBlockContext(parseResult, targetSpan, out var context) ||
            context.Block is not ParagraphBlock ||
            context.SiblingIndex >= context.Siblings.Count - 1)
        {
            result = null;
            return false;
        }

        var nextBlock = context.Siblings[context.SiblingIndex + 1];
        if (nextBlock is not ParagraphBlock)
        {
            result = null;
            return false;
        }

        var nextSpan = MarkdownSourceSpan.FromMarkdig(nextBlock.Span);
        if (nextSpan.IsEmpty)
        {
            result = null;
            return false;
        }

        var currentText = MarkdownSourceEditing.NormalizeInlineMarkdown(context.SourceSpan.Slice(markdown));
        var nextText = MarkdownSourceEditing.NormalizeInlineMarkdown(nextSpan.Slice(markdown));
        if (currentText.Length == 0 || nextText.Length == 0)
        {
            result = null;
            return false;
        }

        var replacement = MarkdownSourceEditing.BuildParagraphMarkdown(string.Concat(currentText, " ", nextText));
        var replacementSpan = new MarkdownSourceSpan(
            context.SourceSpan.Start,
            nextSpan.EndExclusive - context.SourceSpan.Start);
        result = new MarkdownStructuralEditResult(
            MarkdownSourceEditing.Replace(markdown, replacementSpan, replacement),
            replacement,
            context.SourceSpan.Start,
            replacement.Length,
            MarkdownStructuralEditKind.Join,
            new MarkdownSourceSpan(context.SourceSpan.Start, replacement.Length));
        return true;
    }

    private bool TryAdjustStructure(
        MarkdownParseResult parseResult,
        string markdown,
        MarkdownSourceSpan targetSpan,
        bool promote,
        out MarkdownStructuralEditResult? result)
    {
        if (!TryResolveBlockContext(parseResult, targetSpan, out var context))
        {
            result = null;
            return false;
        }

        switch (context.Block)
        {
            case HeadingBlock heading:
            {
                var newLevel = promote
                    ? Math.Max(1, heading.Level - 1)
                    : Math.Min(6, heading.Level + 1);
                if (newLevel == heading.Level)
                {
                    result = null;
                    return false;
                }

                var sourceText = context.SourceSpan.Slice(markdown);
                var replacement = MarkdownSourceEditing.BuildHeadingMarkdown(
                    newLevel,
                    MarkdownSourceEditing.ExtractHeadingText(sourceText, heading.Level));
                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
                    replacement,
                    context.SourceSpan.Start,
                    replacement.Length,
                    promote ? MarkdownStructuralEditKind.Promote : MarkdownStructuralEditKind.Demote,
                    new MarkdownSourceSpan(context.SourceSpan.Start, replacement.Length));
                return true;
            }

            case ListBlock:
            {
                var sourceText = context.SourceSpan.Slice(markdown);
                var replacement = AdjustListIndentation(sourceText, promote ? -2 : 2);
                if (string.Equals(sourceText, replacement, StringComparison.Ordinal))
                {
                    result = null;
                    return false;
                }

                result = new MarkdownStructuralEditResult(
                    MarkdownSourceEditing.Replace(markdown, context.SourceSpan, replacement),
                    replacement,
                    context.SourceSpan.Start,
                    replacement.Length,
                    promote ? MarkdownStructuralEditKind.Promote : MarkdownStructuralEditKind.Demote,
                    new MarkdownSourceSpan(context.SourceSpan.Start, replacement.Length));
                return true;
            }
        }

        result = null;
        return false;
    }

    private bool TryResolveBlockContext(
        MarkdownParseResult parseResult,
        MarkdownSourceSpan targetSpan,
        out MarkdownBlockContext context)
    {
        Block? bestBlock = null;
        ContainerBlock? bestParent = null;
        var bestLength = int.MaxValue;

        VisitBlock(parseResult.Document, parseResult.Document);
        if (bestBlock is null || bestParent is null)
        {
            context = default;
            return false;
        }

        var siblings = bestParent
            .OfType<Block>()
            .Where(block => !MarkdownSourceSpan.FromMarkdig(block.Span).IsEmpty)
            .OrderBy(block => MarkdownSourceSpan.FromMarkdig(block.Span).Start)
            .ToArray();
        var siblingIndex = Array.IndexOf(siblings, bestBlock);
        if (siblingIndex < 0)
        {
            context = default;
            return false;
        }

        context = new MarkdownBlockContext(bestBlock, siblings, siblingIndex, MarkdownSourceSpan.FromMarkdig(bestBlock.Span));
        return true;

        void VisitBlock(ContainerBlock parent, ContainerBlock container)
        {
            foreach (var child in container)
            {
                if (child is not Block block)
                {
                    continue;
                }

                var candidateSpan = MarkdownSourceSpan.FromMarkdig(block.Span);
                if (!candidateSpan.IsEmpty && Contains(candidateSpan, targetSpan) && candidateSpan.Length < bestLength)
                {
                    bestBlock = block;
                    bestParent = parent;
                    bestLength = candidateSpan.Length;
                }

                if (block is ContainerBlock nested)
                {
                    VisitBlock(nested, nested);
                }
            }
        }
    }

    private static string ExtractCodeFenceInfo(string sourceText)
    {
        var normalized = MarkdownSourceEditing.NormalizeLineEndings(sourceText);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var firstLineEnd = normalized.IndexOf('\n');
        var firstLine = firstLineEnd >= 0 ? normalized[..firstLineEnd] : normalized;
        if (firstLine.Length < 3)
        {
            return string.Empty;
        }

        var fenceCharacter = firstLine[0];
        if (fenceCharacter is not '`' and not '~')
        {
            return string.Empty;
        }

        var fenceLength = 0;
        while (fenceLength < firstLine.Length && firstLine[fenceLength] == fenceCharacter)
        {
            fenceLength++;
        }

        return fenceLength < 3
            ? string.Empty
            : firstLine[fenceLength..].Trim();
    }

    private static string ResolveBlockSeparator(string? preferredSourceText, string? fallbackSourceText)
    {
        var lineEnding = MarkdownSourceEditing.DetectLineEnding(
            preferredSourceText,
            MarkdownSourceEditing.DetectLineEnding(fallbackSourceText));
        return string.Concat(lineEnding, lineEnding);
    }

    private static string AdjustListIndentation(string sourceText, int offset)
    {
        var normalized = MarkdownSourceEditing.NormalizeLineEndings(sourceText);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index++)
        {
            if (offset > 0)
            {
                lines[index] = string.Concat(new string(' ', offset), lines[index]);
                continue;
            }

            var removeCount = Math.Min(lines[index].Length - lines[index].TrimStart().Length, Math.Abs(offset));
            lines[index] = lines[index][removeCount..];
        }

        return string.Join('\n', lines);
    }

    private static void CollectBlockSpans(ContainerBlock container, ICollection<MarkdownSourceSpan> spans)
    {
        foreach (var child in container)
        {
            if (child is not Block block)
            {
                continue;
            }

            var span = MarkdownSourceSpan.FromMarkdig(block.Span);
            if (!span.IsEmpty)
            {
                spans.Add(span);
            }

            if (block is ContainerBlock nested)
            {
                CollectBlockSpans(nested, spans);
            }
        }
    }

    private static void VisitMarkdownObject(MarkdownObject markdownObject, Action<MarkdownObject> visitor)
    {
        visitor(markdownObject);
        switch (markdownObject)
        {
            case ContainerBlock container:
                foreach (var child in container)
                {
                    VisitMarkdownObject(child, visitor);
                }

                break;
            case LeafBlock { Inline: { } inline }:
                VisitInline(inline, visitor);
                break;
        }
    }

    private static void VisitInline(Inline inline, Action<MarkdownObject> visitor)
    {
        for (Inline? current = inline; current is not null; current = current.NextSibling)
        {
            visitor(current);
            if (current is ContainerInline container && container.FirstChild is { } child)
            {
                VisitInline(child, visitor);
            }
        }
    }

    private static bool Contains(MarkdownSourceSpan outer, MarkdownSourceSpan inner)
    {
        return !outer.IsEmpty &&
               !inner.IsEmpty &&
               inner.Start >= outer.Start &&
               inner.EndExclusive <= outer.EndExclusive;
    }

    private readonly record struct MarkdownBlockContext(
        Block Block,
        IReadOnlyList<Block> Siblings,
        int SiblingIndex,
        MarkdownSourceSpan SourceSpan);
}
