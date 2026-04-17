using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.TextMate;

public sealed class TextMateMarkdownPlugin : IMarkdownPlugin
{
    public const string TextMateCodeEditorId = "textmate-code-editor";

    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddCodeHighlighter(new TextMateCodeHighlighter());
    }
}
