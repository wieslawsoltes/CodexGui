using CodexGui.Markdown.Services;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Embeds;

public sealed class EmbedsMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new EmbedBlockRenderingPlugin())
            .AddBlockTemplateProvider(new EmbedBlockTemplateProvider())
            .AddEditorPlugin(new EmbedBlockEditorPlugin());
    }
}

public static class EmbedsMarkdownEditorIds
{
    public const string Block = "embed-block-editor";
}

internal sealed class EmbedBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => -40;

    public bool CanRender(Block block)
    {
        return block is CustomContainer customContainer &&
               MarkdownEmbedSyntax.IsEmbedInfo(customContainer.Info);
    }

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not CustomContainer customContainer ||
            !MarkdownEmbedSyntax.IsEmbedInfo(customContainer.Info))
        {
            return false;
        }

        var document = MarkdownEmbedParser.Parse(customContainer, context.ParseResult);
        context.AddBlockControl(MarkdownEmbedRendering.CreateBlockView(document, context.RenderContext));
        return true;
    }
}
