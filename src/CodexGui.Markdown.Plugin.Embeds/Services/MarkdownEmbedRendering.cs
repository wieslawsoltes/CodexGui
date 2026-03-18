using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexGui.Markdown.Controls;
using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Embeds;

internal static class MarkdownEmbedRendering
{
    private static readonly IBrush VideoAccentBrush = new SolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush VideoBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush AudioAccentBrush = new SolidColorBrush(Color.Parse("#059669"));
    private static readonly IBrush AudioBackground = new SolidColorBrush(Color.Parse("#ECFDF5"));
    private static readonly IBrush SocialAccentBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush SocialBackground = new SolidColorBrush(Color.Parse("#EFF6FF"));
    private static readonly IBrush DocumentAccentBrush = new SolidColorBrush(Color.Parse("#7C3AED"));
    private static readonly IBrush DocumentBackground = new SolidColorBrush(Color.Parse("#F5F3FF"));
    private static readonly IBrush NeutralAccentBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush NeutralBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush DiagnosticBorderBrush = new SolidColorBrush(Color.Parse("#FECACA"));
    private static readonly IBrush DiagnosticBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush DiagnosticForeground = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush PlaceholderForeground = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IReadOnlyList<IMarkdownPlugin> NestedPlugins =
    [
        new EmbedsMarkdownPlugin()
    ];
    private static readonly IMarkdownRenderController NestedRenderController = MarkdownRenderingServices.CreateController(NestedPlugins);
    private static readonly IMarkdownEditingService NestedEditingService = MarkdownRenderingServices.CreateEditingService(NestedPlugins);

    public static Control CreateBlockView(MarkdownEmbedDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var previewMarkdown = MarkdownEmbedSyntax.BuildPreviewMarkdown(document);
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateBodyView(previewMarkdown, renderContext)
            }
        };

        if (document.Diagnostics.Count > 0)
        {
            body.Children.Add(CreateDiagnosticsPanel(document.Diagnostics));
        }

        var presentation = ResolvePresentation(document.Kind);
        return MarkdownCalloutRendering.CreateCalloutSurface(
            MarkdownEmbedSyntax.ResolveDisplayTitle(document),
            BuildSubtitle(document),
            body,
            presentation.AccentBrush,
            presentation.Background);
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add a resource URL or supporting markdown to render a safe media preview card.",
                Foreground = PlaceholderForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        return new MarkdownTextBlock
        {
            Markdown = markdown,
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            IsEditingEnabled = false,
            EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
            FontSize = renderContext.FontSize,
            FontFamily = renderContext.FontFamily,
            Foreground = renderContext.Foreground,
            TextWrapping = renderContext.TextWrapping,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static string? BuildSubtitle(MarkdownEmbedDocument document)
    {
        var kindLabel = MarkdownEmbedSyntax.ResolveKindOption(document.Kind).Label;
        var provider = document.Provider;
        if (string.IsNullOrWhiteSpace(provider) &&
            MarkdownEmbedSyntax.TryCreateAbsoluteUri(document.Url, out var uri) &&
            uri is not null)
        {
            provider = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }

        return string.IsNullOrWhiteSpace(provider) ? kindLabel : $"{kindLabel} • {provider}";
    }

    private static (IBrush AccentBrush, IBrush Background) ResolvePresentation(string? kind)
    {
        return MarkdownEmbedSyntax.NormalizeKind(kind) switch
        {
            "audio" => (AudioAccentBrush, AudioBackground),
            "document" => (DocumentAccentBrush, DocumentBackground),
            "social" => (SocialAccentBrush, SocialBackground),
            "video" => (VideoAccentBrush, VideoBackground),
            _ => (NeutralAccentBrush, NeutralBackground)
        };
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownEmbedDiagnostic> diagnostics)
    {
        var panel = new StackPanel
        {
            Spacing = 4
        };

        foreach (var diagnostic in diagnostics)
        {
            panel.Children.Add(new TextBlock
            {
                Text = diagnostic.Message,
                Foreground = DiagnosticForeground,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Background = DiagnosticBackground,
            BorderBrush = DiagnosticBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            Child = panel
        };
    }
}
