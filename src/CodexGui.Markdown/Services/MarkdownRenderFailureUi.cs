using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace CodexGui.Markdown.Services;

internal enum MarkdownRenderFailureKind
{
    Parse,
    Render
}

internal readonly record struct MarkdownRenderFailure(
    MarkdownRenderFailureKind Kind,
    string Title,
    string Summary,
    string Detail,
    Exception Exception);

internal static class MarkdownRenderFailureUi
{
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly IBrush FailureBackground = new SolidColorBrush(Color.Parse("#FFF5F5"));
    private static readonly IBrush FailureBorderBrush = new SolidColorBrush(Color.Parse("#F1AEB5"));
    private static readonly IBrush FailureTitleBrush = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush FailureDetailBrush = new SolidColorBrush(Color.Parse("#5F2120"));

    public static MarkdownRenderResult CreateParseFailureResult(MarkdownRenderContext context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        return CreateFailureResult(
            context,
            MarkdownParseResult.Empty,
            new MarkdownRenderFailure(
                MarkdownRenderFailureKind.Parse,
                "Markdown parse failed",
                "The markdown document could not be parsed.",
                $"{exception.GetType().Name}: {exception.Message}",
                exception));
    }

    public static MarkdownRenderResult CreateRenderFailureResult(MarkdownRenderContext context, MarkdownParseResult parseResult, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(exception);

        return CreateFailureResult(
            context,
            parseResult,
            new MarkdownRenderFailure(
                MarkdownRenderFailureKind.Render,
                "Markdown render failed",
                "The markdown document could not be rendered.",
                $"{exception.GetType().Name}: {exception.Message}",
                exception));
    }

    private static MarkdownRenderResult CreateFailureResult(
        MarkdownRenderContext context,
        MarkdownParseResult parseResult,
        MarkdownRenderFailure failure)
    {
        Trace.TraceError(BuildTraceMessage(failure));

        var inlines = new InlineCollection
        {
            new InlineUIContainer(CreateFailureBlock(failure, context))
        };

        return new MarkdownRenderResult(
            inlines,
            context.ResourceTracker,
            parseResult,
            MarkdownRenderMapBuilder.Build(inlines),
            context.Diagnostics.GetSnapshot());
    }

    private static Control CreateFailureBlock(MarkdownRenderFailure failure, MarkdownRenderContext context)
    {
        var title = new TextBlock
        {
            Text = failure.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = FailureTitleBrush
        };

        var summary = new TextBlock
        {
            Text = failure.Summary,
            TextWrapping = TextWrapping.Wrap
        };

        var detail = new TextBlock
        {
            Text = failure.Detail,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FailureDetailBrush,
            FontFamily = MonospaceFamily,
            FontSize = Math.Max(context.FontSize - 1, 11)
        };

        return new Border
        {
            Background = FailureBackground,
            BorderBrush = FailureBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    title,
                    summary,
                    detail
                }
            }
        };
    }

    private static string BuildTraceMessage(MarkdownRenderFailure failure)
    {
        return new StringBuilder()
            .Append("[CodexGui.Markdown] ")
            .Append(failure.Title)
            .Append(": ")
            .Append(failure.Summary)
            .AppendLine()
            .Append(failure.Exception)
            .ToString();
    }
}
