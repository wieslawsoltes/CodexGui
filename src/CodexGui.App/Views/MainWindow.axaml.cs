using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CodexGui.App.ViewModels;

namespace CodexGui.App.Views;

public partial class MainWindow : Window
{
    private const double NavRailWidth = 64;
    private const double WorkspaceWidth = 286;
    private const double MinConversationWidth = 620;

    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnWindowSizeChanged;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        AttachViewModel(DataContext as MainWindowViewModel);
        ApplyResponsiveLayout();
        ScrollConversationToBottom();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        AttachViewModel(null);
        DataContextChanged -= OnDataContextChanged;
        SizeChanged -= OnWindowSizeChanged;
        Opened -= OnOpened;
        Closed -= OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        AttachViewModel(DataContext as MainWindowViewModel);
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

    private void OnConversationItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (_viewModel?.AutoScrollMessages != true)
        {
            return;
        }

        Dispatcher.UIThread.Post(ScrollConversationToBottom, DispatcherPriority.Background);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        ApplyResponsiveLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.ShowNavigationRail)
            or nameof(MainWindowViewModel.ShowWorkspacePanel))
        {
            ApplyResponsiveLayout();
        }
    }

    private void ApplyResponsiveLayout()
    {
        if (_viewModel is null)
        {
            return;
        }

        var showNavigationRail = _viewModel.ShowNavigationRail;
        var showWorkspacePanel = _viewModel.ShowWorkspacePanel;

        var availableConversationWidth = Bounds.Width;
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

        NavRailPanel.IsVisible = showNavigationRail;
        WorkspacePanel.IsVisible = showWorkspacePanel;

        var columns = ShellGrid.ColumnDefinitions;
        columns[0].Width = showNavigationRail ? new GridLength(NavRailWidth) : new GridLength(0);
        columns[1].Width = showWorkspacePanel ? new GridLength(WorkspaceWidth) : new GridLength(0);
    }

    private void ScrollConversationToBottom()
    {
        if (_viewModel is null || _viewModel.ConversationItems.Count == 0)
        {
            return;
        }

        MainContentPanel.ConversationDataGrid.ScrollIntoView(_viewModel.ConversationItems[^1], null);
    }
}
