using Markdig.Syntax;

namespace CodexGui.Markdown.Services;

public sealed class MarkdownOutlineItem
{
    private readonly List<MarkdownOutlineItem> _children = [];

    internal MarkdownOutlineItem(string title, int level, MarkdownSourceSpan sourceSpan, int line, int column)
    {
        Title = title;
        Level = level;
        SourceSpan = sourceSpan;
        Line = line;
        Column = column;
    }

    public string Title { get; }

    public int Level { get; }

    public MarkdownSourceSpan SourceSpan { get; }

    public int Line { get; }

    public int Column { get; }

    public IReadOnlyList<MarkdownOutlineItem> Children => _children;

    internal void AddChild(MarkdownOutlineItem child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
    }

    public override string ToString() => Title;
}

public sealed class MarkdownOutlineSnapshot(
    IReadOnlyList<MarkdownOutlineItem> items,
    IReadOnlyList<MarkdownOutlineItem> breadcrumbs,
    MarkdownOutlineItem? activeItem)
{
    public static MarkdownOutlineSnapshot Empty { get; } =
        new MarkdownOutlineSnapshot(Array.Empty<MarkdownOutlineItem>(), Array.Empty<MarkdownOutlineItem>(), null);

    public IReadOnlyList<MarkdownOutlineItem> Items { get; } = items;

    public IReadOnlyList<MarkdownOutlineItem> Breadcrumbs { get; } = breadcrumbs;

    public MarkdownOutlineItem? ActiveItem { get; } = activeItem;
}

internal sealed class MarkdownOutlineService
{
    public MarkdownOutlineSnapshot BuildSnapshot(MarkdownParseResult? parseResult, MarkdownSourceSpan? activeSourceSpan)
    {
        if (parseResult?.Document is not { Count: > 0 } document)
        {
            return MarkdownOutlineSnapshot.Empty;
        }

        List<HeadingBlock> headings = [];
        CollectHeadings(document, headings);
        if (headings.Count == 0)
        {
            return MarkdownOutlineSnapshot.Empty;
        }

        List<MarkdownOutlineItem> roots = [];
        List<MarkdownOutlineItem> stack = [];
        List<MarkdownOutlineItem> activePath = [];

        foreach (var heading in headings)
        {
            var sourceSpan = MarkdownSourceSpan.FromMarkdig(heading.Span);
            if (sourceSpan.IsEmpty)
            {
                continue;
            }

            var title = MarkdownSourceEditing.ExtractHeadingText(
                sourceSpan.Slice(parseResult.OriginalMarkdown),
                heading.Level);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = $"Heading {heading.Level}";
            }

            var item = new MarkdownOutlineItem(title, heading.Level, sourceSpan, heading.Line, heading.Column);

            while (stack.Count > 0 && stack[^1].Level >= item.Level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (stack.Count == 0)
            {
                roots.Add(item);
            }
            else
            {
                stack[^1].AddChild(item);
            }

            stack.Add(item);

            if (activeSourceSpan is { } active &&
                item.SourceSpan.Start <= active.Start)
            {
                activePath = [.. stack];
            }
        }

        var activeItem = activePath.Count > 0 ? activePath[^1] : null;
        return new MarkdownOutlineSnapshot(roots, activePath, activeItem);
    }

    private static void CollectHeadings(ContainerBlock container, ICollection<HeadingBlock> headings)
    {
        foreach (var child in container)
        {
            if (child is HeadingBlock heading)
            {
                headings.Add(heading);
            }

            if (child is ContainerBlock nestedContainer)
            {
                CollectHeadings(nestedContainer, headings);
            }
        }
    }
}
