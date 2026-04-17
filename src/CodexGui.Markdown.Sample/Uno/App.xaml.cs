using CodexGui.Markdown.Core;
using CodexGui.Markdown.Plugin.Alerts;
using CodexGui.Markdown.Plugin.CustomContainers;
using CodexGui.Markdown.Plugin.DefinitionLists;
using CodexGui.Markdown.Plugin.Figures;
using CodexGui.Markdown.Plugin.Footers;
using CodexGui.Markdown.Plugin.Math;
using CodexGui.Markdown.Plugin.Mermaid;
using CodexGui.Markdown.Plugin.SyntaxHighlighting;
using CodexGui.Markdown.Plugin.TextMate;
using Microsoft.Extensions.Logging;

namespace CodexGui.Markdown.Sample.Uno;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ConfigureMarkdown();

        var window = new MainWindow(new MainPage());
        window.Activate();
    }

    private static void ConfigureMarkdown()
    {
        MarkdownRuntimeConfiguration.Reset();
        MarkdownRuntimeConfiguration.RegisterPlugins(
        [
            new MermaidMarkdownPlugin(),
            new TextMateMarkdownPlugin(),
            new SyntaxHighlightingMarkdownPlugin(),
            new AlertsMarkdownPlugin(),
            new CustomContainersMarkdownPlugin(),
            new DefinitionListMarkdownPlugin(),
            new FiguresMarkdownPlugin(),
            new FootersMarkdownPlugin(),
            new MathMarkdownPlugin()
        ]);
    }

    public static void InitializeLogging()
    {
#if DEBUG
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
