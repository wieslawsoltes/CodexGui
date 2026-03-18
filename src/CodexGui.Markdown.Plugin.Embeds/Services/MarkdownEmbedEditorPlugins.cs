using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexGui.Markdown.Services;
using Markdig.Extensions.CustomContainers;

namespace CodexGui.Markdown.Plugin.Embeds;

internal sealed class EmbedBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 15;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "embed-video",
            "Video card",
            MarkdownEditorFeature.Figure,
            MarkdownEmbedSyntax.BuildEmbed(
                "video",
                "https://www.youtube.com/watch?v=example",
                "Release walkthrough",
                "YouTube",
                "https://img.youtube.com/vi/example/maxresdefault.jpg",
                "Use supporting markdown here to explain why the media matters or what to watch for."),
            "Insert a safe video embed card.");

        yield return new MarkdownBlockTemplate(
            "embed-audio",
            "Audio card",
            MarkdownEditorFeature.Figure,
            MarkdownEmbedSyntax.BuildEmbed(
                "audio",
                "https://open.spotify.com/episode/example",
                "Stand-up recap",
                "Spotify",
                string.Empty,
                "Summarize the episode, transcript, or context alongside the media link."),
            "Insert a safe audio embed card.");

        yield return new MarkdownBlockTemplate(
            "embed-document",
            "Document card",
            MarkdownEditorFeature.Figure,
            MarkdownEmbedSyntax.BuildEmbed(
                "document",
                "https://example.com/design-review.pdf",
                "Design review packet",
                "Example Docs",
                string.Empty,
                "Link to supporting decks, PDFs, or knowledge-base pages without embedding unsafe remote HTML."),
            "Insert a safe document or deck card.");
    }
}

internal sealed class EmbedBlockEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => EmbedsMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Figure;

    public int Order => -40;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<CustomContainer>(out var customContainer, out var depth) ||
            customContainer is null ||
            !MarkdownEmbedSyntax.IsEmbedInfo(customContainer.Info))
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(customContainer.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(customContainer, sourceSpan, customContainer.Line, customContainer.Column),
            depth,
            "Embed card");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is CustomContainer customContainer && MarkdownEmbedSyntax.IsEmbedInfo(customContainer.Info)
            ? MarkdownEmbedEditorUiFactory.CreateEditor(context)
            : null;
    }
}

internal static class MarkdownEmbedEditorUiFactory
{
    public static Control CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = CreateDraft(context.SourceText);
        var kindComboBox = new ComboBox
        {
            ItemsSource = MarkdownEmbedSyntax.AvailableKinds,
            SelectedItem = MarkdownEmbedSyntax.ResolveKindOption(draft.Kind),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = MarkdownEditorUiFactory.InputBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6)
        };
        var kindDescription = MarkdownEditorUiFactory.CreateInfoText(((MarkdownEmbedKindOption?)kindComboBox.SelectedItem)?.Description ?? "Safe media card");
        var urlEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.Url, acceptsReturn: false, minHeight: 42);
        var titleEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.Title, acceptsReturn: false, minHeight: 42);
        var providerEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.Provider, acceptsReturn: false, minHeight: 42);
        var posterEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.PosterUrl, acceptsReturn: false, minHeight: 42);
        var bodyEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.BodyMarkdown, acceptsReturn: true, minHeight: Math.Max(context.RenderContext.FontSize * 6, 160));

        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(urlEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(titleEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(providerEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(posterEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineParagraphStyle(bodyEditor, context.RenderContext.FontSize);
        }
        else
        {
            ApplyCardEditorStyle(urlEditor, context, metadata: true);
            ApplyCardEditorStyle(titleEditor, context, metadata: true);
            ApplyCardEditorStyle(providerEditor, context, metadata: true);
            ApplyCardEditorStyle(posterEditor, context, metadata: true);
            ApplyCardEditorStyle(bodyEditor, context, metadata: false);
        }

        var previewHost = CreatePreviewHost(context);

        string BuildMarkdown()
        {
            return MarkdownEmbedSyntax.BuildEmbed(
                ((MarkdownEmbedKindOption?)kindComboBox.SelectedItem)?.Value,
                urlEditor.Text,
                titleEditor.Text,
                providerEditor.Text,
                posterEditor.Text,
                bodyEditor.Text,
                draft.FenceLength,
                context.SourceText);
        }

        void UpdatePreview()
        {
            kindDescription.Text = ((MarkdownEmbedKindOption?)kindComboBox.SelectedItem)?.Description ?? "Safe media card";
            previewHost.Child = MarkdownEmbedRendering.CreateBlockView(
                MarkdownEmbedParser.Parse(BuildMarkdown()),
                context.RenderContext);
        }

        kindComboBox.SelectionChanged += (_, _) => UpdatePreview();
        urlEditor.TextChanged += (_, _) => UpdatePreview();
        titleEditor.TextChanged += (_, _) => UpdatePreview();
        providerEditor.TextChanged += (_, _) => UpdatePreview();
        posterEditor.TextChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit safe media-card metadata and supporting markdown without hand-authoring the :::embed container syntax."));
        }

        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Card type"));
        body.Children.Add(kindComboBox);
        body.Children.Add(kindDescription);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Resource URL"));
        body.Children.Add(urlEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Title"));
        body.Children.Add(titleEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Provider label"));
        body.Children.Add(providerEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Poster image URL"));
        body.Children.Add(posterEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Supporting markdown"));
        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => bodyEditor)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => bodyEditor));
        body.Children.Add(bodyEditor);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit embed card",
            "Safe media card editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            urlEditor,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    private static MarkdownEmbedDraft CreateDraft(string sourceText)
    {
        var document = MarkdownEmbedParser.Parse(sourceText);
        return new MarkdownEmbedDraft(
            document.Kind,
            document.Url,
            document.Title,
            document.Provider,
            document.PosterUrl,
            document.BodyMarkdown,
            document.FenceLength);
    }

    private static Border CreatePreviewHost(MarkdownEditorPluginContext context)
    {
        return new Border
        {
            Background = MarkdownEditorUiFactory.SectionBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? new Thickness(8, 6) : new Thickness(10)
        };
    }

    private static void ApplyCardEditorStyle(TextBox textBox, MarkdownEditorPluginContext context, bool metadata)
    {
        textBox.FontSize = metadata ? Math.Max(context.RenderContext.FontSize - 1, 12) : context.RenderContext.FontSize;
        textBox.FontFamily = context.RenderContext.FontFamily;
        textBox.Foreground = context.RenderContext.Foreground ?? MarkdownEditorUiFactory.EditorForeground;
        textBox.Background = MarkdownEditorUiFactory.InputBackground;
        textBox.BorderBrush = MarkdownEditorUiFactory.BorderBrush;
        textBox.BorderThickness = new Thickness(1);
        textBox.CornerRadius = new CornerRadius(metadata ? 6 : 10);
        textBox.Padding = metadata ? new Thickness(8, 6) : new Thickness(12, 10);
        textBox.VerticalContentAlignment = metadata ? VerticalAlignment.Center : VerticalAlignment.Top;
    }
}
