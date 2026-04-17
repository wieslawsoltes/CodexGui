using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.Figures;

public sealed class FiguresMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}

public static class FiguresMarkdownEditorIds
{
    public const string Block = "figure-block-editor";
}
