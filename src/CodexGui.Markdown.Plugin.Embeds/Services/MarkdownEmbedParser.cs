using System.Text;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Embeds;

internal static class MarkdownEmbedParser
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build());

    public static MarkdownEmbedDocument Parse(CustomContainer customContainer, MarkdownParseResult parseResult)
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
            source = MarkdownEmbedSyntax.BuildEmbed(
                customContainer.Arguments,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                fallbackBody,
                preferredFenceLength: customContainer.OpeningFencedCharCount);
        }

        return Parse(source, customContainer.Info, customContainer.Arguments);
    }

    public static MarkdownEmbedDocument Parse(string? markdown)
    {
        return Parse(markdown, fallbackInfo: null, fallbackArguments: null);
    }

    private static MarkdownEmbedDocument Parse(string? markdown, string? fallbackInfo, string? fallbackArguments)
    {
        var source = MarkdownEmbedSyntax.NormalizeBlockText(markdown);
        List<MarkdownEmbedDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new MarkdownEmbedDiagnostic("Embed content is empty.", MarkdownSourceSpan.Empty));
            return new MarkdownEmbedDocument(
                string.Empty,
                MarkdownEmbedSyntax.DefaultKind,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                3,
                diagnostics);
        }

        var document = Markdig.Markdown.Parse(source, Pipeline.Value);
        CustomContainer? embedContainer = null;
        foreach (var block in document)
        {
            switch (block)
            {
                case CustomContainer parsedContainer when MarkdownEmbedSyntax.IsEmbedInfo(parsedContainer.Info) && embedContainer is null:
                    embedContainer = parsedContainer;
                    break;
                case BlankLineBlock:
                    break;
                default:
                    diagnostics.Add(new MarkdownEmbedDiagnostic(
                        $"Unexpected block '{block.GetType().Name}' inside embed source.",
                        MarkdownSourceSpan.FromMarkdig(block.Span)));
                    break;
            }
        }

        if (embedContainer is null)
        {
            diagnostics.Add(new MarkdownEmbedDiagnostic(
                "Markdig did not produce an embed custom container from the provided markdown.",
                MarkdownSourceSpan.Empty));
        }

        var kind = MarkdownEmbedSyntax.NormalizeKind(embedContainer?.Arguments ?? fallbackArguments);
        var containerBody = MarkdownEmbedSyntax.ExtractContainerBody(source);
        var metadata = MarkdownEmbedSyntax.ParseMetadata(containerBody, diagnostics);
        var provider = MarkdownEmbedSyntax.ResolveProvider(metadata.Provider, metadata.Url);

        if (string.IsNullOrWhiteSpace(metadata.Url))
        {
            diagnostics.Add(new MarkdownEmbedDiagnostic(
                "Embed URL is required.",
                MarkdownSourceSpan.Empty));
        }
        else if (!MarkdownEmbedSyntax.TryCreateAbsoluteUri(metadata.Url, out _))
        {
            diagnostics.Add(new MarkdownEmbedDiagnostic(
                "Embed URL should be an absolute URI.",
                MarkdownSourceSpan.Empty));
        }

        if (!string.IsNullOrWhiteSpace(metadata.PosterUrl) &&
            !MarkdownEmbedSyntax.TryCreateAbsoluteUri(metadata.PosterUrl, out _))
        {
            diagnostics.Add(new MarkdownEmbedDiagnostic(
                "Poster URL should be an absolute URI.",
                MarkdownSourceSpan.Empty));
        }

        var fenceLength = embedContainer is null
            ? MarkdownEmbedSyntax.ReadFenceLength(source)
            : Math.Max(embedContainer.OpeningFencedCharCount, MarkdownEmbedSyntax.ReadFenceLength(source));

        return new MarkdownEmbedDocument(
            source,
            kind,
            metadata.Url,
            metadata.Title,
            provider,
            metadata.PosterUrl,
            metadata.BodyMarkdown,
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

internal static class MarkdownEmbedSyntax
{
    public const string DefaultKind = "website";

    private static readonly HashSet<string> InfoAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "embed",
        "embeds",
        "media"
    };

    private static readonly HashSet<string> KnownMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url",
        "title",
        "provider",
        "poster"
    };

    private static readonly MarkdownEmbedKindOption[] KindOptions =
    [
        new("website", "Website card", "Render a safe external website or article card."),
        new("video", "Video card", "Render a safe video preview card that opens externally."),
        new("audio", "Audio card", "Render a safe audio or podcast card."),
        new("social", "Social post", "Render a safe social or community post card."),
        new("document", "Document card", "Render a safe document, deck, or file preview card.")
    ];

    public static IReadOnlyList<MarkdownEmbedKindOption> AvailableKinds => KindOptions;

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

    public static bool IsEmbedInfo(string? info)
    {
        return InfoAliases.Contains(NormalizeInlineText(info).ToLowerInvariant());
    }

    public static string NormalizeKind(string? kind, string? fallbackKind = null)
    {
        var normalized = NormalizeInlineText(kind);
        if (normalized.Contains(' '))
        {
            normalized = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? normalized;
        }

        return normalized.ToLowerInvariant() switch
        {
            "audio" or "podcast" or "spotify" or "soundcloud" => "audio",
            "document" or "doc" or "pdf" or "deck" or "slides" => "document",
            "social" or "post" or "twitter" or "x" => "social",
            "video" or "movie" or "youtube" or "loom" or "vimeo" => "video",
            "website" or "web" or "link" or "page" => "website",
            _ => string.IsNullOrWhiteSpace(normalized) ? fallbackKind ?? DefaultKind : normalized.ToLowerInvariant()
        };
    }

    public static MarkdownEmbedKindOption ResolveKindOption(string? kind)
    {
        var normalized = NormalizeKind(kind);
        foreach (var option in KindOptions)
        {
            if (string.Equals(option.Value, normalized, StringComparison.Ordinal))
            {
                return option;
            }
        }

        var label = MarkdownCalloutRendering.FormatLabel(normalized);
        return new MarkdownEmbedKindOption(normalized, string.IsNullOrWhiteSpace(label) ? "Embed card" : label, "Safe media card");
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

    public static string ExtractContainerBody(string source)
    {
        var normalized = NormalizeLineEndings(source);
        var firstNewLine = normalized.IndexOf('\n');
        var lastNewLine = normalized.LastIndexOf('\n');
        if (firstNewLine < 0 || lastNewLine <= firstNewLine)
        {
            return string.Empty;
        }

        return NormalizeBlockText(normalized.Substring(firstNewLine + 1, lastNewLine - firstNewLine - 1));
    }

    public static MarkdownEmbedMetadata ParseMetadata(string containerBody, List<MarkdownEmbedDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var normalized = NormalizeLineEndings(containerBody);
        if (normalized.Length == 0)
        {
            return new MarkdownEmbedMetadata(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyStartIndex = lines.Length;
        var sawMetadata = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (sawMetadata)
                {
                    bodyStartIndex = index + 1;
                    break;
                }

                continue;
            }

            if (!TrySplitMetadataLine(line, out var key, out var value))
            {
                bodyStartIndex = index;
                break;
            }

            sawMetadata = true;
            bodyStartIndex = index + 1;
            if (!KnownMetadataKeys.Contains(key))
            {
                diagnostics.Add(new MarkdownEmbedDiagnostic(
                    $"Unknown embed metadata key '{key}'.",
                    MarkdownSourceSpan.Empty));
                continue;
            }

            metadata[key] = value;
        }

        if (!sawMetadata && bodyStartIndex == lines.Length)
        {
            bodyStartIndex = 0;
        }

        var bodyMarkdown = bodyStartIndex < lines.Length
            ? NormalizeBlockText(string.Join('\n', lines[bodyStartIndex..]))
            : string.Empty;

        return new MarkdownEmbedMetadata(
            metadata.GetValueOrDefault("url", string.Empty),
            metadata.GetValueOrDefault("title", string.Empty),
            metadata.GetValueOrDefault("provider", string.Empty),
            metadata.GetValueOrDefault("poster", string.Empty),
            bodyMarkdown);
    }

    public static string BuildEmbed(
        string? kind,
        string? url,
        string? title,
        string? provider,
        string? posterUrl,
        string? bodyMarkdown,
        int preferredFenceLength = 3,
        string? existingSourceText = null)
    {
        var normalizedKind = NormalizeKind(kind);
        var normalizedUrl = NormalizeInlineText(url);
        var normalizedTitle = NormalizeInlineText(title);
        var normalizedProvider = NormalizeInlineText(provider);
        var normalizedPoster = NormalizeInlineText(posterUrl);
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        var lineEnding = DetectLineEnding(existingSourceText);

        var contentBuilder = new StringBuilder();
        contentBuilder.Append("url: ").Append(normalizedUrl);
        if (normalizedTitle.Length > 0)
        {
            contentBuilder.Append(lineEnding).Append("title: ").Append(normalizedTitle);
        }

        if (normalizedProvider.Length > 0)
        {
            contentBuilder.Append(lineEnding).Append("provider: ").Append(normalizedProvider);
        }

        if (normalizedPoster.Length > 0)
        {
            contentBuilder.Append(lineEnding).Append("poster: ").Append(normalizedPoster);
        }

        if (normalizedBody.Length > 0)
        {
            contentBuilder
                .Append(lineEnding)
                .Append(lineEnding)
                .Append(NormalizeLineEndings(normalizedBody, lineEnding));
        }

        var content = contentBuilder.ToString();
        var fenceLength = ResolveFenceLength(content, preferredFenceLength);
        var fence = new string(':', fenceLength);

        var builder = new StringBuilder();
        builder.Append(fence).Append("embed");
        if (normalizedKind.Length > 0)
        {
            builder.Append(' ').Append(normalizedKind);
        }

        builder.Append(lineEnding).Append(content).Append(lineEnding).Append(fence);
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

    public static bool TryCreateAbsoluteUri(string? value, out Uri? uri)
    {
        var normalized = NormalizeInlineText(value);
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    public static string ResolveProvider(string? provider, string? url)
    {
        var explicitProvider = NormalizeInlineText(provider);
        if (explicitProvider.Length > 0)
        {
            return explicitProvider;
        }

        return TryCreateAbsoluteUri(url, out var uri) && uri is not null
            ? InferProviderFromUri(uri)
            : string.Empty;
    }

    public static string ResolveDisplayTitle(MarkdownEmbedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!string.IsNullOrWhiteSpace(document.Title))
        {
            return document.Title;
        }

        if (!string.IsNullOrWhiteSpace(document.Provider))
        {
            return document.Provider;
        }

        return ResolveKindOption(document.Kind).Label;
    }

    public static string BuildPreviewMarkdown(MarkdownEmbedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();
        if (TryCreateAbsoluteUri(document.PosterUrl, out var posterUri) && posterUri is not null)
        {
            builder
                .Append("![")
                .Append(EscapeMarkdownText(ResolveDisplayTitle(document)))
                .Append("](")
                .Append(posterUri.AbsoluteUri)
                .Append(')');
        }

        if (!string.IsNullOrWhiteSpace(document.BodyMarkdown))
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append(NormalizeBlockText(document.BodyMarkdown));
        }

        if (TryCreateAbsoluteUri(document.Url, out var resourceUri) && resourceUri is not null)
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder
                .Append("[Open ")
                .Append(EscapeMarkdownText(ResolveKindOption(document.Kind).Label))
                .Append("](")
                .Append(resourceUri.AbsoluteUri)
                .Append(')');
        }
        else if (!string.IsNullOrWhiteSpace(document.Url))
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("Open link: ").Append(NormalizeInlineText(document.Url));
        }

        return builder.ToString();
    }

    private static int ResolveFenceLength(string content, int preferredFenceLength)
    {
        var requiredFenceLength = Math.Max(preferredFenceLength, 3);
        if (content.Length == 0)
        {
            return requiredFenceLength;
        }

        foreach (var line in NormalizeLineEndings(content).Split('\n', StringSplitOptions.None))
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

    private static bool TrySplitMetadataLine(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var keyCandidate = line[..separatorIndex].Trim();
        if (keyCandidate.Length == 0 || !char.IsLetter(keyCandidate[0]))
        {
            return false;
        }

        foreach (var ch in keyCandidate)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_')
            {
                return false;
            }
        }

        key = keyCandidate.ToLowerInvariant();
        value = NormalizeInlineText(line[(separatorIndex + 1)..]);
        return true;
    }

    private static string InferProviderFromUri(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        if (host == "youtu.be" || host.Contains("youtube", StringComparison.Ordinal))
        {
            return "YouTube";
        }

        if (host.Contains("spotify", StringComparison.Ordinal))
        {
            return "Spotify";
        }

        if (host.Contains("soundcloud", StringComparison.Ordinal))
        {
            return "SoundCloud";
        }

        if (host.Contains("vimeo", StringComparison.Ordinal))
        {
            return "Vimeo";
        }

        if (host.Contains("loom", StringComparison.Ordinal))
        {
            return "Loom";
        }

        if (host.Contains("figma", StringComparison.Ordinal))
        {
            return "Figma";
        }

        if (host.Contains("github", StringComparison.Ordinal))
        {
            return "GitHub";
        }

        if (host.Contains("notion", StringComparison.Ordinal))
        {
            return "Notion";
        }

        if (host == "x.com" || host.Contains("twitter", StringComparison.Ordinal))
        {
            return "X / Twitter";
        }

        var normalizedHost = host.StartsWith("www.", StringComparison.Ordinal)
            ? host[4..]
            : host;
        var segments = normalizedHost.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var label = segments.Length >= 2 ? segments[^2] : normalizedHost;
        return MarkdownCalloutRendering.FormatLabel(label);
    }

    private static string EscapeMarkdownText(string value)
    {
        return NormalizeInlineText(value)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }
}
