using System.Collections.Specialized;
using System.ComponentModel;
using CodexGui.App.ViewModels;

namespace CodexGui.App.Uno;

public sealed partial class MainPage : Page
{
    private const double NavRailWidth = 64;
    private const double WorkspaceWidth = 286;
    private const double MinConversationWidth = 760;

    private MainWindowViewModel? _viewModel;

    public MainPage()
        : this(new MainWindowViewModel())
    {
    }

    public MainPage(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnPageSizeChanged;
    }

    public MainWindowViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(ViewModel);
        ApplyResponsiveLayout();
        ScrollConversationToBottom();
        await ViewModel.EnsureInitialConnectionAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(null);
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        SizeChanged -= OnPageSizeChanged;
    }

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.ConversationItems.CollectionChanged -= OnConversationItemsCollectionChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;

        if (_viewModel is null)
        {
            return;
        }

        _viewModel.ConversationItems.CollectionChanged += OnConversationItemsCollectionChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyResponsiveLayout();
    }

    private void OnConversationItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel?.AutoScrollMessages != true)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(ScrollConversationToBottom);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.ShowNavigationRail)
            or nameof(MainWindowViewModel.ShowWorkspacePanel))
        {
            ApplyResponsiveLayout();
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (_viewModel is null)
        {
            return;
        }

        var showNavigationRail = _viewModel.ShowNavigationRail;
        var showWorkspacePanel = _viewModel.ShowWorkspacePanel;

        var availableConversationWidth = ActualWidth;
        if (showNavigationRail)
        {
            availableConversationWidth -= NavRailWidth;
        }

        if (showWorkspacePanel)
        {
            availableConversationWidth -= WorkspaceWidth;
        }

        if (availableConversationWidth < MinConversationWidth && showWorkspacePanel)
        {
            showWorkspacePanel = false;
            availableConversationWidth += WorkspaceWidth;
        }

        if (availableConversationWidth < MinConversationWidth && showNavigationRail)
        {
            showNavigationRail = false;
        }

        NavRailPanel.Visibility = showNavigationRail ? Visibility.Visible : Visibility.Collapsed;
        WorkspacePanel.Visibility = showWorkspacePanel ? Visibility.Visible : Visibility.Collapsed;
        ShellGrid.ColumnDefinitions[0].Width = showNavigationRail ? new GridLength(NavRailWidth) : new GridLength(0);
        ShellGrid.ColumnDefinitions[1].Width = showWorkspacePanel ? new GridLength(WorkspaceWidth) : new GridLength(0);
    }

    private void ScrollConversationToBottom()
    {
        if (_viewModel is null || _viewModel.ConversationItems.Count == 0)
        {
            return;
        }

        ConversationWorkspace.ConversationListView.ScrollIntoView(_viewModel.ConversationItems[^1]);
    }
}
