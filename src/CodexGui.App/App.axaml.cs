using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CodexGui.App.Services;
using CodexGui.AppServer.Client;
using CodexGui.App.ViewModels;
using CodexGui.App.Views;

namespace CodexGui.App;

public partial class App : Application
{
    private ICodexSessionService? _sessionService;
    private MainWindowViewModel? _mainWindowViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _sessionService = new CodexSessionService(new CodexAppServerClient());
            _mainWindowViewModel = new MainWindowViewModel(
                _sessionService,
                new AvaloniaUiDispatcher(),
                new GitDiffService(),
                new PendingInteractionFactory());
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainWindowViewModel,
            };

            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
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
}
