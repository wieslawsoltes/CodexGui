using CodexGui.Markdown.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexGui.Markdown.Controls;

internal static class MarkdownAlertViewFactory
{
    private static readonly IReadOnlyDictionary<string, string> AlertDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["note"] = "General information or context.",
            ["info"] = "Reference information or additional context.",
            ["tip"] = "Helpful guidance or a best practice.",
            ["success"] = "A positive outcome or confirmed result.",
            ["important"] = "High-priority guidance that should stand out.",
            ["warning"] = "A potential issue that needs attention.",
            ["caution"] = "Use care before continuing.",
            ["danger"] = "A high-risk condition or breaking impact.",
            ["error"] = "A failure state or invalid result."
        };

    public static UIElement CreateBlockView(
        MarkdownAlertBlock block,
        Func<string, double, UIElement> renderNested,
        double fontSize)
    {
        ArgumentNullException.ThrowIfNull(renderNested);

        return MarkdownCalloutSurfaceFactory.CreateSurface(
            MarkdownCalloutSurfaceFactory.ResolveTitle(block.Kind, block.Title),
            ResolveDescription(block.Kind),
            string.IsNullOrWhiteSpace(block.BodyMarkdown)
                ? CreatePlaceholder("Add alert body content to render a preview.")
                : renderNested(block.BodyMarkdown, 18),
            block.Kind,
            fontSize);
    }

    public static string ResolveDescription(string? kind)
    {
        return !string.IsNullOrWhiteSpace(kind) && AlertDescriptions.TryGetValue(kind, out var description)
            ? description
            : "Alert block";
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
