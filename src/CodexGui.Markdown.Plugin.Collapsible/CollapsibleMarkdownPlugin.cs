using CodexGui.Markdown.Services;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Collapsible;

public sealed class CollapsibleMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new CollapsibleBlockRenderingPlugin())
            .AddBlockTemplateProvider(new CollapsibleBlockTemplateProvider())
            .AddEditorPlugin(new CollapsibleBlockEditorPlugin());
    }
}

public static class CollapsibleMarkdownEditorIds
{
    public const string Block = "collapsible-block-editor";
}

internal sealed class CollapsibleBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => -45;

    public bool CanRender(Block block)
    {
        return block is CustomContainer customContainer &&
               MarkdownCollapsibleSyntax.IsCollapsibleInfo(customContainer.Info);
    }

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not CustomContainer customContainer ||
            !MarkdownCollapsibleSyntax.IsCollapsibleInfo(customContainer.Info))
        {
            return false;
        }

        var document = MarkdownCollapsibleParser.Parse(customContainer, context.ParseResult);
        context.AddBlockControl(MarkdownCollapsibleRendering.CreateBlockView(document, context.RenderContext));
        return true;
    }
}
