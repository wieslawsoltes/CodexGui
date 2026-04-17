using CodexGui.Markdown.Core;

namespace CodexGui.Markdown.Plugin.Alerts;

public sealed class AlertsMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
    }
}

public static class AlertsMarkdownEditorIds
{
    public const string Block = "alert-block-editor";
}
