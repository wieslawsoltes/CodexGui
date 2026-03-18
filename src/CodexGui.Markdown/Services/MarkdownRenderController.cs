using Avalonia.Controls.Documents;

namespace CodexGui.Markdown.Services;

public sealed class MarkdownRenderController(IMarkdownParsingService parsingService, IMarkdownInlineRenderingService inlineRenderingService) : IMarkdownRenderController
{
    private readonly IMarkdownParsingService _parsingService = parsingService ?? throw new ArgumentNullException(nameof(parsingService));
    private readonly IMarkdownInlineRenderingService _inlineRenderingService = inlineRenderingService ?? throw new ArgumentNullException(nameof(inlineRenderingService));
    private readonly object _cacheGate = new();
    private string? _lastMarkdown;
    private MarkdownParseResult? _lastParseResult;

    public MarkdownRenderResult Render(MarkdownRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);

        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            return MarkdownRenderResult.Empty(request.Context.ResourceTracker);
        }

        MarkdownRenderInvalidationPlan invalidationPlan;
        MarkdownParseResult? parseResult = null;

        lock (_cacheGate)
        {
            invalidationPlan = MarkdownRenderInvalidationPlan.Create(_lastMarkdown, request.Markdown);
            if (invalidationPlan.ReuseCachedParseResult)
            {
                parseResult = _lastParseResult;
            }
        }

        if (parseResult is null)
        {
            try
            {
                parseResult = _parsingService.Parse(request.Markdown);
            }
            catch (Exception ex)
            {
                request.Context.Diagnostics.Report(
                    MarkdownRenderDiagnosticSeverity.Error,
                    nameof(MarkdownRenderController),
                    "The markdown document could not be parsed.",
                    MarkdownSourceSpan.Empty,
                    exception: ex);

                return MarkdownRenderFailureUi.CreateParseFailureResult(request.Context, ex);
            }
        }

        lock (_cacheGate)
        {
            _lastMarkdown = request.Markdown;
            _lastParseResult = parseResult;
        }

        try
        {
            return _inlineRenderingService.Render(parseResult, request.Context);
        }
        catch (Exception ex)
        {
            request.Context.Diagnostics.Report(
                MarkdownRenderDiagnosticSeverity.Error,
                nameof(MarkdownRenderController),
                "The markdown document could not be rendered.",
                MarkdownSourceSpan.Empty,
                exception: ex);

            return MarkdownRenderFailureUi.CreateRenderFailureResult(request.Context, parseResult, ex);
        }
    }
}
