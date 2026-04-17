using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.DefinitionLists;

public sealed class DefinitionListMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}

public static class DefinitionListMarkdownEditorIds
{
    public const string Block = "definition-list-block-editor";
}
