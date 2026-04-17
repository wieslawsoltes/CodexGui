using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.CustomContainers;

public sealed class CustomContainersMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}

public static class CustomContainerMarkdownEditorIds
{
    public const string Block = "custom-container-block-editor";
}
