using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.Math;

public sealed class MathMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}

public static class MathMarkdownEditorIds
{
    public const string Block = "math-block-editor";
    public const string Inline = "math-inline-editor";
}
