using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Embeds;

internal sealed class MarkdownEmbedDocument(
    string sourceMarkdown,
    string kind,
    string url,
    string title,
    string provider,
    string posterUrl,
    string bodyMarkdown,
    int fenceLength,
    IReadOnlyList<MarkdownEmbedDiagnostic> diagnostics)
{
    public string SourceMarkdown { get; } = sourceMarkdown;

    public string Kind { get; } = kind;

    public string Url { get; } = url;

    public string Title { get; } = title;

    public string Provider { get; } = provider;

    public string PosterUrl { get; } = posterUrl;

    public string BodyMarkdown { get; } = bodyMarkdown;

    public int FenceLength { get; } = fenceLength;

    public IReadOnlyList<MarkdownEmbedDiagnostic> Diagnostics { get; } = diagnostics;
}

internal sealed record MarkdownEmbedDiagnostic(string Message, MarkdownSourceSpan Span);

internal sealed record MarkdownEmbedDraft(
    string Kind,
    string Url,
    string Title,
    string Provider,
    string PosterUrl,
    string BodyMarkdown,
    int FenceLength);

internal sealed record MarkdownEmbedKindOption(string Value, string Label, string Description)
{
    public override string ToString() => Label;
}

internal sealed record MarkdownEmbedMetadata(
    string Url,
    string Title,
    string Provider,
    string PosterUrl,
    string BodyMarkdown);
