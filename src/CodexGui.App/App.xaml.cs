using CodexGui.App.Services;
using CodexGui.App.Uno.Services;
using CodexGui.App.ViewModels;
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
using CodexGui.AppServer.Client;
using Microsoft.Extensions.Logging;

namespace CodexGui.App;

public partial class App : Application
{
    private ICodexSessionService? _sessionService;
    private MainWindowViewModel? _mainWindowViewModel;

    public App()
    {
        InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ConfigureMarkdown();

        _sessionService = new CodexSessionService(new CodexAppServerClient());
        _mainWindowViewModel = new MainWindowViewModel(
            _sessionService,
            new UnoUiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()),
            new GitDiffService(),
            new PendingInteractionFactory());

        MainWindow = new MainWindow(_mainWindowViewModel);
        MainWindow.Activate();
        MainWindow.Closed += OnMainWindowClosed;
    }

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_mainWindowViewModel is not null)
        {
            await _mainWindowViewModel.DisposeAsync();
        }

        if (_sessionService is not null)
        {
            await _sessionService.DisposeAsync();
        }
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
