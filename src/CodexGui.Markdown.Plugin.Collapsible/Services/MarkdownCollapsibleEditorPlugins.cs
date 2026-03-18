using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexGui.Markdown.Services;
using Markdig.Extensions.CustomContainers;

namespace CodexGui.Markdown.Plugin.Collapsible;

internal sealed class CollapsibleBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 15;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "collapsible-details",
            "Details section",
            MarkdownEditorFeature.CustomContainer,
            MarkdownCollapsibleSyntax.BuildCollapsible(
                "details",
                "Implementation notes",
                "Use details blocks for optional context, release notes, or FAQ answers.",
                isExpanded: false),
            "Insert a collapsible details block.");

        yield return new MarkdownBlockTemplate(
            "collapsible-expanded",
            "Expanded details",
            MarkdownEditorFeature.CustomContainer,
            MarkdownCollapsibleSyntax.BuildCollapsible(
                "details",
                "Deployment checklist",
                "- verify plugin registration\n- confirm rendering parity\n- capture validation output",
                isExpanded: true),
            "Insert a details block that starts expanded.");
    }
}

internal sealed class CollapsibleBlockEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => CollapsibleMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.CustomContainer;

    public int Order => -45;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<CustomContainer>(out var customContainer, out var depth) ||
            customContainer is null ||
            !MarkdownCollapsibleSyntax.IsCollapsibleInfo(customContainer.Info))
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
            "Collapsible section");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is CustomContainer customContainer && MarkdownCollapsibleSyntax.IsCollapsibleInfo(customContainer.Info)
            ? MarkdownCollapsibleEditorUiFactory.CreateEditor(context)
            : null;
    }
}

internal static class MarkdownCollapsibleEditorUiFactory
{
    public static Control CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = CreateDraft(context.SourceText);
        var kindComboBox = new ComboBox
        {
            ItemsSource = MarkdownCollapsibleSyntax.AvailableKinds,
            SelectedItem = MarkdownCollapsibleSyntax.ResolveKindOption(draft.Kind),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = MarkdownEditorUiFactory.InputBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6)
        };
        var kindDescription = MarkdownEditorUiFactory.CreateInfoText(((MarkdownCollapsibleKindOption?)kindComboBox.SelectedItem)?.Description ?? "Collapsible section");
        var summaryEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.Summary, acceptsReturn: false, minHeight: 42);
        var bodyEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.BodyMarkdown, acceptsReturn: true, minHeight: Math.Max(context.RenderContext.FontSize * 6, 160));
        var isExpandedCheckBox = new CheckBox
        {
            Content = "Start expanded",
            IsChecked = draft.IsExpanded
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(summaryEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineParagraphStyle(bodyEditor, context.RenderContext.FontSize);
        }
        else
        {
            ApplyCardEditorStyle(summaryEditor, context, metadata: true);
            ApplyCardEditorStyle(bodyEditor, context, metadata: false);
        }

        var previewHost = CreatePreviewHost(context);

        string BuildMarkdown()
        {
            return MarkdownCollapsibleSyntax.BuildCollapsible(
                ((MarkdownCollapsibleKindOption?)kindComboBox.SelectedItem)?.Value,
                summaryEditor.Text,
                bodyEditor.Text,
                isExpandedCheckBox.IsChecked == true,
                draft.FenceLength,
                context.SourceText);
        }

        void UpdatePreview()
        {
            kindDescription.Text = ((MarkdownCollapsibleKindOption?)kindComboBox.SelectedItem)?.Description ?? "Collapsible section";
            previewHost.Child = MarkdownCollapsibleRendering.CreateBlockView(
                MarkdownCollapsibleParser.Parse(BuildMarkdown()),
                context.RenderContext);
        }

        kindComboBox.SelectionChanged += (_, _) => UpdatePreview();
        summaryEditor.TextChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        isExpandedCheckBox.IsCheckedChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the summary, initial expansion state, and body markdown without manually maintaining the :::details fence syntax."));
        }

        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Block type"));
        body.Children.Add(kindComboBox);
        body.Children.Add(kindDescription);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Summary"));
        body.Children.Add(summaryEditor);
        body.Children.Add(isExpandedCheckBox);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Collapsible body markdown"));
        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => bodyEditor)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => bodyEditor));
        body.Children.Add(bodyEditor);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit collapsible section",
            "Collapsible editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            summaryEditor,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    private static MarkdownCollapsibleDraft CreateDraft(string sourceText)
    {
        var document = MarkdownCollapsibleParser.Parse(sourceText);
        return new MarkdownCollapsibleDraft(
            document.Kind,
            document.Summary,
            document.BodyMarkdown,
            document.IsExpanded,
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
