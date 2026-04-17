using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CodexGui.Markdown.Controls;

internal readonly record struct MarkdownNestedContentStyle(
    double? FontSize = null,
    Windows.UI.Text.FontWeight? FontWeight = null,
    Brush? Foreground = null,
    TextAlignment? TextAlignment = null,
    bool Italic = false);

internal static class MarkdownNestedContentStyler
{
    public static UIElement Apply(UIElement element, MarkdownNestedContentStyle style)
    {
        ArgumentNullException.ThrowIfNull(element);
        ApplyCore(element, style);
        return element;
    }

    private static void ApplyCore(UIElement element, MarkdownNestedContentStyle style)
    {
        if (element is TextBlock textBlock)
        {
            if (style.FontSize is double fontSize)
            {
                textBlock.FontSize = fontSize;
            }

            if (style.FontWeight is Windows.UI.Text.FontWeight fontWeight)
            {
                textBlock.FontWeight = fontWeight;
            }

            if (style.Foreground is not null)
            {
                textBlock.Foreground = style.Foreground;
            }

            if (style.TextAlignment is TextAlignment textAlignment)
            {
                textBlock.TextAlignment = textAlignment;
            }

            if (style.Italic)
            {
                textBlock.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }
        }

        switch (element)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    ApplyCore(child, style);
                }

                break;
            case Border { Child: UIElement child }:
                ApplyCore(child, style);
                break;
            case ContentControl { Content: UIElement content }:
                ApplyCore(content, style);
                break;
            case Viewbox { Child: UIElement child }:
                ApplyCore(child, style);
                break;
        }
    }
}
