namespace CodexGui.Markdown.Core;

public static class MarkdownRuntimeConfiguration
{
    private static readonly object Gate = new();
    private static MarkdownPluginRegistry _registry = new();

    public static void Reset()
    {
        lock (Gate)
        {
            _registry = new MarkdownPluginRegistry();
        }
    }

    public static void RegisterPlugin(IMarkdownPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        lock (Gate)
        {
            plugin.Register(_registry);
        }
    }

    public static void RegisterPlugins(IEnumerable<IMarkdownPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        lock (Gate)
        {
            foreach (var plugin in plugins)
            {
                plugin.Register(_registry);
            }
        }
    }

    public static MarkdownPluginRegistry Snapshot()
    {
        lock (Gate)
        {
            return _registry.Clone();
        }
    }
}
