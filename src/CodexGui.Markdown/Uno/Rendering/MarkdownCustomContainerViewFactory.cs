using CodexGui.Markdown.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexGui.Markdown.Controls;

internal static class MarkdownCustomContainerViewFactory
{
    public static UIElement CreateBlockView(
        MarkdownCustomContainerBlock block,
        Func<string, double, UIElement> renderNested,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(renderNested);

        return MarkdownCalloutSurfaceFactory.CreateSurface(
            MarkdownCalloutSurfaceFactory.ResolveTitle(block.Kind, block.Title),
            string.IsNullOrWhiteSpace(block.Arguments) ? null : block.Arguments,
            string.IsNullOrWhiteSpace(block.BodyMarkdown)
                ? CreatePlaceholder("Add custom-container body content to render a preview.")
                : renderNested(block.BodyMarkdown, 18),
            block.Kind,
            fontSize);
    }

    private static UIElement CreatePlaceholder(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = MarkdownBrushes.Tone("#64748B"),
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap
        };
    }
}
