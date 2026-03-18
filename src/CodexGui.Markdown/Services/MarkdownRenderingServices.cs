namespace CodexGui.Markdown.Services;

public static class MarkdownRenderingServices
{
    public static IMarkdownRenderController DefaultController { get; } = CreateDefaultController();

    public static IMarkdownHitTestingService DefaultHitTestingService { get; } = CreateHitTestingService();

    public static IMarkdownEditingService DefaultEditingService { get; } = CreateEditingService();

    public static IMarkdownRenderController CreateDefaultController()
    {
        return CreateController();
    }

    public static IMarkdownRenderController CreateController(params IMarkdownPlugin[] plugins)
    {
        return CreateControllerFromRegistry(CreateRegistry(plugins));
    }

    public static IMarkdownRenderController CreateController(IEnumerable<IMarkdownPlugin>? plugins)
    {
        return CreateControllerFromRegistry(CreateRegistry(plugins));
    }

    public static IMarkdownRenderController CreateControllerFromRegistry(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return new MarkdownRenderController(
            new MarkdownParsingService(registry.ParserPlugins),
            new MarkdownInlineRenderingService(registry.BlockRenderingPlugins, registry.InlineRenderingPlugins));
    }

    public static IMarkdownHitTestingService CreateHitTestingService()
    {
        return new MarkdownHitTestingService();
    }

    public static IMarkdownEditingService CreateEditingService(params IMarkdownPlugin[] plugins)
    {
        return CreateEditingServiceFromRegistry(CreateRegistry(plugins));
    }

    public static IMarkdownEditingService CreateEditingService(IEnumerable<IMarkdownPlugin>? plugins)
    {
        return CreateEditingServiceFromRegistry(CreateRegistry(plugins));
    }

    public static IMarkdownEditingService CreateEditingServiceFromRegistry(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new MarkdownEditingService(registry.EditorPlugins, registry.BlockTemplateProviders);
    }

    public static MarkdownPluginRegistry CreateRegistry(params IMarkdownPlugin[] plugins)
    {
        return CreateRegistry(plugins.AsEnumerable());
    }

    public static MarkdownPluginRegistry CreateRegistry(IEnumerable<IMarkdownPlugin>? plugins)
    {
        var registry = new MarkdownPluginRegistry();
        MarkdownBuiltInEditorPlugins.Register(registry);

        foreach (var plugin in plugins ?? [])
        {
            registry.AddPlugin(plugin);
        }

        return registry;
    }
}
