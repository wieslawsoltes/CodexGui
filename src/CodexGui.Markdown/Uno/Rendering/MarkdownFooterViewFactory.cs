using CodexGui.Markdown.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexGui.Markdown.Controls;

internal static class MarkdownFooterViewFactory
{
    public static UIElement CreateBlockView(
        MarkdownFooterBlock block,
        Func<string, double, UIElement> renderNested,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(renderNested);

        return new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            BorderBrush = MarkdownBrushes.Tone("#CBD5E1"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Footer",
                        Foreground = MarkdownBrushes.Tone("#64748B"),
                        FontWeight = FontWeights.SemiBold
                    },
                    string.IsNullOrWhiteSpace(block.BodyMarkdown)
                        ? new TextBlock
                        {
                            Text = "Add footer content to render a preview.",
                            Foreground = MarkdownBrushes.Tone("#6E6E6E"),
                            FontStyle = Windows.UI.Text.FontStyle.Italic,
                            TextWrapping = TextWrapping.Wrap
                        }
                        : MarkdownNestedContentStyler.Apply(
                            renderNested(block.BodyMarkdown, 0),
                            new MarkdownNestedContentStyle(
                                FontSize: Math.Max(fontSize - 1, 11),
                                Foreground: MarkdownBrushes.Tone("#64748B")))
                }
            }
        };
    }
}
