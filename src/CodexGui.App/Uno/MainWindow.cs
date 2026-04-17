using CodexGui.App.ViewModels;

namespace CodexGui.App;

public sealed class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "Codex GUI";
        Content = new Uno.MainPage(viewModel);
    }
}
