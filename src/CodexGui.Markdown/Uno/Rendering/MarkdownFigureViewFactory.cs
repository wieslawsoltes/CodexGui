using CodexGui.Markdown.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexGui.Markdown.Controls;

internal static class MarkdownFigureViewFactory
{
    public static UIElement CreateBlockView(
        MarkdownFigureBlock block,
        Func<string, double, UIElement> renderNested,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(renderNested);

        var captionStyle = new MarkdownNestedContentStyle(
            FontSize: Math.Max(fontSize - 1, 11),
            Foreground: MarkdownBrushes.Tone("#6E6E6E"),
            TextAlignment: TextAlignment.Center);

        var content = new StackPanel
        {
            Spacing = 10
        };

        if (!string.IsNullOrWhiteSpace(block.LeadingCaptionMarkdown))
        {
            content.Children.Add(new Border
            {
                Background = MarkdownBrushes.Tone("#EFF6FF"),
                BorderBrush = MarkdownBrushes.Tone("#BFDBFE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Child = MarkdownNestedContentStyler.Apply(
                    renderNested(block.LeadingCaptionMarkdown, 0),
                    captionStyle with { FontWeight = FontWeights.SemiBold })
            });
        }

        content.Children.Add(new Border
        {
            BorderBrush = MarkdownBrushes.Tone("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = string.IsNullOrWhiteSpace(block.BodyMarkdown)
                ? new TextBlock
                {
                    Text = "Add figure body content to render a preview.",
                    Foreground = MarkdownBrushes.Tone("#6E6E6E"),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                }
                : renderNested(block.BodyMarkdown, 24)
        });

        if (!string.IsNullOrWhiteSpace(block.TrailingCaptionMarkdown))
        {
            content.Children.Add(new Border
            {
                Padding = new Thickness(8, 4, 8, 0),
                Child = MarkdownNestedContentStyler.Apply(
                    renderNested(block.TrailingCaptionMarkdown, 12),
                    captionStyle)
            });
        }

        return new Border
        {
            Background = MarkdownBrushes.Tone("#F8FAFC"),
            BorderBrush = MarkdownBrushes.Tone("#D0D7DE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Figure",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = MarkdownBrushes.Tone("#2563EB")
                    },
                    content
                }
            }
        };
    }
}
