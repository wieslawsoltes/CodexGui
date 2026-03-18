using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Collapsible;

internal sealed class MarkdownCollapsibleDocument(
    string sourceMarkdown,
    string kind,
    string summary,
    bool isExpanded,
    string bodyMarkdown,
    int fenceLength,
    IReadOnlyList<MarkdownCollapsibleDiagnostic> diagnostics)
{
    public string SourceMarkdown { get; } = sourceMarkdown;

    public string Kind { get; } = kind;

    public string Summary { get; } = summary;

    public bool IsExpanded { get; } = isExpanded;

    public string BodyMarkdown { get; } = bodyMarkdown;

    public int FenceLength { get; } = fenceLength;

    public IReadOnlyList<MarkdownCollapsibleDiagnostic> Diagnostics { get; } = diagnostics;
}

internal sealed record MarkdownCollapsibleDiagnostic(string Message, MarkdownSourceSpan Span);

internal sealed record MarkdownCollapsibleDraft(
    string Kind,
    string Summary,
    string BodyMarkdown,
    bool IsExpanded,
    int FenceLength);

internal sealed record MarkdownCollapsibleKindOption(string Value, string Label, string Description)
{
    public override string ToString() => Label;
}
