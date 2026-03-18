using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexGui.Markdown.Controls;
using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Collapsible;

internal static class MarkdownCollapsibleRendering
{
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush SubtitleBrush = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IBrush DiagnosticBorderBrush = new SolidColorBrush(Color.Parse("#FECACA"));
    private static readonly IBrush DiagnosticBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush DiagnosticForeground = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush PlaceholderForeground = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IReadOnlyList<IMarkdownPlugin> NestedPlugins =
    [
        new CollapsibleMarkdownPlugin()
    ];
    private static readonly IMarkdownRenderController NestedRenderController = MarkdownRenderingServices.CreateController(NestedPlugins);
    private static readonly IMarkdownEditingService NestedEditingService = MarkdownRenderingServices.CreateEditingService(NestedPlugins);

    public static Control CreateBlockView(MarkdownCollapsibleDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var kindOption = MarkdownCollapsibleSyntax.ResolveKindOption(document.Kind);
        var presentation = MarkdownCalloutRendering.ResolvePresentation(document.Kind, fallbackTitle: kindOption.Label);
        var title = string.IsNullOrWhiteSpace(document.Summary)
            ? kindOption.Label
            : document.Summary;
        var subtitle = document.IsExpanded
            ? $"{kindOption.Label} section • Expanded by default"
            : $"{kindOption.Label} section • Collapsed by default";

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateBodyView(document.BodyMarkdown, renderContext)
            }
        };

        if (document.Diagnostics.Count > 0)
        {
            content.Children.Add(CreateDiagnosticsPanel(document.Diagnostics));
        }

        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = presentation.AccentBrush,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = subtitle,
                    FontSize = 12,
                    Foreground = SubtitleBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        var expander = new Expander
        {
            IsExpanded = document.IsExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = header,
            Content = content
        };

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        layout.Children.Add(new Border
        {
            Width = 4,
            Background = presentation.AccentBrush
        });

        var expanderHost = new Border
        {
            Padding = new Thickness(12),
            Child = expander
        };
        Grid.SetColumn(expanderHost, 1);
        layout.Children.Add(expanderHost);

        return new Border
        {
            Background = presentation.Background,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = layout
        };
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add collapsible body content to render a preview.",
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

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownCollapsibleDiagnostic> diagnostics)
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
