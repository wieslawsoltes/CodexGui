using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.Footers;

public sealed class FootersMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}

public static class FooterMarkdownEditorIds
{
    public const string Block = "footer-block-editor";
}
