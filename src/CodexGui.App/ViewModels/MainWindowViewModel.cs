using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodexGui.App.Services;
using CodexGui.AppServer.Client;
using CodexGui.AppServer.Models;

namespace CodexGui.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private static readonly string[] NotificationOptOutMethods = [];

    private readonly ICodexSessionService _sessionService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IGitDiffService _gitDiffService;
    private readonly IPendingInteractionFactory _pendingInteractionFactory;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _threadDetailCancellationTokenSource;
    private CancellationTokenSource? _liveUpdateLoopCancellationTokenSource;
    private bool _liveUpdatesActive;
    private Task? _liveUpdateLoopTask;
    private DateTimeOffset _lastNotificationReceivedAt = DateTimeOffset.MinValue;
    private string? _selectedThreadId;
    private bool _isApplyingThreadList;

    private string _accountSummary = "Pending";
    private string _modelSummary = "No catalog";
    private string _threadSummary = "0 sessions";
    private string _connectorSummary = "0 connectors";
    private string _accessSummary = "Read-only MVP";
    private string _rateLimitSummary = "No limits";
    private bool _requiresOpenaiAuth;
    private AppListResult? _latestApps;
    private SkillsListResult? _latestSkills;
    private ConfigRequirementsReadResult? _latestRequirements;
    private JsonElement? _latestConfigSnapshot;
    private JsonElement? _latestExperimentalFeatures;
    private JsonElement? _latestMcpServerStatuses;
    private JsonElement? _latestLoadedThreads;

    [ObservableProperty]
    private string commandPath = "codex";

    [ObservableProperty]
    private string commandArguments = "app-server";

    [ObservableProperty]
    private string workingDirectory = Environment.CurrentDirectory;

    [ObservableProperty]
    private string connectionState = "Offline";

    [ObservableProperty]
    private string connectionDetail = "Connect to a local Codex app-server instance to browse sessions, author turns, and answer approvals.";

    [ObservableProperty]
    private string currentWorkspaceTitle = "CodexGui";

    [ObservableProperty]
    private string currentWorkspaceSubtitle = "Interactive desktop shell for Codex app-server sessions";

    [ObservableProperty]
    private string composerHint = "Turn authoring, approvals, reasoning streams, and auth requests are enabled.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private ThreadListEntryViewModel? selectedThread;

    [ObservableProperty]
    private bool autoScrollMessages = true;

    [ObservableProperty]
    private bool showNavigationRail = true;

    [ObservableProperty]
    private bool showWorkspacePanel = true;

    [ObservableProperty]
    private string? activeAccountLoginId;

    public MainWindowViewModel()
        : this(null, null, null, null)
    {
    }

    internal MainWindowViewModel(
        ICodexSessionService? sessionService = null,
        IUiDispatcher? uiDispatcher = null,
        IGitDiffService? gitDiffService = null,
        IPendingInteractionFactory? pendingInteractionFactory = null)
    {
        _sessionService = sessionService ?? NullCodexSessionService.Instance;
        _uiDispatcher = uiDispatcher ?? new AvaloniaUiDispatcher();
        _gitDiffService = gitDiffService ?? new GitDiffService();
        _pendingInteractionFactory = pendingInteractionFactory ?? new PendingInteractionFactory();

        StatusChips = new ObservableCollection<StatusChipViewModel>();
        RecentThreads = new ObservableCollection<ThreadListEntryViewModel>();

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanRefresh);
        LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, CanLogout);
        CancelLoginCommand = new AsyncRelayCommand(CancelLoginAsync, CanCancelLogin);
        ShowThreadsCommand = new AsyncRelayCommand(ShowThreadsAsync, CanShowThreads);
        ShowAppsCommand = new AsyncRelayCommand(ShowAppsAsync, CanShowApps);
        ShowSettingsCommand = new AsyncRelayCommand(ShowSettingsAsync, CanShowSettings);
        ReloadMcpServersCommand = new AsyncRelayCommand(ReloadMcpServersAsync, CanReloadMcpServers);
        NewThreadCommand = new AsyncRelayCommand(StartNewThreadAsync, CanStartNewThread);
        ForkThreadCommand = new AsyncRelayCommand(ForkThreadAsync, CanForkThread);
        ArchiveThreadCommand = new AsyncRelayCommand(ArchiveThreadAsync, CanArchiveThread);
        UnarchiveThreadCommand = new AsyncRelayCommand(UnarchiveThreadAsync, CanUnarchiveThread);
        RenameThreadCommand = new AsyncRelayCommand(RenameThreadAsync, CanRenameThread);
        CompactThreadCommand = new AsyncRelayCommand(CompactThreadAsync, CanCompactThread);
        RollbackThreadCommand = new AsyncRelayCommand(RollbackThreadAsync, CanRollbackThread);
        SendTurnCommand = new AsyncRelayCommand(SendTurnAsync, CanSendTurn);
        SteerTurnCommand = new AsyncRelayCommand(SteerTurnAsync, CanSteerTurn);
        InterruptTurnCommand = new AsyncRelayCommand(InterruptTurnAsync, CanInterruptTurn);
        StartReviewCommand = new AsyncRelayCommand(StartReviewAsync, CanStartReview);

        _sessionService.NotificationReceived += OnNotificationReceived;
        _sessionService.ConnectionStateChanged += OnConnectionStateChanged;
        _sessionService.ServerRequestHandlerAsync = HandleServerRequestAsync;

        SeedRuntimeState();
    }

    public async ValueTask DisposeAsync()
    {
        _sessionService.NotificationReceived -= OnNotificationReceived;
        _sessionService.ConnectionStateChanged -= OnConnectionStateChanged;

        if (_sessionService.ServerRequestHandlerAsync == HandleServerRequestAsync)
        {
            _sessionService.ServerRequestHandlerAsync = null;
        }

        _threadDetailCancellationTokenSource?.Cancel();
        _threadDetailCancellationTokenSource?.Dispose();
        _threadDetailCancellationTokenSource = null;

        StopLiveUpdateLoop();
        StopLiveUpdates();

        foreach (var completion in _pendingServerRequestCompletions.Values)
        {
            completion.TrySetCanceled();
        }

        _pendingServerRequestCompletions.Clear();
        _refreshGate.Dispose();

        await Task.CompletedTask;
    }

    public ObservableCollection<StatusChipViewModel> StatusChips { get; }

    public ObservableCollection<ThreadListEntryViewModel> RecentThreads { get; }

    public IAsyncRelayCommand ConnectCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand LoginCommand { get; }

    public IAsyncRelayCommand LogoutCommand { get; }

    public IAsyncRelayCommand CancelLoginCommand { get; }

    public IAsyncRelayCommand ShowThreadsCommand { get; }

    public IAsyncRelayCommand ShowAppsCommand { get; }

    public IAsyncRelayCommand ShowSettingsCommand { get; }

    public IAsyncRelayCommand ReloadMcpServersCommand { get; }

    public string ThreadCountLabel => RecentThreads.Count == 0 ? "No threads" : $"{RecentThreads.Count} threads";

    public bool HasActiveLogin => !string.IsNullOrWhiteSpace(ActiveAccountLoginId);

    public string WorkingDirectoryName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(WorkingDirectory))
            {
                return "workspace";
            }

            var trimmed = WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrWhiteSpace(trimmed) ? WorkingDirectory : Path.GetFileName(trimmed);
        }
    }

    public IBrush ConnectionBrush => IsConnected ? ShellBrushes.Green : IsBusy ? ShellBrushes.Blue : ShellBrushes.Neutral;

    partial void OnSelectedThreadChanged(ThreadListEntryViewModel? value)
    {
        if (value is null)
        {
            if (_isApplyingThreadList && !string.IsNullOrWhiteSpace(_selectedThreadId))
            {
                return;
            }

            _selectedThreadId = null;
            SelectedConversationItem = null;
            SeedConversationPlaceholder();
            NotifyCommandStates();
            return;
        }

        if (string.Equals(value.Id, _selectedThreadId, StringComparison.Ordinal))
        {
            NotifyCommandStates();
            return;
        }

        _selectedThreadId = value.Id;
        SelectedConversationItem = null;
        _ = LoadThreadDetailAsync(value.Id, false);
        NotifyCommandStates();
    }

    partial void OnWorkingDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(WorkingDirectoryName));
    }

    partial void OnIsBusyChanged(bool value)
    {
        if (!_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.Post(() =>
            {
                OnPropertyChanged(nameof(ConnectionBrush));
                NotifyCommandStates();
            });
            return;
        }

        OnPropertyChanged(nameof(ConnectionBrush));
        NotifyCommandStates();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        if (!_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.Post(() =>
            {
                OnPropertyChanged(nameof(ConnectionBrush));
                NotifyCommandStates();
            });
            return;
        }

        OnPropertyChanged(nameof(ConnectionBrush));
        NotifyCommandStates();
    }

    partial void OnActiveAccountLoginIdChanged(string? value)
    {
        OnPropertyChanged(nameof(HasActiveLogin));
        NotifyCommandStates();
    }

    private void SeedRuntimeState()
    {
        CurrentWorkspaceTitle = "CodexGui";
        CurrentWorkspaceSubtitle = "Interactive desktop shell for Codex app-server sessions";
        _accountSummary = "Pending";
        _modelSummary = "No catalog";
        _threadSummary = "0 sessions";
        _connectorSummary = "0 connectors";
        _accessSummary = "Read-only MVP";
        _rateLimitSummary = "No limits";
        _requiresOpenaiAuth = false;
        _latestApps = null;
        _latestSkills = null;
        _latestRequirements = null;
        _latestConfigSnapshot = null;
        _latestExperimentalFeatures = null;
        _latestMcpServerStatuses = null;
        _latestLoadedThreads = null;
        ActiveAccountLoginId = null;
        RebuildStatusChips();
        SeedConversationPlaceholder();
        SeedPhaseTwoRuntimeState();
        NotifyCollectionStateChanged();
    }

    private bool CanConnect() => !IsBusy && !IsConnected;

    private bool CanDisconnect() => !IsBusy && IsConnected;

    private bool CanRefresh() => !IsBusy && IsConnected;

    private bool CanLogin() => !IsBusy && IsConnected && _requiresOpenaiAuth && !HasActiveLogin;

    private bool CanLogout() => !IsBusy && IsConnected;

    private bool CanCancelLogin() => !IsBusy && IsConnected && HasActiveLogin;

    private bool CanShowThreads() => !IsBusy;

    private bool CanShowApps() => !IsBusy;

    private bool CanShowSettings() => !IsBusy;

    private bool CanReloadMcpServers() => IsConnected && !IsBusy;

    private async Task ConnectAsync()
    {
        if (_sessionService is null)
        {
            return;
        }

        var isRemoteEndpoint = IsRemoteEndpoint(CommandPath);
        IsBusy = true;
        ConnectionState = "Connecting";
        ConnectionDetail = isRemoteEndpoint
            ? $"Connecting to remote Codex app-server endpoint {CommandPath}."
            : "Launching codex app-server and performing the initialize handshake.";

        try
        {
            await _sessionService.ConnectAsync(new AppServerClientOptions(
                CommandPath,
                CommandArguments,
                string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory,
                "codex_gui",
                "CodexGui",
                GetType().Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                true,
                NotificationOptOutMethods)).ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                IsConnected = true;
                ConnectionState = "Connected";
                ConnectionDetail = "Handshake complete. Interactive transport is active for turns and approvals.";
            });

            await RefreshDataAsync(setBusyState: false, backgroundRefresh: false, includeThreadDetail: true).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsHandledSessionException(exception))
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                IsConnected = false;
                ConnectionState = "Failed";
                ConnectionDetail = exception.Message;
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task DisconnectAsync()
    {
        if (_sessionService is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _sessionService.DisconnectAsync().ConfigureAwait(false);
            await _uiDispatcher.InvokeAsync(() =>
            {
                IsConnected = false;
                ActiveAccountLoginId = null;
                ConnectionState = "Disconnected";
                ConnectionDetail = "The app-server transport is not active.";
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task LoginAsync()
    {
        if (_sessionService is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result = await RequestAsync<AccountLoginStartResult>(
                "account/login/start",
                new
                {
                    type = "chatgpt"
                },
                "Unable to start account login.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                ActiveAccountLoginId = result?.LoginId;
                if (!string.IsNullOrWhiteSpace(result?.AuthUrl))
                {
                    ConnectionDetail = $"Login flow started. Open {result.AuthUrl} to continue.";
                }
                else
                {
                    ConnectionDetail = "Login flow started. Waiting for account/login/completed notification.";
                }
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task LogoutAsync()
    {
        if (_sessionService is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await RequestAsync<AccountLogoutResult>(
                "account/logout",
                new { },
                "Unable to logout from account.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() => ActiveAccountLoginId = null);

            await RefreshDataAsync(setBusyState: false, backgroundRefresh: false, includeThreadDetail: false).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task CancelLoginAsync()
    {
        if (_sessionService is null || string.IsNullOrWhiteSpace(ActiveAccountLoginId))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await RequestAsync<AccountLoginCancelResult>(
                "account/login/cancel",
                new
                {
                    loginId = ActiveAccountLoginId
                },
                "Unable to cancel account login.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                ActiveAccountLoginId = null;
                ConnectionDetail = "Account login flow cancelled.";
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task ShowThreadsAsync()
    {
        var loadedThreadCount = CountArrayProperty(_latestLoadedThreads, "data");

        await _uiDispatcher.InvokeAsync(() =>
        {
            ShowNavigationRail = true;
            ShowWorkspacePanel = true;
        });

        if (_sessionService is not null && _sessionService.IsConnected && RecentThreads.Count == 0)
        {
            await RefreshThreadListAsync().ConfigureAwait(false);
        }

        if (_sessionService is not null && _sessionService.IsConnected)
        {
            var loadedThreads = await RequestAsync<JsonElement>(
                "thread/loaded/list",
                new { cursor = (string?)null, limit = 200 },
                "Unable to read loaded thread sessions.").ConfigureAwait(false);
            loadedThreadCount = CountArrayProperty(loadedThreads, "data");

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (IsDefinedJsonElement(loadedThreads) && loadedThreads.ValueKind == JsonValueKind.Object)
                {
                    _latestLoadedThreads = loadedThreads;
                }
            });
        }

        await _uiDispatcher.InvokeAsync(() =>
        {
            SelectedThread ??= RecentThreads.FirstOrDefault();
            ComposerHint = loadedThreadCount > 0
                ? $"Thread browser is active. {loadedThreadCount} thread sessions are currently loaded in memory."
                : "Thread browser is active. Select a thread to inspect live item updates.";
        });
    }

    private async Task ShowAppsAsync()
    {
        IsBusy = true;

        try
        {
            AppListResult? apps = _latestApps;
            SkillsListResult? skills = _latestSkills;

            if (_sessionService is not null && _sessionService.IsConnected)
            {
                var appsTask = RequestAsync<AppListResult>(
                    "app/list",
                    new { cursor = (string?)null, limit = 50, forceRefetch = false },
                    "Unable to read connectors.");
                var skillsTask = RequestAsync<SkillsListResult>(
                    "skills/list",
                    new { cwds = new[] { WorkingDirectory }, forceReload = false },
                    "Unable to read skills.");

                await Task.WhenAll(appsTask, skillsTask).ConfigureAwait(false);
                apps = appsTask.Result;
                skills = skillsTask.Result;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                _latestApps = apps;
                _latestSkills = skills;
                _connectorSummary = apps?.Data is { Count: > 0 } appData ? $"{appData.Count} connectors" : "0 connectors";
                _accessSummary = BuildAccessSummary(_latestRequirements?.Requirements, skills?.Data);
                RebuildStatusChips();
                ShowWorkspacePanel = true;

                var card = BuildAppsAndSkillsCard(apps, skills);
                UpsertUtilityConversationCard(card);
                ComposerHint = "Apps and skills catalog loaded.";
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task ShowSettingsAsync()
    {
        IsBusy = true;

        try
        {
            ConfigRequirementsReadResult? requirements = _latestRequirements;
            JsonElement? configRead = _latestConfigSnapshot;
            JsonElement? experimentalFeatures = _latestExperimentalFeatures;
            JsonElement? mcpServerStatuses = _latestMcpServerStatuses;
            JsonElement? loadedThreads = _latestLoadedThreads;
            if (_sessionService is not null && _sessionService.IsConnected)
            {
                var requirementsTask = RequestAsync<ConfigRequirementsReadResult>(
                    "configRequirements/read",
                    new { },
                    "Unable to read requirements.");
                var configTask = RequestAsync<JsonElement>(
                    "config/read",
                    new
                    {
                        includeLayers = true,
                        cwd = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory
                    },
                    "Unable to read active config.");
                var experimentalFeaturesTask = RequestAsync<JsonElement>(
                    "experimentalFeature/list",
                    new { cursor = (string?)null, limit = 100 },
                    "Unable to read experimental features.");
                var mcpStatusesTask = RequestAsync<JsonElement>(
                    "mcpServerStatus/list",
                    new { cursor = (string?)null, limit = 100 },
                    "Unable to read MCP server status.");
                var loadedThreadsTask = RequestAsync<JsonElement>(
                    "thread/loaded/list",
                    new { cursor = (string?)null, limit = 200 },
                    "Unable to read loaded thread sessions.");

                await Task.WhenAll(requirementsTask, configTask, experimentalFeaturesTask, mcpStatusesTask, loadedThreadsTask).ConfigureAwait(false);

                requirements = requirementsTask.Result ?? requirements;

                if (IsDefinedJsonElement(configTask.Result))
                {
                    configRead = configTask.Result;
                }

                if (IsDefinedJsonElement(experimentalFeaturesTask.Result))
                {
                    experimentalFeatures = experimentalFeaturesTask.Result;
                }

                if (IsDefinedJsonElement(mcpStatusesTask.Result))
                {
                    mcpServerStatuses = mcpStatusesTask.Result;
                }

                if (IsDefinedJsonElement(loadedThreadsTask.Result))
                {
                    loadedThreads = loadedThreadsTask.Result;
                }
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                _latestRequirements = requirements ?? _latestRequirements;
                if (configRead is { } config && IsDefinedJsonElement(config))
                {
                    _latestConfigSnapshot = config;
                }

                if (experimentalFeatures is { } features && IsDefinedJsonElement(features))
                {
                    _latestExperimentalFeatures = features;
                }

                if (mcpServerStatuses is { } statuses && IsDefinedJsonElement(statuses))
                {
                    _latestMcpServerStatuses = statuses;
                }

                if (loadedThreads is { } loaded && IsDefinedJsonElement(loaded))
                {
                    _latestLoadedThreads = loaded;
                }

                ShowWorkspacePanel = true;
                var card = BuildSettingsCard(
                    _latestRequirements,
                    _latestConfigSnapshot,
                    _latestExperimentalFeatures,
                    _latestMcpServerStatuses,
                    _latestLoadedThreads);
                UpsertUtilityConversationCard(card);
                ComposerHint = "Settings summary loaded. Review policies, config, and runtime capabilities before starting the next turn.";
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task ReloadMcpServersAsync()
    {
        if (_sessionService is null || !_sessionService.IsConnected)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await RequestAsync<JsonElement>(
                "config/mcpServer/reload",
                new
                {
                    cwd = string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory
                },
                "Unable to reload MCP servers.").ConfigureAwait(false);

            var mcpStatuses = await RequestAsync<JsonElement>(
                "mcpServerStatus/list",
                new { cursor = (string?)null, limit = 100 },
                "Unable to read MCP server status after reload.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (IsDefinedJsonElement(mcpStatuses))
                {
                    _latestMcpServerStatuses = mcpStatuses;
                }

                ShowWorkspacePanel = true;
                var card = BuildSettingsCard(
                    _latestRequirements,
                    _latestConfigSnapshot,
                    _latestExperimentalFeatures,
                    _latestMcpServerStatuses,
                    _latestLoadedThreads);
                UpsertUtilityConversationCard(card);
                ComposerHint = "MCP server reload requested. Runtime status has been refreshed.";
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private Task RefreshAsync() => RefreshDataAsync(setBusyState: true, backgroundRefresh: false, includeThreadDetail: true);

    private async Task RefreshDataAsync(bool setBusyState, bool backgroundRefresh, bool includeThreadDetail)
    {
        if (_sessionService is null || !_sessionService.IsConnected)
        {
            return;
        }

        if (backgroundRefresh)
        {
            if (!_refreshGate.Wait(0))
            {
                return;
            }
        }
        else
        {
            await _refreshGate.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            if (setBusyState)
            {
                await _uiDispatcher.InvokeAsync(() => IsBusy = true);
            }

            var selectionId = SelectedThread?.Id;

            var accountTask = RequestAsync<AccountReadResult>("account/read", new { refreshToken = false }, "Unable to read account state.");
            var modelsTask = RequestAsync<ModelListResult>("model/list", new { limit = 24, includeHidden = false }, "Unable to read models.");
            var threadsTask = RequestAsync<ThreadListResult>("thread/list", new { cursor = (string?)null, limit = 40, sortKey = "updated_at" }, "Unable to read threads.");
            var appsTask = RequestAsync<AppListResult>("app/list", new { cursor = (string?)null, limit = 50, forceRefetch = false }, "Unable to read connectors.");
            var skillsTask = RequestAsync<SkillsListResult>("skills/list", new { cwds = new[] { WorkingDirectory }, forceReload = false }, "Unable to read skills.");
            var requirementsTask = RequestAsync<ConfigRequirementsReadResult>("configRequirements/read", new { }, "Unable to read requirements.");
            var rateLimitsTask = RequestAsync<RateLimitsReadResult>("account/rateLimits/read", new { }, "Unable to read account rate limits.");

            await Task.WhenAll(accountTask, modelsTask, threadsTask, appsTask, skillsTask, requirementsTask, rateLimitsTask).ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                ApplyOverview(accountTask.Result, modelsTask.Result, threadsTask.Result, appsTask.Result, skillsTask.Result, requirementsTask.Result, rateLimitsTask.Result);
                ApplyThreads(threadsTask.Result?.Data ?? Array.Empty<ThreadSummary>(), selectionId);
            });

            if (includeThreadDetail && SelectedThread is not null)
            {
                await LoadThreadDetailAsync(SelectedThread.Id, true).ConfigureAwait(false);
            }
            else if (includeThreadDetail)
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    SeedConversationPlaceholder();
                });
            }
        }
        finally
        {
            if (setBusyState)
            {
                await _uiDispatcher.InvokeAsync(() => IsBusy = false);
            }

            _refreshGate.Release();
        }
    }

    private async Task RefreshThreadListAsync(CancellationToken cancellationToken = default)
    {
        if (_sessionService is null || !_sessionService.IsConnected || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!_refreshGate.Wait(0))
        {
            return;
        }

        try
        {
            var selectionId = await _uiDispatcher.InvokeAsync(() => SelectedThread?.Id);
            var result = await RequestAsync<ThreadListResult>(
                "thread/list",
                new { cursor = (string?)null, limit = 40, sortKey = "updated_at" },
                "Unable to read threads.",
                cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || result is null)
            {
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                ApplyThreads(result.Data ?? Array.Empty<ThreadSummary>(), selectionId);
            });
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task LoadThreadDetailAsync(string threadId, bool backgroundRefresh, CancellationToken cancellationToken = default)
    {
        if (_sessionService is null || !_sessionService.IsConnected || string.IsNullOrWhiteSpace(threadId) || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (backgroundRefresh && _threadDetailCancellationTokenSource is not null)
        {
            return;
        }

        var nextCancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _threadDetailCancellationTokenSource, nextCancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        try
        {
            if (!backgroundRefresh)
            {
                await _uiDispatcher.InvokeAsync(() => IsBusy = true);
            }

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(nextCancellation.Token, cancellationToken);
            var result = await RequestAsync<ThreadReadResult>(
                "thread/read",
                new { threadId, includeTurns = true },
                "Unable to read thread details.",
                linkedCancellation.Token).ConfigureAwait(false);

            if (linkedCancellation.IsCancellationRequested || result?.Thread is null)
            {
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (HasThreadDetailChanged(result.Thread))
                {
                    ApplyThreadDetail(result.Thread);
                }
            });
        }
        finally
        {
            if (!backgroundRefresh)
            {
                await _uiDispatcher.InvokeAsync(() => IsBusy = false);
            }

            if (ReferenceEquals(_threadDetailCancellationTokenSource, nextCancellation))
            {
                _threadDetailCancellationTokenSource = null;
            }

            nextCancellation.Dispose();
        }
    }

    private async Task<T?> RequestAsync<T>(string method, object parameters, string failureMessage, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _sessionService!.SendRequestAsync<T>(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return default;
        }
        catch (Exception exception) when (IsHandledSessionException(exception))
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                ConnectionDetail = $"{failureMessage} {exception.Message}";
            });
            return default;
        }
    }

    private void ApplyOverview(
        AccountReadResult? account,
        ModelListResult? models,
        ThreadListResult? threads,
        AppListResult? apps,
        SkillsListResult? skills,
        ConfigRequirementsReadResult? requirements,
        RateLimitsReadResult? rateLimits)
    {
        _latestApps = apps;
        _latestSkills = skills;
        _latestRequirements = requirements;
        _accountSummary = account?.Account?.Email ?? account?.Account?.Type ?? (account?.RequiresOpenaiAuth == true ? "Auth required" : "No account");
        _requiresOpenaiAuth = account?.RequiresOpenaiAuth == true;
        _modelSummary = models?.Data?.FirstOrDefault(static model => model.IsDefault)?.DisplayName
            ?? models?.Data?.FirstOrDefault()?.DisplayName
            ?? "No models";
        UpdateAvailableModels(models);
        _threadSummary = threads?.Data is { Count: > 0 } data ? $"{data.Count} sessions" : "0 sessions";
        _connectorSummary = apps?.Data is { Count: > 0 } appData ? $"{appData.Count} connectors" : "0 connectors";
        _accessSummary = BuildAccessSummary(requirements?.Requirements, skills?.Data);
        _rateLimitSummary = BuildRateLimitSummary(rateLimits);
        LoginCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();

        RebuildStatusChips();
    }

    private void ApplyThreads(IEnumerable<ThreadSummary> threads, string? selectionId)
    {
        var nextSelectionId = selectionId ?? _selectedThreadId;
        var previousSelection = SelectedThread;
        var mapped = threads
            .OrderByDescending(static thread => thread.UpdatedAt ?? thread.CreatedAt ?? 0)
            .Select(MapThread)
            .ToList();

        if (!string.IsNullOrWhiteSpace(nextSelectionId) &&
            mapped.All(thread => !string.Equals(thread.Id, nextSelectionId, StringComparison.Ordinal)) &&
            previousSelection is not null &&
            string.Equals(previousSelection.Id, nextSelectionId, StringComparison.Ordinal))
        {
            mapped.Insert(0, previousSelection);
        }

        _isApplyingThreadList = true;
        try
        {
            ReplaceCollection(RecentThreads, mapped);

            if (!string.IsNullOrWhiteSpace(nextSelectionId))
            {
                var selected = mapped.FirstOrDefault(thread => string.Equals(thread.Id, nextSelectionId, StringComparison.Ordinal));
                if (selected is not null)
                {
                    SelectedThread = selected;
                }
            }
            else
            {
                SelectedThread = mapped.FirstOrDefault();
            }
        }
        finally
        {
            _isApplyingThreadList = false;
        }

        OnPropertyChanged(nameof(ThreadCountLabel));
    }

    private bool HasThreadDetailChanged(ThreadDetail thread)
    {
        return _currentThreadDetail is null
            || !string.Equals(
                BuildThreadDetailFingerprint(_currentThreadDetail),
                BuildThreadDetailFingerprint(thread),
                StringComparison.Ordinal);
    }

    private static string BuildThreadDetailFingerprint(ThreadDetail thread)
    {
        var turns = thread.Turns ?? Array.Empty<ThreadTurn>();
        var lastTurn = turns.LastOrDefault();
        var lastItems = lastTurn?.Items ?? Array.Empty<ThreadItem>();
        var lastItem = lastItems.LastOrDefault();

        return string.Join('|',
            thread.Id ?? string.Empty,
            thread.UpdatedAt?.ToString() ?? string.Empty,
            thread.Status?.Type ?? string.Empty,
            turns.Count.ToString(),
            lastTurn?.Id ?? string.Empty,
            lastTurn?.Status ?? string.Empty,
            lastItems.Count.ToString(),
            lastItem?.Id ?? string.Empty,
            lastItem?.Type ?? string.Empty,
            lastItem?.Status ?? string.Empty,
            thread.Preview ?? string.Empty);
    }

    private void ApplyThreadDetail(ThreadDetail thread)
    {
        _currentThreadDetail = thread;
        _activeTurnId = thread.Turns?.LastOrDefault(static turn => string.Equals(turn.Status, "inProgress", StringComparison.OrdinalIgnoreCase))?.Id;
        CurrentWorkspaceTitle = thread.Name ?? thread.Preview ?? thread.Id ?? "Codex thread";
        CurrentWorkspaceSubtitle = $"{thread.ModelProvider ?? "openai"} · {thread.Status?.Type ?? "notLoaded"} · {FormatRelativeTime(thread.UpdatedAt ?? thread.CreatedAt)}";

        var selectedItemId = SelectedConversationItem?.ItemId;
        var items = BuildConversationItems(thread);
        ReplaceCollection(ConversationItems, items);
        SelectedConversationItem = items.FirstOrDefault(item => item.ItemId == selectedItemId)
            ?? items.FirstOrDefault(item => item.Kind is "fileChange" or "turnDiff" or "commandExecution" or "mcpToolCall" or "dynamicToolCall")
            ?? items.FirstOrDefault();

        NotifyCollectionStateChanged();
        NotifyCommandStates();
    }

    private void SeedConversationPlaceholder()
    {
        ReplaceCollection(ConversationItems,
        [
            new ConversationItemViewModel(
                "placeholder",
                "placeholder",
                "placeholder",
                "assistant",
                "Codex",
                "Select a thread or start a new one to inspect commands, diffs, tool calls, and live approval prompts.",
                "Phase two shell",
                string.Empty,
                null,
                null,
                null,
                null,
                "markdown",
                "session-summary.md",
                "No conversation selected",
                BuildWelcomeDocument(),
                ShellBrushes.Green,
                ShellBrushes.Paper,
                ShellBrushes.TextPrimary)
        ]);
        SelectedConversationItem = null;
    }

    private void RebuildStatusChips()
    {
        var chips = new List<StatusChipViewModel>
        {
            new("Connection", ConnectionState, ConnectionBrush, IsConnected ? ShellBrushes.GreenSoft : ShellBrushes.NeutralSoft),
            new("Account", _accountSummary, ShellBrushes.Blue, ShellBrushes.BlueSoft),
            new("Rate limits", _rateLimitSummary, ShellBrushes.Amber, ShellBrushes.AmberSoft),
            new("Model", _modelSummary, ShellBrushes.Green, ShellBrushes.GreenSoft),
            new("Threads", _threadSummary, ShellBrushes.Amber, ShellBrushes.AmberSoft),
            new("Connectors", _connectorSummary, ShellBrushes.Blue, ShellBrushes.BlueSoft),
            new("Access", _accessSummary, ShellBrushes.Neutral, ShellBrushes.NeutralSoft)
        };

        if (HasActiveLogin)
        {
            chips.Add(new StatusChipViewModel("Login", "In progress", ShellBrushes.Blue, ShellBrushes.BlueSoft));
        }

        ReplaceCollection(StatusChips, chips);
    }

    private ThreadListEntryViewModel MapThread(ThreadSummary thread)
    {
        var accent = (thread.Status?.Type ?? "notLoaded") switch
        {
            "active" => ShellBrushes.Green,
            "idle" => ShellBrushes.Blue,
            "systemError" => ShellBrushes.Red,
            _ => ShellBrushes.Neutral
        };

        return new ThreadListEntryViewModel(
            thread.Id ?? string.Empty,
            thread.Name ?? thread.Preview ?? "Untitled thread",
            string.IsNullOrWhiteSpace(thread.Preview) ? "No preview available." : Shorten(thread.Preview, 140),
            FormatRelativeTime(thread.UpdatedAt ?? thread.CreatedAt),
            thread.ModelProvider ?? "openai",
            thread.Ephemeral ? "ephemeral" : thread.Status?.Type ?? "notLoaded",
            accent,
            ShellBrushes.Paper,
            thread.Status?.Type ?? "notLoaded");
    }

    private void OnNotificationReceived(object? sender, AppServerNotificationEventArgs eventArgs)
    {
        var method = eventArgs.Notification.Method;
        var notificationThreadId = TryGetString(eventArgs.Notification.Parameters, "threadId")
            ?? TryGetString(eventArgs.Notification.Parameters, "thread", "id");
        var selectedThreadId = SelectedThread?.Id;

        if (string.IsNullOrWhiteSpace(notificationThreadId)
            || string.Equals(notificationThreadId, selectedThreadId, StringComparison.Ordinal))
        {
            _lastNotificationReceivedAt = DateTimeOffset.UtcNow;
        }

        _uiDispatcher.Post(() =>
        {
            HandlePhaseTwoNotification(method, eventArgs.Notification.Parameters);
        });

        if (ShouldRefreshThreadListForNotification(method))
        {
            _ = RefreshThreadListAsync();
        }

        if (ShouldRefreshForNotification(method))
        {
            _ = RefreshDataAsync(setBusyState: false, backgroundRefresh: true, includeThreadDetail: false);
        }
    }

    private void OnConnectionStateChanged(object? sender, AppServerConnectionChangedEventArgs eventArgs)
    {
        _uiDispatcher.Post(() =>
        {
            IsConnected = eventArgs.IsConnected;
            ConnectionState = eventArgs.Status;
            ConnectionDetail = eventArgs.Detail ?? ConnectionDetail;
            RebuildStatusChips();

            if (eventArgs.IsConnected)
            {
                StartLiveUpdates();
            }
            else
            {
                StopLiveUpdates();
            }
        });
    }

    private void StartLiveUpdates()
    {
        if (_sessionService is null || !_sessionService.IsConnected || _liveUpdatesActive)
        {
            return;
        }

        _liveUpdatesActive = true;
        _lastNotificationReceivedAt = DateTimeOffset.UtcNow;
        StartLiveUpdateLoop();
    }

    private void StopLiveUpdates()
    {
        if (!_liveUpdatesActive)
        {
            return;
        }

        _liveUpdatesActive = false;
        StopLiveUpdateLoop();
    }

    private void StartLiveUpdateLoop()
    {
        if (_liveUpdateLoopTask is { IsCompleted: false })
        {
            return;
        }

        var previousLoopCancellation = _liveUpdateLoopCancellationTokenSource;
        var previousLoopTask = _liveUpdateLoopTask;
        _liveUpdateLoopCancellationTokenSource = null;
        _liveUpdateLoopTask = null;

        if (previousLoopCancellation is not null)
        {
            previousLoopCancellation.Cancel();
            if (previousLoopTask is { IsCompleted: false })
            {
                _ = previousLoopTask.ContinueWith(
                    _ => previousLoopCancellation.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else
            {
                previousLoopCancellation.Dispose();
            }
        }

        var nextLoopCancellation = new CancellationTokenSource();
        var nextLoopToken = nextLoopCancellation.Token;
        _liveUpdateLoopCancellationTokenSource = nextLoopCancellation;
        _liveUpdateLoopTask = Task.Run(() => RunLiveUpdateLoopAsync(nextLoopToken));
    }

    private void StopLiveUpdateLoop()
    {
        var cancellation = _liveUpdateLoopCancellationTokenSource;
        var loopTask = _liveUpdateLoopTask;
        _liveUpdateLoopCancellationTokenSource = null;
        _liveUpdateLoopTask = null;

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (loopTask is { IsCompleted: false })
        {
            _ = loopTask.ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        else
        {
            cancellation.Dispose();
        }
    }

    private async Task RunLiveUpdateLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!_liveUpdatesActive || _sessionService is null || !_sessionService.IsConnected)
            {
                continue;
            }

            if (DateTimeOffset.UtcNow - _lastNotificationReceivedAt < TimeSpan.FromSeconds(2))
            {
                continue;
            }

            try
            {
                await RefreshThreadListAsync(cancellationToken).ConfigureAwait(false);
                var selectedThreadId = await _uiDispatcher.InvokeAsync(() => SelectedThread?.Id);
                if (!string.IsNullOrWhiteSpace(selectedThreadId))
                {
                    await LoadThreadDetailAsync(selectedThreadId, true, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (IsHandledSessionException(exception))
            {
                await _uiDispatcher.InvokeAsync(() =>
                {
                    ConnectionDetail = $"Live refresh warning: {exception.Message}";
                });
            }
        }
    }

    private void NotifyCommandStates()
    {
        if (!_uiDispatcher.CheckAccess())
        {
            _uiDispatcher.Post(NotifyCommandStates);
            return;
        }

        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        LoginCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
        CancelLoginCommand.NotifyCanExecuteChanged();
        ShowThreadsCommand.NotifyCanExecuteChanged();
        ShowAppsCommand.NotifyCanExecuteChanged();
        ShowSettingsCommand.NotifyCanExecuteChanged();
        ReloadMcpServersCommand.NotifyCanExecuteChanged();
        NewThreadCommand.NotifyCanExecuteChanged();
        ForkThreadCommand.NotifyCanExecuteChanged();
        ArchiveThreadCommand.NotifyCanExecuteChanged();
        UnarchiveThreadCommand.NotifyCanExecuteChanged();
        RenameThreadCommand.NotifyCanExecuteChanged();
        CompactThreadCommand.NotifyCanExecuteChanged();
        RollbackThreadCommand.NotifyCanExecuteChanged();
        SendTurnCommand.NotifyCanExecuteChanged();
        SteerTurnCommand.NotifyCanExecuteChanged();
        InterruptTurnCommand.NotifyCanExecuteChanged();
        StartReviewCommand.NotifyCanExecuteChanged();
        RebuildStatusChips();
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(ThreadCountLabel));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();

        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private static string BuildAccessSummary(ConfigRequirementsInfo? requirements, ICollection<SkillsByWorkingDirectory>? skills)
    {
        if (requirements?.AllowedSandboxModes is { Count: > 0 })
        {
            return string.Join(", ", requirements.AllowedSandboxModes);
        }

        if (skills is { Count: > 0 })
        {
            var skillCount = skills.SelectMany(static group => group.Skills ?? Array.Empty<SkillInfo>()).Count();
            return skillCount == 0 ? "Read-only MVP" : $"{skillCount} skills";
        }

        return "Read-only MVP";
    }

    private static string BuildRateLimitSummary(RateLimitsReadResult? rateLimits)
    {
        var primary = rateLimits?.RateLimits?.Primary;
        if (primary?.UsedPercent is int usedPercent)
        {
            return $"{usedPercent}% used";
        }

        var firstBucket = rateLimits?.RateLimitsByLimitId?.Values.FirstOrDefault();
        if (firstBucket?.Primary?.UsedPercent is int firstUsedPercent)
        {
            return $"{firstUsedPercent}% used";
        }

        return "No limits";
    }

    private ConversationItemViewModel BuildAppsAndSkillsCard(AppListResult? apps, SkillsListResult? skills)
    {
        var appData = apps?.Data?.ToList() ?? [];
        var skillGroups = skills?.Data?.ToList() ?? [];
        var allSkills = skillGroups
            .SelectMany(static group => group.Skills ?? Array.Empty<SkillInfo>())
            .ToList();

        var appCount = appData.Count;
        var enabledAppCount = appData.Count(static app => app.IsEnabled);
        var accessibleAppCount = appData.Count(static app => app.IsAccessible);
        var skillCount = allSkills.Count;
        var enabledSkillCount = allSkills.Count(static skill => skill.Enabled);

        var body = appCount == 0 && skillCount == 0
            ? "No connectors or skills were reported for this workspace."
            : $"Catalog snapshot for {WorkingDirectoryName}: {appCount} connectors ({enabledAppCount} enabled, {accessibleAppCount} accessible) and {skillCount} skills ({enabledSkillCount} enabled).";

        var connectorPreview = appData.Count == 0
            ? null
            : string.Join('\n', appData.Take(8).Select(app =>
            {
                var status = app.IsEnabled ? "enabled" : "disabled";
                var access = app.IsAccessible ? "accessible" : "restricted";
                var name = string.IsNullOrWhiteSpace(app.Name) ? app.Id : app.Name;
                return $"- {name} ({status}, {access})";
            }));

        var skillsPreview = allSkills.Count == 0
            ? null
            : string.Join('\n', allSkills.Take(10).Select(skill =>
            {
                var status = skill.Enabled ? "enabled" : "disabled";
                return $"- {skill.Name} ({status})";
            }));

        return CreateConversationCard(
            itemId: "utility:apps",
            turnId: "utility",
            kind: "appsCatalog",
            role: "system",
            title: "Apps & Skills",
            body: body,
            meta: $"Workspace · {WorkingDirectoryName}",
            badge: appCount == 0 ? "No connectors" : $"{appCount} connectors",
            codePreviewLabel: connectorPreview is null ? null : "Connectors",
            codePreview: connectorPreview,
            outputPreviewLabel: skillsPreview is null ? null : "Skills",
            outputPreview: skillsPreview,
            documentKind: "markdown",
            documentName: "apps-and-skills.md",
            documentMeta: $"{appCount} connectors · {skillCount} skills",
            documentText: BuildAppsAndSkillsDocument(appData, skillGroups),
            accentBrush: ShellBrushes.Blue,
            surfaceBrush: ShellBrushes.BlueSoft);
    }

    private ConversationItemViewModel BuildSettingsCard(
        ConfigRequirementsReadResult? requirements,
        JsonElement? configRead,
        JsonElement? experimentalFeatures,
        JsonElement? mcpServerStatuses,
        JsonElement? loadedThreads)
    {
        var requirementInfo = requirements?.Requirements;
        var policies = requirementInfo?.AllowedApprovalPolicies?
            .Where(static policy => !string.IsNullOrWhiteSpace(policy))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static policy => policy, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var sandboxModes = requirementInfo?.AllowedSandboxModes?
            .Where(static mode => !string.IsNullOrWhiteSpace(mode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static mode => mode, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var networkEnabled = requirementInfo?.Network?.Enabled == true;
        var domainCount = requirementInfo?.Network?.AllowedDomains?.Count ?? 0;
        var configLayerCount = CountArrayProperty(configRead, "layers");
        var experimentalFeatureCount = CountArrayProperty(experimentalFeatures, "data");
        var enabledExperimentalFeatureCount = CountEnabledExperimentalFeatures(experimentalFeatures);
        var mcpServerCount = CountArrayProperty(mcpServerStatuses, "data");
        var authenticatedMcpServerCount = CountAuthenticatedMcpServers(mcpServerStatuses);
        var loadedThreadCount = CountArrayProperty(loadedThreads, "data");

        var body = requirementInfo is null
            ? "No runtime requirements were reported by the connected app-server."
            : $"Runtime policy snapshot: approvals {DescribeListOrFallback(policies, "not specified")}, sandbox {DescribeListOrFallback(sandboxModes, "not specified")}, network {(networkEnabled ? "enabled" : "disabled")} ({domainCount} domains), config layers {configLayerCount}, experimental features {experimentalFeatureCount} ({enabledExperimentalFeatureCount} enabled), MCP servers {mcpServerCount} ({authenticatedMcpServerCount} authenticated), loaded threads {loadedThreadCount}.";

        var connectionPreview = string.Join('\n',
        [
            $"- command: {CommandPath}",
            $"- arguments: {CommandArguments}",
            $"- working directory: {WorkingDirectory}"
        ]);

        var requirementsPreview = requirementInfo is null
            ? "No requirements reported."
            : string.Join('\n',
            [
                $"- approval policies: {DescribeListOrFallback(policies, "not specified")}",
                $"- sandbox modes: {DescribeListOrFallback(sandboxModes, "not specified")}",
                $"- network: {(networkEnabled ? "enabled" : "disabled")}",
                $"- allowed domains: {domainCount}",
                $"- config layers: {configLayerCount}",
                $"- experimental features: {experimentalFeatureCount} ({enabledExperimentalFeatureCount} enabled)",
                $"- MCP servers: {mcpServerCount} ({authenticatedMcpServerCount} authenticated)",
                $"- loaded threads: {loadedThreadCount}"
            ]);

        return CreateConversationCard(
            itemId: "utility:settings",
            turnId: "utility",
            kind: "settingsSnapshot",
            role: "system",
            title: "Settings & Policies",
            body: body,
            meta: $"Workspace · {WorkingDirectoryName}",
            badge: requirementInfo is null ? "No requirements" : "Requirements",
            codePreviewLabel: "Connection",
            codePreview: connectionPreview,
            outputPreviewLabel: "Requirements",
            outputPreview: requirementsPreview,
            documentKind: "markdown",
            documentName: "settings-and-policies.md",
            documentMeta: "Runtime settings snapshot",
            documentText: BuildSettingsDocument(requirements, configRead, experimentalFeatures, mcpServerStatuses, loadedThreads),
            accentBrush: ShellBrushes.Neutral,
            surfaceBrush: ShellBrushes.NeutralSoft);
    }

    private static string BuildAppsAndSkillsDocument(
        IReadOnlyCollection<AppInfo> apps,
        IReadOnlyCollection<SkillsByWorkingDirectory> skillsByDirectory)
    {
        var lines = new List<string>
        {
            "# Apps & Skills",
            string.Empty,
            "## Connectors",
            $"- total: {apps.Count}",
            $"- enabled: {apps.Count(static app => app.IsEnabled)}",
            $"- accessible: {apps.Count(static app => app.IsAccessible)}",
            string.Empty
        };

        if (apps.Count == 0)
        {
            lines.Add("_No connectors reported._");
            lines.Add(string.Empty);
        }
        else
        {
            foreach (var app in apps.Take(25))
            {
                var name = string.IsNullOrWhiteSpace(app.Name) ? app.Id : app.Name;
                lines.Add($"- **{name}** (`{app.Id}`) · {(app.IsEnabled ? "enabled" : "disabled")} · {(app.IsAccessible ? "accessible" : "restricted")}");
                if (!string.IsNullOrWhiteSpace(app.Description))
                {
                    lines.Add($"  - {app.Description}");
                }
            }

            if (apps.Count > 25)
            {
                lines.Add($"- …and {apps.Count - 25} more connectors");
            }

            lines.Add(string.Empty);
        }

        var allSkills = skillsByDirectory
            .SelectMany(static group => group.Skills ?? Array.Empty<SkillInfo>())
            .ToList();

        lines.Add("## Skills");
        lines.Add($"- total: {allSkills.Count}");
        lines.Add($"- enabled: {allSkills.Count(static skill => skill.Enabled)}");
        lines.Add(string.Empty);

        if (skillsByDirectory.Count == 0)
        {
            lines.Add("_No skills reported._");
            lines.Add(string.Empty);
            return string.Join('\n', lines);
        }

        foreach (var group in skillsByDirectory)
        {
            lines.Add($"### {group.Cwd}");

            var groupSkills = group.Skills?.ToList() ?? [];
            if (groupSkills.Count == 0)
            {
                lines.Add("_No skills found in this workspace._");
            }
            else
            {
                foreach (var skill in groupSkills.Take(20))
                {
                    lines.Add($"- **{skill.Name}** · {(skill.Enabled ? "enabled" : "disabled")}");
                    if (!string.IsNullOrWhiteSpace(skill.Description))
                    {
                        lines.Add($"  - {skill.Description}");
                    }
                }

                if (groupSkills.Count > 20)
                {
                    lines.Add($"- …and {groupSkills.Count - 20} more skills");
                }
            }

            if (group.Errors is { Count: > 0 })
            {
                lines.Add(string.Empty);
                lines.Add("Errors:");
                foreach (var error in group.Errors.Take(5))
                {
                    lines.Add($"- {error}");
                }
            }

            lines.Add(string.Empty);
        }

        return string.Join('\n', lines);
    }

    private static string BuildSettingsDocument(
        ConfigRequirementsReadResult? requirements,
        JsonElement? configRead,
        JsonElement? experimentalFeatures,
        JsonElement? mcpServerStatuses,
        JsonElement? loadedThreads)
    {
        var requirementInfo = requirements?.Requirements;
        var lines = new List<string>
        {
            "# Settings & Policies",
            string.Empty
        };

        if (requirementInfo is null)
        {
            lines.Add("_No requirements were returned by the app-server._");
            return string.Join('\n', lines);
        }

        lines.Add("## Approvals");
        if (requirementInfo.AllowedApprovalPolicies is { Count: > 0 })
        {
            foreach (var policy in requirementInfo.AllowedApprovalPolicies.OrderBy(static policy => policy, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"- {policy}");
            }
        }
        else
        {
            lines.Add("- not specified");
        }

        lines.Add(string.Empty);
        lines.Add("## Sandbox Modes");
        if (requirementInfo.AllowedSandboxModes is { Count: > 0 })
        {
            foreach (var mode in requirementInfo.AllowedSandboxModes.OrderBy(static mode => mode, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"- {mode}");
            }
        }
        else
        {
            lines.Add("- not specified");
        }

        lines.Add(string.Empty);
        lines.Add("## Feature Requirements");
        if (requirementInfo.FeatureRequirements is { Count: > 0 })
        {
            foreach (var feature in requirementInfo.FeatureRequirements.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"- {feature.Key}: {(feature.Value ? "required" : "optional")}");
            }
        }
        else
        {
            lines.Add("- not specified");
        }

        lines.Add(string.Empty);
        lines.Add("## Network");
        var network = requirementInfo.Network;
        lines.Add($"- enabled: {(network?.Enabled == true ? "true" : "false")}");
        if (network?.AllowedDomains is { Count: > 0 })
        {
            lines.Add("- allowed domains:");
            foreach (var domain in network.AllowedDomains.OrderBy(static domain => domain, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"  - {domain}");
            }
        }
        else
        {
            lines.Add("- allowed domains: unrestricted");
        }

        lines.Add(string.Empty);
        lines.Add("## Runtime Config");
        lines.Add($"- layers returned: {CountArrayProperty(configRead, "layers")}");
        lines.Add($"- top-level keys: {DescribeConfigTopLevelKeys(configRead)}");

        lines.Add(string.Empty);
        lines.Add("## Experimental Features");
        foreach (var featureLine in BuildExperimentalFeatureLines(experimentalFeatures))
        {
            lines.Add(featureLine);
        }

        lines.Add(string.Empty);
        lines.Add("## MCP Server Status");
        foreach (var mcpLine in BuildMcpServerStatusLines(mcpServerStatuses))
        {
            lines.Add(mcpLine);
        }

        lines.Add(string.Empty);
        lines.Add("## Loaded Thread Sessions");
        foreach (var threadLine in BuildLoadedThreadLines(loadedThreads))
        {
            lines.Add(threadLine);
        }

        return string.Join('\n', lines);
    }

    private static int CountArrayProperty(JsonElement? element, string propertyName)
    {
        if (!TryGetArrayProperty(element, propertyName, out var array))
        {
            return 0;
        }

        var count = 0;
        foreach (var _ in array.EnumerateArray())
        {
            count++;
        }

        return count;
    }

    private static bool IsDefinedJsonElement(JsonElement element)
        => element.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;

    private static int CountEnabledExperimentalFeatures(JsonElement? experimentalFeatures)
    {
        if (!TryGetArrayProperty(experimentalFeatures, "data", out var data))
        {
            return 0;
        }

        var enabledCount = 0;
        foreach (var feature in data.EnumerateArray())
        {
            if (feature.ValueKind == JsonValueKind.Object
                && feature.TryGetProperty("enabled", out var enabled)
                && enabled.ValueKind == JsonValueKind.True)
            {
                enabledCount++;
            }
        }

        return enabledCount;
    }

    private static int CountAuthenticatedMcpServers(JsonElement? mcpServerStatuses)
    {
        if (!TryGetArrayProperty(mcpServerStatuses, "data", out var data))
        {
            return 0;
        }

        var authenticatedCount = 0;
        foreach (var server in data.EnumerateArray())
        {
            var authStatus = TryGetString(server, "authStatus");
            if (string.Equals(authStatus, "bearerToken", StringComparison.OrdinalIgnoreCase)
                || string.Equals(authStatus, "oAuth", StringComparison.OrdinalIgnoreCase))
            {
                authenticatedCount++;
            }
        }

        return authenticatedCount;
    }

    private static string DescribeConfigTopLevelKeys(JsonElement? configRead)
    {
        if (configRead is not { ValueKind: JsonValueKind.Object } configRoot
            || !configRoot.TryGetProperty("config", out var config)
            || config.ValueKind != JsonValueKind.Object)
        {
            return "not available";
        }

        var keys = new List<string>();
        var remaining = 0;
        foreach (var property in config.EnumerateObject())
        {
            if (keys.Count < 8)
            {
                keys.Add(property.Name);
            }
            else
            {
                remaining++;
            }
        }

        if (keys.Count == 0)
        {
            return "none";
        }

        var summary = string.Join(", ", keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
        return remaining > 0
            ? $"{summary}, +{remaining} more"
            : summary;
    }

    private static IReadOnlyList<string> BuildExperimentalFeatureLines(JsonElement? experimentalFeatures)
    {
        if (!TryGetArrayProperty(experimentalFeatures, "data", out var data))
        {
            return ["- unavailable"];
        }

        var lines = new List<string>();
        var total = 0;
        foreach (var feature in data.EnumerateArray())
        {
            total++;
            if (lines.Count >= 12)
            {
                continue;
            }

            var name = TryGetString(feature, "name") ?? "feature";
            var stage = TryGetString(feature, "stage") ?? "unknown";
            var enabled = feature.TryGetProperty("enabled", out var enabledElement)
                          && enabledElement.ValueKind == JsonValueKind.True;
            lines.Add($"- {name} · {stage} · {(enabled ? "enabled" : "disabled")}");
        }

        if (total == 0)
        {
            lines.Add("- none");
        }
        else if (total > lines.Count)
        {
            lines.Add($"- ...and {total - lines.Count} more features");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildMcpServerStatusLines(JsonElement? mcpServerStatuses)
    {
        if (!TryGetArrayProperty(mcpServerStatuses, "data", out var data))
        {
            return ["- unavailable"];
        }

        var lines = new List<string>();
        var total = 0;
        foreach (var server in data.EnumerateArray())
        {
            total++;
            if (lines.Count >= 12)
            {
                continue;
            }

            var name = TryGetString(server, "name") ?? "server";
            var authStatus = TryGetString(server, "authStatus") ?? "unknown";
            lines.Add($"- {name}: {authStatus}");
        }

        if (total == 0)
        {
            lines.Add("- none");
        }
        else if (total > lines.Count)
        {
            lines.Add($"- ...and {total - lines.Count} more servers");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildLoadedThreadLines(JsonElement? loadedThreads)
    {
        if (!TryGetArrayProperty(loadedThreads, "data", out var data))
        {
            return ["- unavailable"];
        }

        var lines = new List<string>();
        var total = 0;
        foreach (var threadIdElement in data.EnumerateArray())
        {
            total++;
            if (lines.Count >= 12)
            {
                continue;
            }

            if (threadIdElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var threadId = threadIdElement.GetString();
            if (string.IsNullOrWhiteSpace(threadId))
            {
                continue;
            }

            lines.Add($"- {threadId}");
        }

        if (total == 0)
        {
            lines.Add("- none");
        }
        else if (total > lines.Count)
        {
            lines.Add($"- ...and {total - lines.Count} more loaded thread ids");
        }

        return lines;
    }

    private static bool TryGetArrayProperty(JsonElement? element, string propertyName, out JsonElement array)
    {
        if (element is { ValueKind: JsonValueKind.Object } objectElement
            && objectElement.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Array)
        {
            array = property;
            return true;
        }

        array = default;
        return false;
    }

    private void UpsertUtilityConversationCard(ConversationItemViewModel card)
    {
        var index = FindConversationItemIndexById(card.ItemId);
        if (index >= 0)
        {
            ConversationItems[index] = card;
        }
        else
        {
            ConversationItems.Insert(0, card);
        }

        SelectedConversationItem = card;
    }

    private static string DescribeListOrFallback(IReadOnlyCollection<string> values, string fallback)
        => values.Count == 0 ? fallback : string.Join(", ", values);

    private static string BuildWelcomeDocument()
    {
        return string.Join('\n',
        [
            "# CodexGui session summary",
            "",
            "## Current phase",
            "- Avalonia client for Codex app-server",
            "- Light Codex-style shell with thread rail, conversation view, and composer",
            "- Turn authoring and approval prompts are enabled",
            "- Commands, diffs, and tool calls render with item-specific detail",
            "",
            "## What loads after connect",
            "- account/read",
            "- model/list",
            "- thread/list",
            "- thread/read",
            "- skills/list",
            "- app/list",
            "- configRequirements/read",
            "",
            "## Next",
            "- Select a thread to hydrate the center conversation",
            "- Use the composer to start or interrupt turns"
        ]);
    }

    private static string BuildThreadDocument(ThreadDetail thread)
    {
        var lines = new List<string>
        {
            $"# Thread Summary: {thread.Name ?? thread.Preview ?? thread.Id ?? "Untitled thread"}",
            string.Empty,
            "## Metadata",
            $"- id: {thread.Id ?? "unknown"}",
            $"- provider: {thread.ModelProvider ?? "openai"}",
            $"- status: {thread.Status?.Type ?? "notLoaded"}",
            $"- cwd: {thread.Cwd ?? "not reported"}",
            $"- updated: {FormatRelativeTime(thread.UpdatedAt ?? thread.CreatedAt)}",
            string.Empty,
            "## Turns"
        };

        var turnIndex = 1;
        foreach (var turn in thread.Turns ?? Array.Empty<ThreadTurn>())
        {
            lines.Add($"### {turnIndex}. {turn.Id ?? "turn"} · {turn.Status ?? "unknown"}");

            foreach (var item in turn.Items ?? Array.Empty<ThreadItem>())
            {
                lines.Add($"- {HumanizeItemType(item.Type)}: {Shorten(DescribeThreadItem(item), 220)}");
            }

            if (!string.IsNullOrWhiteSpace(turn.Error?.Message))
            {
                lines.Add($"- error: {turn.Error.Message}");
            }

            lines.Add(string.Empty);
            turnIndex++;
        }

        if (turnIndex == 1)
        {
            lines.Add("- No persisted turns were returned for this thread.");
        }

        return string.Join('\n', lines);
    }

    private static string DescribeThreadItem(ThreadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            return item.Text;
        }

        return item.Type switch
        {
            "userMessage" => ExtractUserMessageText(item) ?? "User message",
            "commandExecution" => BuildCommandSummary(item.AdditionalPropertiesJson),
            "fileChange" => BuildFileChangeSummary(item.AdditionalPropertiesJson),
            _ => BuildFallbackSummary(item.AdditionalPropertiesJson)
        };
    }

    private static string? ExtractUserMessageText(ThreadItem item)
    {
        if (item.AdditionalPropertiesJson is null || !item.AdditionalPropertiesJson.TryGetValue("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var contentPart in content.EnumerateArray())
        {
            var text = TryGetString(contentPart, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(text);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string BuildCommandSummary(IDictionary<string, JsonElement>? properties)
    {
        if (properties is null || !properties.TryGetValue("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.Array)
        {
            return "Command execution";
        }

        var args = commandElement.EnumerateArray()
            .Select(static part => part.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value));
        return string.Join(' ', args!);
    }

    private static string BuildFileChangeSummary(IDictionary<string, JsonElement>? properties)
    {
        if (properties is null || !properties.TryGetValue("changes", out var changeElement) || changeElement.ValueKind != JsonValueKind.Array)
        {
            return "File change";
        }

        var previews = changeElement.EnumerateArray()
            .Select(change => $"{TryGetString(change, "kind") ?? "change"} {TryGetString(change, "path") ?? "file"}")
            .Take(3);
        return string.Join(" · ", previews);
    }

    private static string BuildFallbackSummary(IDictionary<string, JsonElement>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return "Structured protocol item";
        }

        var fragments = properties
            .Where(static pair => pair.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            .Take(3)
            .Select(pair => $"{pair.Key}: {pair.Value}");
        return string.Join(" · ", fragments);
    }

    private static string HumanizeItemType(string? itemType)
    {
        return itemType switch
        {
            "userMessage" => "User message",
            "agentMessage" => "Agent message",
            "reasoning" => "Reasoning",
            "plan" => "Plan",
            "commandExecution" => "Command",
            "fileChange" => "File change",
            "mcpToolCall" => "Connector tool",
            "dynamicToolCall" => "Dynamic tool",
            "webSearch" => "Web search",
            null or "" => "Item",
            _ => itemType
        };
    }

    private static IBrush AccentForItem(string? itemType)
    {
        return itemType switch
        {
            "userMessage" => ShellBrushes.Blue,
            "agentMessage" => ShellBrushes.Green,
            "reasoning" => ShellBrushes.Amber,
            "plan" => ShellBrushes.Amber,
            "commandExecution" => ShellBrushes.Blue,
            "fileChange" => ShellBrushes.Amber,
            _ => ShellBrushes.Neutral
        };
    }

    private static IBrush SurfaceForItem(string? itemType)
    {
        return itemType switch
        {
            "userMessage" => ShellBrushes.BlueSoft,
            "agentMessage" => ShellBrushes.Paper,
            "reasoning" => ShellBrushes.AmberSoft,
            "plan" => ShellBrushes.AmberSoft,
            "commandExecution" => ShellBrushes.BlueSoft,
            "fileChange" => ShellBrushes.AmberSoft,
            _ => ShellBrushes.NeutralSoft
        };
    }

    private static bool ShouldRefreshForNotification(string method)
    {
        return method is
            "account/updated" or
            "account/rateLimits/updated" or
            "account/login/completed" or
            "app/list/updated" or
            "skills/changed";
    }

    private static bool ShouldRefreshThreadListForNotification(string method)
    {
        return method is
            "thread/started" or
            "thread/status/changed" or
            "thread/name/updated" or
            "thread/archived" or
            "thread/unarchived" or
            "thread/closed" or
            "turn/started" or
            "turn/completed";
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 1)].TrimEnd() + "...";
    }

    private static string ShortenIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "session";
        }

        return value.Length <= 12 ? value : value[..12];
    }

    private static string FormatRelativeTime(long? unixTime)
    {
        if (unixTime is null)
        {
            return "time unavailable";
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTime.Value);
        var delta = DateTimeOffset.Now - timestamp;

        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)delta.TotalMinutes)}m";
        }

        if (delta.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)delta.TotalHours)}h";
        }

        return $"{Math.Max(1, (int)delta.TotalDays)}d";
    }

    private static bool IsRemoteEndpoint(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var endpoint) &&
               (string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHandledSessionException(Exception exception)
        => exception is
            AppServerException or
            IOException or
            InvalidOperationException or
            JsonException or
            SocketException or
            WebSocketException or
            Win32Exception or
            UnauthorizedAccessException or
            DirectoryNotFoundException;

    private static string? TryGetString(IDictionary<string, JsonElement> properties, string name)
    {
        return properties.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static string? TryGetString(JsonElement element, params string[] path)
    {
        var current = element;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static ThreadSummary? TryGetThreadFromResponse(JsonElement? response, out string? threadId)
    {
        threadId = null;
        if (response is not { ValueKind: JsonValueKind.Object } responseObject
            || !responseObject.TryGetProperty("thread", out var threadElement)
            || threadElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        threadId = TryGetString(threadElement, "id");
        return AppJson.Deserialize<ThreadSummary>(threadElement);
    }

    private readonly Dictionary<string, string> _latestTurnDiffs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestFileChangeDiffs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestAgentMessageDeltas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestPlanDeltas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestReasoningSummaryDeltas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestReasoningTextDeltas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestReasoningSummaryParts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestCommandOutputDeltas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _latestToolProgressMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TurnPlanUpdatedNotification> _latestTurnPlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<AppServerServerRequestCompletion>> _pendingServerRequestCompletions = new(StringComparer.Ordinal);

    private ThreadDetail? _currentThreadDetail;
    private string? _activeTurnId;

    [ObservableProperty]
    private string draftPrompt = string.Empty;

    [ObservableProperty]
    private string selectedModel = "gpt-5.4";

    [ObservableProperty]
    private string selectedApprovalPolicy = "on-request";

    [ObservableProperty]
    private ConversationItemViewModel? selectedConversationItem;

    public ObservableCollection<ConversationItemViewModel> ConversationItems { get; } = new();

    public ObservableCollection<PendingInteractionViewModel> PendingRequests { get; } = new();

    public ObservableCollection<string> AvailableModels { get; } = new() { "gpt-5.4" };

    public IReadOnlyList<string> AvailableApprovalPolicies { get; } = new[]
    {
        "on-request",
        "untrusted",
        "on-failure",
        "never"
    };

    public IAsyncRelayCommand NewThreadCommand { get; }

    public IAsyncRelayCommand ForkThreadCommand { get; }

    public IAsyncRelayCommand ArchiveThreadCommand { get; }

    public IAsyncRelayCommand UnarchiveThreadCommand { get; }

    public IAsyncRelayCommand RenameThreadCommand { get; }

    public IAsyncRelayCommand CompactThreadCommand { get; }

    public IAsyncRelayCommand RollbackThreadCommand { get; }

    public IAsyncRelayCommand SendTurnCommand { get; }

    public IAsyncRelayCommand SteerTurnCommand { get; }

    public IAsyncRelayCommand InterruptTurnCommand { get; }

    public IAsyncRelayCommand StartReviewCommand { get; }

    public bool HasPendingRequests => PendingRequests.Count > 0;

    public string PendingRequestCountLabel => PendingRequests.Count == 1 ? "1 pending request" : $"{PendingRequests.Count} pending requests";

    partial void OnDraftPromptChanged(string value)
    {
        SendTurnCommand.NotifyCanExecuteChanged();
        SteerTurnCommand.NotifyCanExecuteChanged();
        RenameThreadCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedConversationItemChanged(ConversationItemViewModel? value)
    {
    }

    private void SeedPhaseTwoRuntimeState()
    {
        DraftPrompt = string.Empty;
        SelectedApprovalPolicy = AvailableApprovalPolicies[0];
        SelectedModel = AvailableModels.FirstOrDefault() ?? "gpt-5.4";
        _activeTurnId = null;
        _currentThreadDetail = null;
        PendingRequests.Clear();
        NotifyPendingRequestsChanged();
    }

    private bool CanStartNewThread() => IsConnected && !IsBusy;

    private bool CanForkThread() => IsConnected && !IsBusy && SelectedThread is not null;

    private bool CanArchiveThread() => IsConnected
                                       && !IsBusy
                                       && SelectedThread is not null
                                       && !string.Equals(SelectedThread.StatusType, "archived", StringComparison.Ordinal)
                                       && !string.Equals(SelectedThread.StatusType, "closed", StringComparison.Ordinal);

    private bool CanUnarchiveThread() => IsConnected
                                         && !IsBusy
                                         && SelectedThread is not null
                                         && string.Equals(SelectedThread.StatusType, "archived", StringComparison.Ordinal);

    private bool CanRenameThread() => IsConnected && !IsBusy && SelectedThread is not null && !string.IsNullOrWhiteSpace(DraftPrompt);

    private bool CanCompactThread() => IsConnected && !IsBusy && SelectedThread is not null;

    private bool CanRollbackThread() => IsConnected && !IsBusy && SelectedThread is not null;

    private bool CanSendTurn() => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(DraftPrompt);

    private bool CanSteerTurn() => IsConnected
                                   && !IsBusy
                                   && SelectedThread is not null
                                   && !string.IsNullOrWhiteSpace(_activeTurnId)
                                   && !string.IsNullOrWhiteSpace(DraftPrompt);

    private bool CanInterruptTurn() => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(_activeTurnId) && SelectedThread is not null;

    private bool CanStartReview() => IsConnected && !IsBusy && SelectedThread is not null;

    private async Task StartNewThreadAsync()
    {
        if (_sessionService is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var result = await RequestAsync<ThreadStartResult>(
                "thread/start",
                new
                {
                    model = string.IsNullOrWhiteSpace(SelectedModel) ? null : SelectedModel,
                    cwd = string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory,
                    approvalPolicy = string.IsNullOrWhiteSpace(SelectedApprovalPolicy) ? null : SelectedApprovalPolicy,
                    sandbox = "workspace-write",
                    serviceName = "codex_gui"
                },
                "Unable to start a new thread.").ConfigureAwait(false);

            if (result?.Thread?.Id is null)
            {
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                UpsertThread(result.Thread);
                SelectedThread = RecentThreads.FirstOrDefault(thread => thread.Id == result.Thread.Id) ?? MapThread(result.Thread);
                ComposerHint = "New thread ready. Write a prompt below to start a turn.";
            });

            await LoadThreadDetailAsync(result.Thread.Id, true).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task ForkThreadAsync()
    {
        if (_sessionService is null || SelectedThread is null)
        {
            return;
        }

        var sourceThreadId = SelectedThread.Id;
        IsBusy = true;

        try
        {
            var response = await RequestAsync<JsonElement>(
                "thread/fork",
                new
                {
                    threadId = sourceThreadId,
                    persistExtendedHistory = true
                },
                "Unable to fork the selected thread.").ConfigureAwait(false);

            var forkedThread = TryGetThreadFromResponse(response, out var forkedThreadId);

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (forkedThread is not null)
                {
                    UpsertThread(forkedThread);
                }

                if (!string.IsNullOrWhiteSpace(forkedThreadId))
                {
                    SelectedThread = RecentThreads.FirstOrDefault(thread => thread.Id == forkedThreadId)
                        ?? (forkedThread is null ? SelectedThread : MapThread(forkedThread));
                }

                ComposerHint = string.IsNullOrWhiteSpace(forkedThreadId)
                    ? "Fork requested. Waiting for the app-server to surface the new thread."
                    : $"Forked thread ready ({ShortenIdentifier(forkedThreadId)}).";
            });

            await RefreshThreadListAsync().ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(forkedThreadId))
            {
                await LoadThreadDetailAsync(forkedThreadId, true).ConfigureAwait(false);
            }
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task ArchiveThreadAsync()
    {
        if (_sessionService is null || SelectedThread is null)
        {
            return;
        }

        var threadId = SelectedThread.Id;
        IsBusy = true;

        try
        {
            await RequestAsync<JsonElement>(
                "thread/archive",
                new { threadId },
                "Unable to archive the selected thread.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                UpdateThreadStatusInList(threadId, "archived");
                ComposerHint = "Thread archived. Unarchive it when you want to resume this conversation.";
            });

            await RefreshThreadListAsync().ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task UnarchiveThreadAsync()
    {
        if (_sessionService is null || SelectedThread is null)
        {
            return;
        }

        var threadId = SelectedThread.Id;
        IsBusy = true;

        try
        {
            await RequestAsync<JsonElement>(
                "thread/unarchive",
                new { threadId },
                "Unable to unarchive the selected thread.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                UpdateThreadStatusInList(threadId, "idle");
                ComposerHint = "Thread restored. You can continue with a new turn or review existing messages.";
            });

            await RefreshThreadListAsync().ConfigureAwait(false);
            await LoadThreadDetailAsync(threadId, true).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task RenameThreadAsync()
    {
        if (_sessionService is null || SelectedThread is null)
        {
            return;
        }

        var nextName = DraftPrompt.Trim();
        if (string.IsNullOrWhiteSpace(nextName))
        {
            return;
        }

        var threadId = SelectedThread.Id;
        IsBusy = true;

        try
        {
            await RequestAsync<JsonElement>(
                "thread/name/set",
                new
                {
                    threadId,
                    name = nextName
                },
                "Unable to rename the selected thread.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                var index = FindThreadIndex(threadId);
                if (index >= 0)
                {
                    var current = RecentThreads[index];
                    RecentThreads[index] = current with
                    {
                        Title = nextName,
                        TimeLabel = "just now"
                    };

                    SelectedThread = RecentThreads[index];
                }

                DraftPrompt = string.Empty;
                ComposerHint = $"Renamed thread to \"{nextName}\".";
            });

            await RefreshThreadListAsync().ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task CompactThreadAsync()
    {
        if (_sessionService is null || SelectedThread is null)
        {
            return;
        }

        var threadId = SelectedThread.Id;
        IsBusy = true;

        try
        {
            await RequestAsync<JsonElement>(
                "thread/compact/start",
                new { threadId },
                "Unable to start context compaction for the selected thread.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                ComposerHint = "Context compaction requested. Watch the conversation stream for compaction updates.";
            });

            await LoadThreadDetailAsync(threadId, true).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task RollbackThreadAsync()
    {
        if (_sessionService is null || SelectedThread is null)
        {
            return;
        }

        var threadId = SelectedThread.Id;
        IsBusy = true;

        try
        {
            await RequestAsync<JsonElement>(
                "thread/rollback",
                new
                {
                    threadId,
                    numTurns = 1
                },
                "Unable to rollback the latest turn.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                _activeTurnId = null;
                ComposerHint = "Rolled back the latest turn. Local workspace edits are unchanged.";
                NotifyCommandStates();
            });

            await RefreshThreadListAsync().ConfigureAwait(false);
            await LoadThreadDetailAsync(threadId, true).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task SendTurnAsync()
    {
        if (_sessionService is null)
        {
            return;
        }

        var prompt = DraftPrompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var threadId = await EnsureThreadReadyForConversationAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            var result = await RequestAsync<TurnStartResult>(
                "turn/start",
                new
                {
                    threadId,
                    input = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = prompt
                        }
                    },
                    model = string.IsNullOrWhiteSpace(SelectedModel) ? null : SelectedModel,
                    approvalPolicy = string.IsNullOrWhiteSpace(SelectedApprovalPolicy) ? null : SelectedApprovalPolicy,
                    summary = "concise",
                    sandboxPolicy = new
                    {
                        type = "workspaceWrite",
                        writableRoots = new[]
                        {
                            string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory
                        },
                        networkAccess = true
                    }
                },
                "Unable to start the turn.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                DraftPrompt = string.Empty;
                _activeTurnId = result?.Turn?.Id ?? _activeTurnId;
                ComposerHint = "Turn started. Watch the center stream and pending approvals for live progress.";
                NotifyCommandStates();
            });

            await LoadThreadDetailAsync(threadId, true).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task SteerTurnAsync()
    {
        if (_sessionService is null || SelectedThread is null || string.IsNullOrWhiteSpace(_activeTurnId))
        {
            return;
        }

        var steerPrompt = DraftPrompt.Trim();
        if (string.IsNullOrWhiteSpace(steerPrompt))
        {
            return;
        }

        var threadId = SelectedThread.Id;
        var expectedTurnId = _activeTurnId!;
        IsBusy = true;

        try
        {
            var result = await RequestAsync<JsonElement>(
                "turn/steer",
                new
                {
                    threadId,
                    expectedTurnId,
                    input = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = steerPrompt,
                            text_elements = Array.Empty<object>()
                        }
                    }
                },
                "Unable to steer the active turn.").ConfigureAwait(false);

            var returnedTurnId = result.ValueKind == JsonValueKind.Object
                ? TryGetString(result, "turnId")
                : null;

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(returnedTurnId))
                {
                    _activeTurnId = returnedTurnId;
                }

                DraftPrompt = string.Empty;
                ComposerHint = "Steering prompt sent to the active turn. Streaming updates will continue in place.";
                NotifyCommandStates();
            });

            await LoadThreadDetailAsync(threadId, true).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task InterruptTurnAsync()
    {
        if (_sessionService is null || SelectedThread is null || string.IsNullOrWhiteSpace(_activeTurnId))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await RequestAsync<TurnInterruptResult>(
                "turn/interrupt",
                new
                {
                    threadId = SelectedThread.Id,
                    turnId = _activeTurnId
                },
                "Unable to interrupt the current turn.").ConfigureAwait(false);

            await _uiDispatcher.InvokeAsync(() =>
            {
                ComposerHint = "Interrupt requested. Waiting for the turn to settle.";
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task StartReviewAsync()
    {
        if (_sessionService is null || SelectedThread is null)
        {
            return;
        }

        var threadId = SelectedThread.Id;
        IsBusy = true;

        try
        {
            var result = await RequestAsync<JsonElement>(
                "review/start",
                new
                {
                    threadId,
                    target = new
                    {
                        type = "uncommittedChanges"
                    },
                    delivery = "inline"
                },
                "Unable to start review for this thread.").ConfigureAwait(false);

            var reviewThreadId = result.ValueKind == JsonValueKind.Object
                ? TryGetString(result, "reviewThreadId")
                : null;
            var reviewTurnId = result.ValueKind == JsonValueKind.Object
                ? TryGetString(result, "turn", "id")
                : null;
            var targetThreadId = string.IsNullOrWhiteSpace(reviewThreadId) ? threadId : reviewThreadId!;

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(reviewTurnId))
                {
                    _activeTurnId = reviewTurnId;
                }

                ComposerHint = string.Equals(targetThreadId, threadId, StringComparison.Ordinal)
                    ? "Inline review started for uncommitted changes."
                    : $"Detached review started in thread {ShortenIdentifier(targetThreadId)}.";
                NotifyCommandStates();
            });

            await RefreshThreadListAsync().ConfigureAwait(false);
            await LoadThreadDetailAsync(targetThreadId, true).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task<string?> EnsureThreadReadyForConversationAsync()
    {
        if (_sessionService is null)
        {
            return null;
        }

        if (SelectedThread is null)
        {
            var created = await RequestAsync<ThreadStartResult>(
                "thread/start",
                new
                {
                    model = string.IsNullOrWhiteSpace(SelectedModel) ? null : SelectedModel,
                    cwd = string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory,
                    approvalPolicy = string.IsNullOrWhiteSpace(SelectedApprovalPolicy) ? null : SelectedApprovalPolicy,
                    sandbox = "workspace-write",
                    serviceName = "codex_gui"
                },
                "Unable to create a new thread for the turn.").ConfigureAwait(false);

            if (created?.Thread?.Id is null)
            {
                return null;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                UpsertThread(created.Thread);
                SelectedThread = RecentThreads.FirstOrDefault(thread => thread.Id == created.Thread.Id) ?? MapThread(created.Thread);
            });

            return created.Thread.Id;
        }

        var resumed = await RequestAsync<ThreadStartResult>(
            "thread/resume",
            new
            {
                threadId = SelectedThread.Id,
                model = string.IsNullOrWhiteSpace(SelectedModel) ? null : SelectedModel,
                cwd = string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory,
                approvalPolicy = string.IsNullOrWhiteSpace(SelectedApprovalPolicy) ? null : SelectedApprovalPolicy,
                sandbox = "workspace-write"
            },
            "Unable to resume the selected thread.").ConfigureAwait(false);

        if (resumed?.Thread?.Id is not null)
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                UpsertThread(resumed.Thread);
                SelectedThread = RecentThreads.FirstOrDefault(thread => thread.Id == resumed.Thread.Id) ?? MapThread(resumed.Thread);
            });
        }

        return resumed?.Thread?.Id ?? SelectedThread.Id;
    }

    private async Task<AppServerServerRequestCompletion> HandleServerRequestAsync(AppServerServerRequestMessage request, CancellationToken cancellationToken)
    {
        return await EnqueuePendingRequestAsync(
            request.RequestId.GetRawText(),
            request.Method,
            completeAction => _pendingInteractionFactory.Create(request, completeAction),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AppServerServerRequestCompletion> EnqueuePendingRequestAsync(
        string requestKey,
        string method,
        Func<Action<AppServerServerRequestCompletion>, PendingInteractionViewModel?> createPending,
        CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<AppServerServerRequestCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingServerRequestCompletions[requestKey] = completionSource;

        var pending = createPending(completion =>
        {
            if (completionSource.TrySetResult(completion))
            {
                _uiDispatcher.Post(() => RemovePendingRequest(requestKey));
            }
        });

        if (pending is null)
        {
            _pendingServerRequestCompletions.Remove(requestKey);
            return AppServerServerRequestCompletion.FromError(-32601, $"{method} is not supported in CodexGui yet.");
        }

        await _uiDispatcher.InvokeAsync(() =>
        {
            RemovePendingRequest(requestKey);
            PendingRequests.Insert(0, pending);
            NotifyPendingRequestsChanged();
        });

        using var registration = cancellationToken.Register(() =>
        {
            if (completionSource.TrySetCanceled(cancellationToken))
            {
                _uiDispatcher.Post(() => RemovePendingRequest(requestKey));
            }
        });

        try
        {
            return await completionSource.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingServerRequestCompletions.Remove(requestKey);
        }
    }

    private void HandlePhaseTwoNotification(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "account/login/completed":
            {
                ActiveAccountLoginId = null;
                ConnectionDetail = "Account login completed.";
                ComposerHint = "Authentication completed. You can continue sending turns.";
                _ = RefreshDataAsync(setBusyState: false, backgroundRefresh: true, includeThreadDetail: false);
                break;
            }
            case "thread/started":
            {
                var started = AppJson.Deserialize<ThreadStartedNotification>(parameters);
                if (started?.Thread is not null)
                {
                    UpsertThread(started.Thread);
                }

                var threadId = started?.Thread?.Id ?? TryGetString(parameters, "thread", "id");
                RequestSelectedThreadDetailRefresh(threadId);
                break;
            }
            case "thread/archived":
            case "thread/unarchived":
            case "thread/closed":
            {
                var threadId = ApplyThreadLifecycleState(method, parameters);
                RequestSelectedThreadDetailRefresh(threadId);
                break;
            }
            case "thread/status/changed":
            {
                var statusChanged = AppJson.Deserialize<ThreadStatusChangedNotification>(parameters);
                ApplyLiveThreadStatus(statusChanged);
                break;
            }
            case "thread/name/updated":
            {
                var threadNameUpdated = AppJson.Deserialize<ThreadNameUpdatedNotification>(parameters);
                ApplyLiveThreadName(threadNameUpdated);
                break;
            }
            case "thread/tokenUsage/updated":
            {
                var threadId = TryGetString(parameters, "threadId") ?? TryGetString(parameters, "thread", "id");
                RequestSelectedThreadDetailRefresh(threadId);
                break;
            }
            case "model/rerouted":
            {
                ApplyModelRerouted(parameters);
                break;
            }
            case "windows/worldWritableWarning":
            {
                ApplyWorldWritableWarning(parameters);
                break;
            }
            case "turn/started":
            {
                var started = AppJson.Deserialize<TurnStartedNotification>(parameters);
                _activeTurnId = started?.Turn?.Id ?? _activeTurnId;
                ComposerHint = "Turn running. Stream updates, approvals, and tool calls will appear live.";
                NotifyCommandStates();
                break;
            }
            case "turn/completed":
            {
                var completed = AppJson.Deserialize<TurnCompletedNotification>(parameters);
                _activeTurnId = null;
                ComposerHint = "Turn finished. You can send a follow-up or start another thread.";
                NotifyCommandStates();
                RequestSelectedThreadDetailRefresh(completed?.ThreadId);
                break;
            }
            case "item/started":
            case "item/updated":
            case "item/completed":
            {
                var itemLifecycle = AppJson.Deserialize<ItemLifecycleNotification>(parameters);
                ApplyLiveItemLifecycle(itemLifecycle);

                if (string.Equals(method, "item/completed", StringComparison.Ordinal))
                {
                    RequestSelectedThreadDetailRefresh(itemLifecycle?.ThreadId);
                }
                break;
            }
            case "item/agentMessage/textDelta":
            case "item/agentMessage/delta":
            {
                var agentMessageDelta = AppJson.Deserialize<AgentMessageDeltaNotification>(parameters);
                var itemId = ResolveLiveDeltaItemId(agentMessageDelta?.ItemId, agentMessageDelta?.TurnId, "agentMessage");
                AppendDeltaChunk(_latestAgentMessageDeltas, itemId, agentMessageDelta?.Delta);
                ApplyLiveAgentMessageDelta(agentMessageDelta, itemId);
                break;
            }
            case "item/plan/delta":
            {
                var planDelta = AppJson.Deserialize<PlanDeltaNotification>(parameters);
                var itemId = ResolveLiveDeltaItemId(planDelta?.ItemId, planDelta?.TurnId, "plan");
                AppendDeltaChunk(_latestPlanDeltas, itemId, planDelta?.Delta);
                ApplyLivePlanDelta(planDelta, itemId);
                break;
            }
            case "item/reasoning/summaryTextDelta":
            {
                var threadId = TryGetString(parameters, "threadId");
                var turnId = TryGetString(parameters, "turnId");
                var itemId = ResolveLiveDeltaItemId(TryGetString(parameters, "itemId"), turnId, "reasoning");
                AppendDeltaChunk(_latestReasoningSummaryDeltas, itemId, TryGetString(parameters, "delta"));
                ApplyLiveReasoningDelta(threadId, turnId, itemId);
                break;
            }
            case "item/reasoning/textDelta":
            {
                var threadId = TryGetString(parameters, "threadId");
                var turnId = TryGetString(parameters, "turnId");
                var itemId = ResolveLiveDeltaItemId(TryGetString(parameters, "itemId"), turnId, "reasoning");
                AppendDeltaChunk(_latestReasoningTextDeltas, itemId, TryGetString(parameters, "delta"));
                ApplyLiveReasoningDelta(threadId, turnId, itemId);
                break;
            }
            case "item/reasoning/summaryPartAdded":
            {
                var threadId = TryGetString(parameters, "threadId");
                var turnId = TryGetString(parameters, "turnId");
                var itemId = ResolveLiveDeltaItemId(TryGetString(parameters, "itemId"), turnId, "reasoning");
                var summaryPart = parameters.TryGetProperty("summaryPart", out var summaryPartElement)
                    ? AppJson.PrettyPrint(summaryPartElement)
                    : TryGetString(parameters, "delta");
                AppendMessageChunk(_latestReasoningSummaryParts, itemId, summaryPart);
                ApplyLiveReasoningDelta(threadId, turnId, itemId);
                break;
            }
            case "item/commandExecution/outputDelta":
            {
                var commandOutputDelta = AppJson.Deserialize<CommandExecutionOutputDeltaNotification>(parameters);
                var itemId = ResolveLiveDeltaItemId(commandOutputDelta?.ItemId, commandOutputDelta?.TurnId, "commandExecution");
                AppendDeltaChunk(_latestCommandOutputDeltas, itemId, commandOutputDelta?.Delta);
                ApplyLiveCommandOutputDelta(commandOutputDelta, itemId);
                break;
            }
            case "item/commandExecution/terminalInteraction":
            {
                var terminalInteraction = AppJson.Deserialize<TerminalInteractionNotification>(parameters);
                ApplyLiveTerminalInteraction(terminalInteraction);
                break;
            }
            case "item/mcpToolCall/progress":
            case "item/dynamicToolCall/progress":
            {
                var toolProgress = AppJson.Deserialize<McpToolCallProgressNotification>(parameters);
                var itemKind = string.Equals(method, "item/dynamicToolCall/progress", StringComparison.Ordinal)
                    ? "dynamicToolCall"
                    : "mcpToolCall";
                var itemId = ResolveLiveDeltaItemId(toolProgress?.ItemId, toolProgress?.TurnId, itemKind);
                AppendMessageChunk(_latestToolProgressMessages, itemId, toolProgress?.Message);
                ApplyLiveToolProgress(toolProgress, itemId, itemKind);
                break;
            }
            case "turn/diff/updated":
            {
                var diffUpdated = AppJson.Deserialize<TurnDiffUpdatedNotification>(parameters);
                if (!string.IsNullOrWhiteSpace(diffUpdated?.TurnId) && diffUpdated?.Diff is not null)
                {
                    _latestTurnDiffs[diffUpdated.TurnId] = diffUpdated.Diff;
                }

                ApplyLiveTurnDiff(diffUpdated);
                break;
            }
            case "item/fileChange/textDelta":
            {
                // textDelta streams prose/status updates; patch text arrives through outputDelta.
                break;
            }
            case "item/fileChange/outputDelta":
            {
                var fileChangeDelta = AppJson.Deserialize<FileChangeOutputDeltaNotification>(parameters);
                AppendDeltaChunk(_latestFileChangeDiffs, fileChangeDelta?.ItemId, fileChangeDelta?.Delta);
                AppendDeltaChunk(_latestTurnDiffs, fileChangeDelta?.TurnId, fileChangeDelta?.Delta);

                ApplyLiveTurnDiff(new TurnDiffUpdatedNotification
                {
                    ThreadId = fileChangeDelta?.ThreadId,
                    TurnId = fileChangeDelta?.TurnId,
                    Diff = !string.IsNullOrWhiteSpace(fileChangeDelta?.ItemId) && _latestFileChangeDiffs.TryGetValue(fileChangeDelta.ItemId, out var itemDiff)
                        ? itemDiff
                        : !string.IsNullOrWhiteSpace(fileChangeDelta?.TurnId) && _latestTurnDiffs.TryGetValue(fileChangeDelta.TurnId, out var turnDiff)
                            ? turnDiff
                            : fileChangeDelta?.Delta
                });
                break;
            }
            case "turn/plan/updated":
            {
                var planUpdated = AppJson.Deserialize<TurnPlanUpdatedNotification>(parameters);
                if (!string.IsNullOrWhiteSpace(planUpdated?.TurnId) && planUpdated is not null)
                {
                    _latestTurnPlans[planUpdated.TurnId] = planUpdated;
                }
                break;
            }
            case "serverRequest/resolved":
            {
                var resolved = AppJson.Deserialize<ServerRequestResolvedNotification>(parameters);
                var requestKey = resolved?.RequestId.GetRawText();

                if (!string.IsNullOrWhiteSpace(requestKey) && _pendingServerRequestCompletions.TryGetValue(requestKey, out var completionSource) && !completionSource.Task.IsCompleted)
                {
                    completionSource.TrySetResult(AppServerServerRequestCompletion.FromResult(new { }));
                }

                if (!string.IsNullOrWhiteSpace(requestKey))
                {
                    RemovePendingRequest(requestKey);
                }

                RequestSelectedThreadDetailRefresh(resolved?.ThreadId);
                break;
            }
        }
    }

    private void RequestSelectedThreadDetailRefresh(string? notificationThreadId)
    {
        if (_sessionService is null || !_sessionService.IsConnected)
        {
            return;
        }

        var selectedThreadId = ResolveSelectedThreadId();
        if (string.IsNullOrWhiteSpace(selectedThreadId))
        {
            return;
        }

        if (IsNotificationForSelectedThread(notificationThreadId))
        {
            _ = LoadThreadDetailAsync(selectedThreadId, true);
        }
    }

    private string? ResolveSelectedThreadId()
    {
        return SelectedThread?.Id ?? _selectedThreadId;
    }

    private string? ApplyThreadLifecycleState(string method, JsonElement parameters)
    {
        var threadId = TryGetString(parameters, "threadId") ?? TryGetString(parameters, "thread", "id");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var index = FindThreadIndex(threadId);
        if (index < 0)
        {
            return threadId;
        }

        var statusType = method switch
        {
            "thread/archived" => "archived",
            "thread/unarchived" => "idle",
            "thread/closed" => "closed",
            _ => RecentThreads[index].StatusType
        };

        UpdateThreadStatusInList(threadId, statusType);

        return threadId;
    }

    private void ApplyModelRerouted(JsonElement parameters)
    {
        var fromModel = TryGetString(parameters, "fromModel") ?? TryGetString(parameters, "from");
        var toModel = TryGetString(parameters, "toModel") ?? TryGetString(parameters, "to");
        if (string.IsNullOrWhiteSpace(fromModel) && string.IsNullOrWhiteSpace(toModel))
        {
            ConnectionDetail = "Model routing changed for this thread.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(toModel))
        {
            if (!AvailableModels.Contains(toModel, StringComparer.Ordinal))
            {
                AvailableModels.Add(toModel);
            }

            SelectedModel = toModel;
        }

        ConnectionDetail = string.IsNullOrWhiteSpace(fromModel)
            ? $"Model rerouted to {toModel}."
            : $"Model rerouted from {fromModel} to {toModel}.";
    }

    private void ApplyWorldWritableWarning(JsonElement parameters)
    {
        var path = TryGetString(parameters, "path") ?? WorkingDirectory;
        var message = TryGetString(parameters, "message") ?? "Workspace has world-writable permissions.";
        ConnectionDetail = string.IsNullOrWhiteSpace(path) ? message : $"{message} ({path})";
        ComposerHint = "Security warning received from app-server. Review workspace permissions before approving commands.";
    }

    private void ApplyLiveReasoningDelta(string? threadId, string? turnId, string itemId)
    {
        if (!IsNotificationForSelectedThread(threadId))
        {
            return;
        }

        var resolvedTurnId = string.IsNullOrWhiteSpace(turnId) ? _activeTurnId ?? "turn" : turnId;
        var summaryText = _latestReasoningSummaryDeltas.TryGetValue(itemId, out var aggregatedSummary)
            ? aggregatedSummary
            : string.Empty;
        if (_latestReasoningSummaryParts.TryGetValue(itemId, out var summaryParts))
        {
            summaryText = string.IsNullOrWhiteSpace(summaryText) ? summaryParts : $"{summaryText}\n{summaryParts}";
        }

        var contentText = _latestReasoningTextDeltas.TryGetValue(itemId, out var aggregatedReasoning)
            ? aggregatedReasoning
            : string.Empty;

        var body = !string.IsNullOrWhiteSpace(summaryText)
            ? PreviewMultiline(summaryText, 5, 280)
            : !string.IsNullOrWhiteSpace(contentText)
                ? PreviewMultiline(contentText, 5, 280)
                : "Reasoning in progress.";

        var card = CreateConversationCard(
            itemId: itemId,
            turnId: resolvedTurnId,
            kind: "reasoning",
            role: "assistant",
            title: "Reasoning",
            body: body,
            meta: $"{resolvedTurnId} · inProgress",
            badge: "streaming",
            codePreviewLabel: string.IsNullOrWhiteSpace(summaryText) ? null : "Summary",
            codePreview: string.IsNullOrWhiteSpace(summaryText) ? null : PreviewMultiline(summaryText, 8, 420),
            outputPreviewLabel: string.IsNullOrWhiteSpace(contentText) ? null : "Content",
            outputPreview: string.IsNullOrWhiteSpace(contentText) ? null : PreviewMultiline(contentText, 8, 420),
            documentKind: "markdown",
            documentName: $"turn-{ShortenIdentifier(resolvedTurnId)}-reasoning-live.md",
            documentMeta: "Streaming reasoning",
            documentText: BuildLiveReasoningDocument(resolvedTurnId, itemId, summaryText, contentText),
            accentBrush: ShellBrushes.Amber,
            surfaceBrush: ShellBrushes.PaperMuted);

        UpsertLiveConversationItem(card);
    }

    private bool IsNotificationForSelectedThread(string? notificationThreadId)
    {
        var selectedThreadId = ResolveSelectedThreadId();
        if (string.IsNullOrWhiteSpace(selectedThreadId))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(notificationThreadId)
            || string.Equals(notificationThreadId, selectedThreadId, StringComparison.Ordinal);
    }

    private void ApplyLiveItemLifecycle(ItemLifecycleNotification? itemLifecycle)
    {
        if (itemLifecycle?.Item is null || !IsNotificationForSelectedThread(itemLifecycle.ThreadId))
        {
            return;
        }

        var turnId = itemLifecycle.TurnId ?? _activeTurnId ?? "turn";
        var syntheticTurn = new ThreadTurn
        {
            Id = turnId,
            Status = itemLifecycle.Item.Status ?? "inProgress",
            Items = new[] { itemLifecycle.Item }
        };

        var liveItem = CreateConversationItem(syntheticTurn, itemLifecycle.Item, turnId, 0);
        UpsertLiveConversationItem(liveItem);
    }

    private void ApplyLiveAgentMessageDelta(AgentMessageDeltaNotification? deltaNotification, string itemId)
    {
        if (deltaNotification is null || !IsNotificationForSelectedThread(deltaNotification.ThreadId))
        {
            return;
        }

        var turnId = deltaNotification.TurnId ?? _activeTurnId ?? "turn";
        var text = _latestAgentMessageDeltas.TryGetValue(itemId, out var aggregatedText)
            ? aggregatedText
            : deltaNotification.Delta;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var syntheticTurn = new ThreadTurn
        {
            Id = turnId,
            Status = "inProgress",
            Items = Array.Empty<ThreadItem>()
        };

        var syntheticItem = new ThreadItem
        {
            Id = itemId,
            Type = "agentMessage",
            Text = text,
            Status = "inProgress"
        };

        var liveItem = CreateConversationItem(syntheticTurn, syntheticItem, turnId, 0);
        UpsertLiveConversationItem(liveItem);
    }

    private void ApplyLivePlanDelta(PlanDeltaNotification? deltaNotification, string itemId)
    {
        if (deltaNotification is null || !IsNotificationForSelectedThread(deltaNotification.ThreadId))
        {
            return;
        }

        var turnId = deltaNotification.TurnId ?? _activeTurnId ?? "turn";
        var text = _latestPlanDeltas.TryGetValue(itemId, out var aggregatedText)
            ? aggregatedText
            : deltaNotification.Delta;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var syntheticTurn = new ThreadTurn
        {
            Id = turnId,
            Status = "inProgress",
            Items = Array.Empty<ThreadItem>()
        };

        var syntheticItem = new ThreadItem
        {
            Id = itemId,
            Type = "plan",
            Text = text,
            Status = "inProgress"
        };

        var liveItem = CreateConversationItem(syntheticTurn, syntheticItem, turnId, 0);
        UpsertLiveConversationItem(liveItem);
    }

    private void ApplyLiveCommandOutputDelta(CommandExecutionOutputDeltaNotification? deltaNotification, string itemId)
    {
        if (deltaNotification is null || !IsNotificationForSelectedThread(deltaNotification.ThreadId))
        {
            return;
        }

        var turnId = deltaNotification.TurnId ?? _activeTurnId ?? "turn";
        var output = _latestCommandOutputDeltas.TryGetValue(itemId, out var aggregatedOutput)
            ? aggregatedOutput
            : deltaNotification.Delta;

        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var existingIndex = FindConversationItemIndexById(itemId);
        if (existingIndex >= 0 && string.Equals(ConversationItems[existingIndex].Kind, "commandExecution", StringComparison.Ordinal))
        {
            var existing = ConversationItems[existingIndex];
            var commandText = !string.IsNullOrWhiteSpace(existing.CodePreview) ? existing.CodePreview : "Command execution";
            var updated = existing with
            {
                Meta = $"{turnId} · inProgress",
                Badge = "running",
                OutputPreviewLabel = "Output",
                OutputPreview = PreviewMultiline(output, 8, 420),
                DocumentKind = "command",
                DocumentMeta = "Streaming output",
                DocumentText = BuildLiveCommandDocument(turnId, itemId, commandText, output)
            };

            ConversationItems[existingIndex] = updated;

            if (SelectedConversationItem is not null && string.Equals(SelectedConversationItem.ItemId, updated.ItemId, StringComparison.Ordinal))
            {
                SelectedConversationItem = updated;
            }

            NotifyCollectionStateChanged();
            return;
        }

        var syntheticTurn = new ThreadTurn
        {
            Id = turnId,
            Status = "inProgress",
            Items = Array.Empty<ThreadItem>()
        };

        var syntheticItem = new ThreadItem
        {
            Id = itemId,
            Type = "commandExecution",
            Status = "inProgress",
            AggregatedOutput = output
        };

        var liveItem = CreateConversationItem(syntheticTurn, syntheticItem, turnId, 0);
        UpsertLiveConversationItem(liveItem);
    }

    private void ApplyLiveTerminalInteraction(TerminalInteractionNotification? interactionNotification)
    {
        if (interactionNotification is null || !IsNotificationForSelectedThread(interactionNotification.ThreadId))
        {
            return;
        }

        var itemId = ResolveLiveDeltaItemId(interactionNotification.ItemId, interactionNotification.TurnId, "commandExecution");
        var interactionText = string.IsNullOrWhiteSpace(interactionNotification.Stdin)
            ? "<empty stdin>"
            : interactionNotification.Stdin!.TrimEnd();

        AppendMessageChunk(_latestCommandOutputDeltas, itemId, $"[stdin] {interactionText}");
        ApplyLiveCommandOutputDelta(new CommandExecutionOutputDeltaNotification
        {
            ThreadId = interactionNotification.ThreadId,
            TurnId = interactionNotification.TurnId,
            ItemId = itemId,
            Delta = $"[stdin] {interactionText}"
        }, itemId);

    }

    private void ApplyLiveToolProgress(McpToolCallProgressNotification? progressNotification, string itemId, string itemKind)
    {
        if (progressNotification is null || !IsNotificationForSelectedThread(progressNotification.ThreadId))
        {
            return;
        }

        var turnId = progressNotification.TurnId ?? _activeTurnId ?? "turn";
        var progressText = _latestToolProgressMessages.TryGetValue(itemId, out var aggregatedProgress)
            ? aggregatedProgress
            : progressNotification.Message;

        if (string.IsNullOrWhiteSpace(progressText))
        {
            return;
        }

        var index = FindConversationItemIndexById(itemId);
        if (index >= 0 && ConversationItems[index].Kind is "mcpToolCall" or "dynamicToolCall")
        {
            var existing = ConversationItems[index];
            var updated = existing with
            {
                Meta = $"{turnId} · inProgress",
                Badge = "running",
                OutputPreviewLabel = "Progress",
                OutputPreview = PreviewMultiline(progressText, 12, 420),
                DocumentKind = "tool",
                DocumentMeta = "Streaming progress",
                DocumentText = BuildLiveToolProgressDocument(turnId, itemId, itemKind, progressText)
            };

            ConversationItems[index] = updated;

            if (SelectedConversationItem is not null && string.Equals(SelectedConversationItem.ItemId, updated.ItemId, StringComparison.Ordinal))
            {
                SelectedConversationItem = updated;
            }

            NotifyCollectionStateChanged();
            return;
        }

        var title = string.Equals(itemKind, "dynamicToolCall", StringComparison.Ordinal)
            ? "Dynamic tool"
            : "Connector tool";

        var liveItem = CreateConversationCard(
            itemId: itemId,
            turnId: turnId,
            kind: itemKind,
            role: "system",
            title: title,
            body: progressNotification.Message ?? "Tool call in progress.",
            meta: $"{turnId} · inProgress",
            badge: "running",
            codePreviewLabel: null,
            codePreview: null,
            outputPreviewLabel: "Progress",
            outputPreview: PreviewMultiline(progressText, 12, 420),
            documentKind: "tool",
            documentName: string.Equals(itemKind, "dynamicToolCall", StringComparison.Ordinal)
                ? $"turn-{ShortenIdentifier(turnId)}-dynamic-tool-progress.log"
                : $"turn-{ShortenIdentifier(turnId)}-tool-progress.log",
            documentMeta: "Streaming progress",
            documentText: BuildLiveToolProgressDocument(turnId, itemId, itemKind, progressText),
            accentBrush: string.Equals(itemKind, "dynamicToolCall", StringComparison.Ordinal) ? ShellBrushes.Blue : ShellBrushes.Amber,
            surfaceBrush: ShellBrushes.ToolSurface);

        UpsertLiveConversationItem(liveItem);
    }

    private void UpsertLiveConversationItem(ConversationItemViewModel liveItem)
    {
        var index = FindConversationItemIndexById(liveItem.ItemId);

        if (index >= 0)
        {
            ConversationItems[index] = liveItem;
        }
        else
        {
            ConversationItems.Add(liveItem);
        }

        if (SelectedConversationItem is null)
        {
            SelectedConversationItem = liveItem;
        }
        else if (string.Equals(SelectedConversationItem.ItemId, liveItem.ItemId, StringComparison.Ordinal))
        {
            SelectedConversationItem = liveItem;
        }

        NotifyCollectionStateChanged();
    }

    private void ApplyLiveTurnDiff(TurnDiffUpdatedNotification? diffUpdated)
    {
        if (diffUpdated is null || string.IsNullOrWhiteSpace(diffUpdated.TurnId))
        {
            return;
        }

        if (!IsNotificationForSelectedThread(diffUpdated.ThreadId))
        {
            return;
        }

        var diff = diffUpdated.Diff;
        if (string.IsNullOrWhiteSpace(diff) && !_latestTurnDiffs.TryGetValue(diffUpdated.TurnId, out diff))
        {
            return;
        }

        var turnId = diffUpdated.TurnId;
        var itemId = $"{turnId}:diff";
        var card = CreateConversationCard(
            itemId: itemId,
            turnId: turnId,
            kind: "turnDiff",
            role: "system",
            title: "Aggregated diff",
            body: "Latest unified diff for the turn.",
            meta: $"{turnId} · inProgress",
            badge: "diff",
            codePreviewLabel: "Unified diff",
            codePreview: PreviewMultiline(diff, 8, 320),
            outputPreviewLabel: null,
            outputPreview: null,
            documentKind: "diff",
            documentName: $"turn-{ShortenIdentifier(turnId)}-diff.patch",
            documentMeta: $"{diff.Split('\n').Length} lines",
            documentText: $"# Aggregated Diff\n\n{diff}",
            accentBrush: ShellBrushes.Amber,
            surfaceBrush: ShellBrushes.AmberSoft);

        var index = FindConversationItemIndexById(itemId);
        if (index >= 0)
        {
            ConversationItems[index] = card;
        }
        else
        {
            ConversationItems.Add(card);
        }

        if (SelectedConversationItem is not null && string.Equals(SelectedConversationItem.ItemId, itemId, StringComparison.Ordinal))
        {
            SelectedConversationItem = card;
        }
    }

    private int FindConversationItemIndexById(string itemId)
    {
        for (var index = 0; index < ConversationItems.Count; index++)
        {
            if (string.Equals(ConversationItems[index].ItemId, itemId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string ResolveLiveDeltaItemId(string? itemId, string? turnId, string itemKind)
    {
        if (!string.IsNullOrWhiteSpace(itemId))
        {
            return itemId;
        }

        if (!string.IsNullOrWhiteSpace(turnId))
        {
            return $"{turnId}:{itemKind}:delta";
        }

        return $"{itemKind}:delta";
    }

    private static string BuildLiveCommandDocument(string turnId, string itemId, string command, string output)
    {
        return string.Join('\n',
        [
            "# Command Execution",
            string.Empty,
            $"- turn: {turnId}",
            $"- item: {itemId}",
            "- status: inProgress",
            string.Empty,
            "## Command",
            "$ " + (string.IsNullOrWhiteSpace(command) ? "Command execution" : command),
            string.Empty,
            "## Output",
            output
        ]);
    }

    private static string BuildLiveToolProgressDocument(string turnId, string itemId, string itemKind, string progress)
    {
        return string.Join('\n',
        [
            "# Tool Progress",
            string.Empty,
            $"- turn: {turnId}",
            $"- item: {itemId}",
            $"- kind: {itemKind}",
            "- status: inProgress",
            string.Empty,
            "## Progress",
            progress
        ]);
    }

    private static string BuildLiveReasoningDocument(string turnId, string itemId, string summaryText, string contentText)
    {
        return string.Join('\n',
        [
            "# Reasoning",
            string.Empty,
            $"- turn: {turnId}",
            $"- item: {itemId}",
            "- status: inProgress",
            string.Empty,
            "## Summary",
            string.IsNullOrWhiteSpace(summaryText) ? "No summary captured yet." : summaryText,
            string.Empty,
            "## Content",
            string.IsNullOrWhiteSpace(contentText) ? "No content captured yet." : contentText
        ]);
    }

    private void UpdateAvailableModels(ModelListResult? models)
    {
        var modelIds = (models?.Data ?? Array.Empty<ModelInfo>())
            .Select(static model => model.Model ?? model.Id)
            .Where(static model => !string.IsNullOrWhiteSpace(model))
            .Select(static model => model!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (modelIds.Count == 0)
        {
            modelIds.Add("gpt-5.4");
        }

        ReplaceCollection(AvailableModels, modelIds);
        if (!modelIds.Contains(SelectedModel, StringComparer.Ordinal))
        {
            SelectedModel = models?.Data?.FirstOrDefault(static model => model.IsDefault)?.Model
                ?? modelIds[0];
        }
    }

    private void NotifyPendingRequestsChanged()
    {
        OnPropertyChanged(nameof(HasPendingRequests));
        OnPropertyChanged(nameof(PendingRequestCountLabel));
    }

    private void RemovePendingRequest(string requestKey)
    {
        var existing = PendingRequests.FirstOrDefault(request => request.RequestKey == requestKey);
        if (existing is null)
        {
            return;
        }

        PendingRequests.Remove(existing);
        NotifyPendingRequestsChanged();
    }

    private void UpsertThread(ThreadSummary thread)
    {
        var mapped = MapThread(thread);
        var index = -1;

        for (var i = 0; i < RecentThreads.Count; i++)
        {
            if (string.Equals(RecentThreads[i].Id, mapped.Id, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            RecentThreads[index] = mapped;
        }
        else
        {
            RecentThreads.Insert(0, mapped);
        }

        OnPropertyChanged(nameof(ThreadCountLabel));
    }

    private void ApplyLiveThreadStatus(ThreadStatusChangedNotification? statusChanged)
    {
        var threadId = statusChanged?.ThreadId;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        var index = FindThreadIndex(threadId);
        if (index < 0)
        {
            return;
        }

        var existing = RecentThreads[index];
        var statusType = statusChanged?.Status?.Type;
        if (string.IsNullOrWhiteSpace(statusType))
        {
            statusType = existing.StatusType;
        }

        statusType ??= "notLoaded";
        var accent = statusType switch
        {
            "active" => ShellBrushes.Green,
            "idle" => ShellBrushes.Blue,
            "systemError" => ShellBrushes.Red,
            _ => ShellBrushes.Neutral
        };

        var badge = string.Equals(existing.Badge, "ephemeral", StringComparison.Ordinal)
            ? existing.Badge
            : statusType;

        RecentThreads[index] = existing with
        {
            StatusType = statusType,
            Badge = string.IsNullOrWhiteSpace(badge) ? existing.Badge : badge,
            AccentBrush = accent,
            TimeLabel = "just now"
        };
    }

    private void ApplyLiveThreadName(ThreadNameUpdatedNotification? threadNameUpdated)
    {
        var threadId = threadNameUpdated?.ThreadId;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return;
        }

        var index = FindThreadIndex(threadId);
        if (index < 0)
        {
            return;
        }

        var existing = RecentThreads[index];
        var nextTitle = string.IsNullOrWhiteSpace(threadNameUpdated?.ThreadName)
            ? existing.Title
            : threadNameUpdated.ThreadName!;

        RecentThreads[index] = existing with
        {
            Title = nextTitle,
            TimeLabel = "just now"
        };
    }

    private void UpdateThreadStatusInList(string threadId, string statusType)
    {
        var index = FindThreadIndex(threadId);
        if (index < 0)
        {
            return;
        }

        var existing = RecentThreads[index];
        var accent = statusType switch
        {
            "archived" => ShellBrushes.Neutral,
            "closed" => ShellBrushes.Red,
            "active" => ShellBrushes.Green,
            "idle" => ShellBrushes.Blue,
            _ => ShellBrushes.Neutral
        };

        var badge = string.Equals(existing.Badge, "ephemeral", StringComparison.Ordinal)
            ? existing.Badge
            : statusType;

        var updated = existing with
        {
            StatusType = statusType,
            Badge = string.IsNullOrWhiteSpace(badge) ? existing.Badge : badge,
            AccentBrush = accent,
            TimeLabel = "just now"
        };

        RecentThreads[index] = updated;

        if (string.Equals(SelectedThread?.Id, threadId, StringComparison.Ordinal))
        {
            SelectedThread = updated;
        }
    }

    private int FindThreadIndex(string threadId)
    {
        for (var index = 0; index < RecentThreads.Count; index++)
        {
            if (string.Equals(RecentThreads[index].Id, threadId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private List<ConversationItemViewModel> BuildConversationItems(ThreadDetail thread)
    {
        var items = new List<ConversationItemViewModel>();

        foreach (var turn in thread.Turns ?? Array.Empty<ThreadTurn>())
        {
            var turnId = turn.Id ?? Guid.NewGuid().ToString("N");
            var index = 0;

            foreach (var item in turn.Items ?? Array.Empty<ThreadItem>())
            {
                items.Add(CreateConversationItem(turn, item, turnId, index));
                index++;
            }

            if (_latestTurnDiffs.TryGetValue(turnId, out var diff) && items.All(item => item.TurnId != turnId || item.Kind != "fileChange"))
            {
                items.Add(CreateConversationCard(
                    itemId: $"{turnId}:diff",
                    turnId: turnId,
                    kind: "turnDiff",
                    role: "system",
                    title: "Aggregated diff",
                    body: "Latest unified diff for the turn.",
                    meta: $"{turnId} · {turn.Status ?? "inProgress"}",
                    badge: "diff",
                    codePreviewLabel: "Unified diff",
                    codePreview: PreviewMultiline(diff, 8, 320),
                    outputPreviewLabel: null,
                    outputPreview: null,
                    documentKind: "diff",
                    documentName: $"turn-{ShortenIdentifier(turnId)}-diff.patch",
                    documentMeta: $"{diff.Split('\n').Length} lines",
                    documentText: $"# Aggregated Diff\n\n{diff}",
                    accentBrush: ShellBrushes.Amber,
                    surfaceBrush: ShellBrushes.AmberSoft));
            }

            if (!string.IsNullOrWhiteSpace(turn.Error?.Message))
            {
                items.Add(CreateConversationCard(
                    itemId: $"{turnId}:error",
                    turnId: turnId,
                    kind: "turnError",
                    role: "system",
                    title: "Turn error",
                    body: turn.Error.Message,
                    meta: turnId,
                    badge: "failed",
                    codePreviewLabel: null,
                    codePreview: null,
                    outputPreviewLabel: null,
                    outputPreview: null,
                    documentKind: "markdown",
                    documentName: $"turn-{ShortenIdentifier(turnId)}-error.md",
                    documentMeta: "Turn failure",
                    documentText: $"# Turn Error\n\n{turn.Error.Message}\n\n{turn.Error.AdditionalDetails}",
                    accentBrush: ShellBrushes.Red,
                    surfaceBrush: ShellBrushes.RedSoft));
            }
        }

        if (items.Count == 0)
        {
            items.Add(CreateConversationCard(
                itemId: "empty",
                turnId: "empty",
                kind: "placeholder",
                role: "assistant",
                title: "Codex",
                body: "This thread has no persisted turns yet.",
                meta: thread.Id ?? "thread",
                badge: string.Empty,
                codePreviewLabel: null,
                codePreview: null,
                outputPreviewLabel: null,
                outputPreview: null,
                documentKind: "markdown",
                documentName: $"thread-{ShortenIdentifier(thread.Id)}-summary.md",
                documentMeta: "No turns",
                documentText: BuildThreadDocument(thread),
                accentBrush: ShellBrushes.Green,
                surfaceBrush: ShellBrushes.Paper));
        }

        return items;
    }

    private ConversationItemViewModel CreateConversationItem(ThreadTurn turn, ThreadItem item, string turnId, int index)
    {
        var itemId = item.Id ?? $"{turnId}:{item.Type ?? "item"}:{index}";
        var meta = $"{turnId} · {item.Status ?? turn.Status ?? "available"}";

        return item.Type switch
        {
            "userMessage" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "userMessage",
                "user",
                "You",
                ExtractUserMessageText(item) ?? "User message",
                meta,
                string.Empty,
                null,
                null,
                null,
                null,
                "markdown",
                $"turn-{ShortenIdentifier(turnId)}-user.md",
                "User input",
                $"# User Message\n\n{ExtractUserMessageText(item) ?? "User message"}",
                ShellBrushes.Blue,
                ShellBrushes.BlueSoft),
            "agentMessage" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "agentMessage",
                "assistant",
                "Codex",
                item.Text ?? TryGetString(item.AdditionalPropertiesJson ?? new Dictionary<string, JsonElement>(), "text") ?? "Assistant message",
                meta,
                item.Phase ?? string.Empty,
                null,
                null,
                null,
                null,
                "markdown",
                $"turn-{ShortenIdentifier(turnId)}-assistant.md",
                item.Phase ?? "Assistant reply",
                $"# Assistant Message\n\n{item.Text ?? TryGetString(item.AdditionalPropertiesJson ?? new Dictionary<string, JsonElement>(), "text") ?? "Assistant message"}",
                ShellBrushes.Green,
                ShellBrushes.Paper),
            "commandExecution" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "commandExecution",
                "system",
                "Command execution",
                BuildCommandBody(item),
                meta,
                item.ExitCode is null ? item.Status ?? string.Empty : $"exit {item.ExitCode}",
                "Command",
                item.Command ?? BuildCommandSummary(item.AdditionalPropertiesJson),
                string.IsNullOrWhiteSpace(item.AggregatedOutput) ? null : "Output",
                string.IsNullOrWhiteSpace(item.AggregatedOutput) ? null : PreviewMultiline(item.AggregatedOutput, 8, 420),
                "command",
                $"turn-{ShortenIdentifier(turnId)}-command.log",
                BuildCommandDocumentMeta(item),
                BuildCommandDocument(turn, item),
                ShellBrushes.Blue,
                ShellBrushes.CommandSurface),
            "fileChange" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "fileChange",
                "system",
                "File change",
                BuildFileChangeBody(item),
                meta,
                item.Changes is { Count: > 0 } changes ? $"{changes.Count} file(s)" : item.Status ?? string.Empty,
                "Changed files",
                BuildChangedFilesPreview(item),
                "Diff preview",
                PreviewMultiline(BuildAggregatedDiff(turnId, item), 8, 420),
                "diff",
                $"turn-{ShortenIdentifier(turnId)}-diff.patch",
                item.Changes is { Count: > 0 } diffChanges ? $"{diffChanges.Count} file(s) changed" : "Diff",
                BuildFileChangeDocument(turn, item, turnId),
                ShellBrushes.Amber,
                ShellBrushes.AmberSoft,
                BuildFileDiffEntries(turnId, item)),
            "mcpToolCall" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "mcpToolCall",
                "system",
                "Connector tool",
                BuildToolBody(item),
                meta,
                item.Tool ?? item.Status ?? string.Empty,
                "Arguments",
                AppJson.PrettyPrint(item.Arguments),
                "Result",
                BuildToolResultPreview(item),
                "tool",
                $"turn-{ShortenIdentifier(turnId)}-tool.json",
                $"{item.Server ?? "connector"}/{item.Tool ?? "tool"}",
                BuildToolDocument(turn, item),
                ShellBrushes.Amber,
                ShellBrushes.ToolSurface),
            "dynamicToolCall" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "dynamicToolCall",
                "system",
                "Dynamic tool",
                BuildToolBody(item),
                meta,
                item.Success is null ? item.Status ?? string.Empty : item.Success.Value ? "success" : "failed",
                "Arguments",
                AppJson.PrettyPrint(item.Arguments),
                "Returned content",
                BuildDynamicToolContentPreview(item),
                "tool",
                $"turn-{ShortenIdentifier(turnId)}-dynamic-tool.json",
                item.Tool ?? "dynamic tool",
                BuildToolDocument(turn, item),
                ShellBrushes.Blue,
                ShellBrushes.ToolSurface),
            "plan" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "plan",
                "assistant",
                "Plan",
                item.Text ?? DescribeThreadItem(item),
                meta,
                string.Empty,
                null,
                null,
                null,
                null,
                "markdown",
                $"turn-{ShortenIdentifier(turnId)}-plan.md",
                "Plan",
                BuildPlanDocument(turnId, item),
                ShellBrushes.Amber,
                ShellBrushes.AmberSoft),
            "reasoning" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "reasoning",
                "assistant",
                "Reasoning",
                BuildReasoningBody(item),
                meta,
                string.Empty,
                "Summary",
                AppJson.PrettyPrint(item.SummaryJson),
                "Content",
                PreviewMultiline(AppJson.PrettyPrint(item.ContentJson), 8, 420),
                "markdown",
                $"turn-{ShortenIdentifier(turnId)}-reasoning.md",
                "Reasoning",
                BuildReasoningDocument(turnId, item),
                ShellBrushes.Amber,
                ShellBrushes.PaperMuted),
            "enteredReviewMode" or "exitedReviewMode" => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "review",
                "assistant",
                item.Type == "enteredReviewMode" ? "Entered review mode" : "Exited review mode",
                item.Text ?? DescribeThreadItem(item),
                meta,
                string.Empty,
                null,
                null,
                null,
                null,
                "markdown",
                $"turn-{ShortenIdentifier(turnId)}-review.md",
                "Review",
                $"# Review Mode\n\n{item.Text ?? DescribeThreadItem(item)}",
                ShellBrushes.Green,
                ShellBrushes.Paper),
            _ => CreateConversationCard(
                itemId,
                turnId,
                item.Type ?? "item",
                "system",
                HumanizeItemType(item.Type),
                DescribeThreadItem(item),
                meta,
                item.Status ?? string.Empty,
                null,
                null,
                null,
                null,
                "markdown",
                $"turn-{ShortenIdentifier(turnId)}-{HumanizeItemType(item.Type).Replace(' ', '-').ToLowerInvariant()}.md",
                HumanizeItemType(item.Type),
                $"# {HumanizeItemType(item.Type)}\n\n{DescribeThreadItem(item)}",
                AccentForItem(item.Type),
                SurfaceForItem(item.Type))
        };
    }

    private static ConversationItemViewModel CreateConversationCard(
        string itemId,
        string turnId,
        string kind,
        string role,
        string title,
        string body,
        string meta,
        string badge,
        string? codePreviewLabel,
        string? codePreview,
        string? outputPreviewLabel,
        string? outputPreview,
        string documentKind,
        string documentName,
        string documentMeta,
        string documentText,
        IBrush accentBrush,
        IBrush surfaceBrush,
        IReadOnlyList<DiffFileEntryViewModel>? fileDiffs = null)
    {
        return new ConversationItemViewModel(
            itemId,
            turnId,
            kind,
            role,
            title,
            body,
            meta,
            badge,
            codePreviewLabel,
            codePreview,
            outputPreviewLabel,
            outputPreview,
            documentKind,
            documentName,
            documentMeta,
            documentText,
            accentBrush,
            surfaceBrush,
            ShellBrushes.TextPrimary,
            fileDiffs);
    }

    private static string BuildCommandBody(ThreadItem item)
    {
        var duration = item.DurationMs is null ? string.Empty : $" in {item.DurationMs} ms";
        var status = item.ExitCode is null ? item.Status ?? "pending" : $"exit {item.ExitCode}";
        return $"{item.Command ?? "Command execution"} · {status}{duration}";
    }

    private static string BuildCommandDocumentMeta(ThreadItem item)
    {
        var parts = new List<string>();
        if (item.ExitCode is not null)
        {
            parts.Add($"exit {item.ExitCode}");
        }

        if (item.DurationMs is not null)
        {
            parts.Add($"{item.DurationMs} ms");
        }

        if (!string.IsNullOrWhiteSpace(item.Status))
        {
            parts.Add(item.Status);
        }

        return parts.Count == 0 ? "Command" : string.Join(" · ", parts);
    }

    private static string BuildCommandDocument(ThreadTurn turn, ThreadItem item)
    {
        return string.Join('\n',
        [
            "# Command Execution",
            string.Empty,
            $"- turn: {turn.Id ?? "turn"}",
            $"- item: {item.Id ?? "item"}",
            $"- status: {item.Status ?? "unknown"}",
            $"- cwd: {item.Cwd ?? "not reported"}",
            $"- exitCode: {(item.ExitCode is null ? "n/a" : item.ExitCode)}",
            $"- durationMs: {(item.DurationMs is null ? "n/a" : item.DurationMs)}",
            string.Empty,
            "## Command",
            "$ " + (item.Command ?? BuildCommandSummary(item.AdditionalPropertiesJson)),
            string.Empty,
            "## Output",
            string.IsNullOrWhiteSpace(item.AggregatedOutput) ? "No output captured yet." : item.AggregatedOutput
        ]);
    }

    private static string BuildFileChangeBody(ThreadItem item)
    {
        var changeCount = item.Changes?.Count ?? 0;
        return changeCount == 0
            ? "Codex proposed file edits."
            : $"Codex proposed {changeCount} file change(s).";
    }

    private static string BuildChangedFilesPreview(ThreadItem item)
    {
        if (item.Changes is not { Count: > 0 })
        {
            return string.Empty;
        }

        return string.Join('\n', item.Changes.Take(4).Select(static change => $"{change.Kind ?? "change"} {change.Path ?? "file"}"));
    }

    private IReadOnlyList<DiffFileEntryViewModel> BuildFileDiffEntries(string turnId, ThreadItem item)
    {
        if (item.Changes is not { Count: > 0 } changes)
        {
            return Array.Empty<DiffFileEntryViewModel>();
        }

        var localGitDiff = BuildLocalGitDiff(item);
        var fallbackAggregatedDiff = BuildAggregatedDiff(turnId, item, localGitDiff);
        var itemDiff = !string.IsNullOrWhiteSpace(item.Id) && _latestFileChangeDiffs.TryGetValue(item.Id, out var latestItemDiff)
            ? latestItemDiff
            : string.Empty;
        var turnDiff = _latestTurnDiffs.TryGetValue(turnId, out var latestTurnDiff)
            ? latestTurnDiff
            : string.Empty;
        var entries = new List<DiffFileEntryViewModel>(changes.Count);

        foreach (var change in changes)
        {
            var path = string.IsNullOrWhiteSpace(change.Path) ? "file" : change.Path;
            var diff = ResolveFileDiffFromApi(path!, change.Diff, itemDiff, turnDiff, fallbackAggregatedDiff, localGitDiff, changes.Count);

            if (string.IsNullOrWhiteSpace(diff))
            {
                diff = "Waiting for app-server diff payload.";
            }

            entries.Add(new DiffFileEntryViewModel(
                path!,
                change.Kind ?? "change",
                BuildDiffSummary(diff),
                diff));
        }

        return entries;
    }

    private static string BuildDiffSummary(string diff)
    {
        if (string.IsNullOrWhiteSpace(diff)
            || string.Equals(diff, "No diff captured yet.", StringComparison.Ordinal)
            || string.Equals(diff, "Waiting for app-server diff payload.", StringComparison.Ordinal))
        {
            return "No patch text";
        }

        var additions = 0;
        var deletions = 0;
        var lines = diff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (var line in lines)
        {
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                additions++;
            }
            else if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
            {
                deletions++;
            }
        }

        return $"+{additions}  -{deletions}";
    }

    private string BuildAggregatedDiff(string turnId, ThreadItem item, string? localGitDiff = null)
    {
        if (!string.IsNullOrWhiteSpace(item.Id) &&
            _latestFileChangeDiffs.TryGetValue(item.Id, out var itemDiff) &&
            LooksLikeUnifiedDiff(itemDiff))
        {
            return itemDiff;
        }

        if (_latestTurnDiffs.TryGetValue(turnId, out var diff) && LooksLikeUnifiedDiff(diff))
        {
            return diff;
        }

        var itemDiffs = string.Join(
            "\n\n",
            (item.Changes ?? Array.Empty<FileUpdateChange>())
                .Select(static change => change.Diff)
                .Where(LooksLikeUnifiedDiff));

        if (!string.IsNullOrWhiteSpace(itemDiffs))
        {
            return itemDiffs;
        }

        if (string.IsNullOrWhiteSpace(localGitDiff))
        {
            localGitDiff = BuildLocalGitDiff(item);
        }

        if (LooksLikeUnifiedDiff(localGitDiff))
        {
            return localGitDiff!.TrimEnd();
        }

        if (item.Changes is not { Count: > 0 })
        {
            return "No diff captured yet.";
        }

        return "Waiting for app-server diff payload.";
    }

    private static string ResolveFileDiffFromApi(
        string path,
        string? changeDiff,
        string itemDiff,
        string turnDiff,
        string fallbackAggregatedDiff,
        string localGitDiff,
        int totalChanges)
    {
        if (LooksLikeUnifiedDiff(changeDiff))
        {
            return changeDiff!.TrimEnd();
        }

        var extractedItemDiff = TryExtractFileDiff(itemDiff, path);
        if (!string.IsNullOrWhiteSpace(extractedItemDiff))
        {
            return extractedItemDiff;
        }

        var extractedTurnDiff = TryExtractFileDiff(turnDiff, path);
        if (!string.IsNullOrWhiteSpace(extractedTurnDiff))
        {
            return extractedTurnDiff;
        }

        var extractedLocalDiff = TryExtractFileDiff(localGitDiff, path);
        if (!string.IsNullOrWhiteSpace(extractedLocalDiff))
        {
            return extractedLocalDiff;
        }

        if (totalChanges == 1)
        {
            if (LooksLikeUnifiedDiff(itemDiff))
            {
                return itemDiff.TrimEnd();
            }

            if (LooksLikeUnifiedDiff(turnDiff))
            {
                return turnDiff.TrimEnd();
            }

            if (LooksLikeUnifiedDiff(fallbackAggregatedDiff))
            {
                return fallbackAggregatedDiff.TrimEnd();
            }

            if (LooksLikeUnifiedDiff(localGitDiff))
            {
                return localGitDiff.TrimEnd();
            }
        }

        return string.Empty;
    }

    private static string TryExtractFileDiff(string? unifiedDiff, string path)
    {
        if (!LooksLikeUnifiedDiff(unifiedDiff))
        {
            return string.Empty;
        }

        var normalizedPath = NormalizeDiffPath(path);
        var lines = unifiedDiff!.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder();
        var capturing = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (capturing && builder.Length > 0)
                {
                    break;
                }

                capturing = DiffHeaderMatchesPath(line, normalizedPath);
                if (capturing)
                {
                    builder.AppendLine(line);
                }

                continue;
            }

            if (capturing)
            {
                builder.AppendLine(line);
            }
        }

        if (builder.Length > 0)
        {
            return builder.ToString().TrimEnd();
        }

        if (LooksLikeUnifiedDiff(unifiedDiff) && !unifiedDiff!.Contains("diff --git ", StringComparison.Ordinal))
        {
            return unifiedDiff.TrimEnd();
        }

        return string.Empty;
    }

    private static bool DiffHeaderMatchesPath(string headerLine, string normalizedPath)
    {
        var parts = headerLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return false;
        }

        var left = NormalizeDiffPath(parts[2]);
        var right = NormalizeDiffPath(parts[3]);
        return string.Equals(left, normalizedPath, StringComparison.Ordinal)
            || string.Equals(right, normalizedPath, StringComparison.Ordinal);
    }

    private static string NormalizeDiffPath(string path)
    {
        var normalized = path.Trim().Trim('"').Replace('\\', '/');
        if (normalized.StartsWith("a/", StringComparison.Ordinal) || normalized.StartsWith("b/", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static bool LooksLikeUnifiedDiff(string? diff)
    {
        if (string.IsNullOrWhiteSpace(diff))
        {
            return false;
        }

        return diff.Contains("diff --git ", StringComparison.Ordinal)
            || diff.Contains("\n@@ ", StringComparison.Ordinal)
            || diff.StartsWith("@@ ", StringComparison.Ordinal)
            || diff.Contains("\n--- ", StringComparison.Ordinal)
            || diff.StartsWith("--- ", StringComparison.Ordinal);
    }

    private string BuildLocalGitDiff(ThreadItem item)
    {
        var workingDirectory = ResolveDiffWorkingDirectory(item);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return string.Empty;
        }

        return _gitDiffService.BuildLocalGitDiff(item, workingDirectory);
    }

    private string ResolveDiffWorkingDirectory(ThreadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Cwd))
        {
            return item.Cwd;
        }

        if (!string.IsNullOrWhiteSpace(_currentThreadDetail?.Cwd))
        {
            return _currentThreadDetail.Cwd;
        }

        return string.IsNullOrWhiteSpace(WorkingDirectory)
            ? Environment.CurrentDirectory
            : WorkingDirectory;
    }

    private string BuildFileChangeDocument(ThreadTurn turn, ThreadItem item, string turnId)
    {
        var diff = BuildAggregatedDiff(turnId, item);
        var lines = new List<string>
        {
            "# File Changes",
            string.Empty,
            $"- turn: {turn.Id ?? "turn"}",
            $"- item: {item.Id ?? "item"}",
            $"- status: {item.Status ?? "unknown"}",
            string.Empty,
            "## Files"
        };

        foreach (var change in item.Changes ?? Array.Empty<FileUpdateChange>())
        {
            lines.Add($"- {change.Kind ?? "change"} {change.Path ?? "file"}");
        }

        lines.Add(string.Empty);
        lines.Add("## Aggregated diff");
        lines.Add(diff);
        return string.Join('\n', lines);
    }

    private static string BuildToolBody(ThreadItem item)
    {
        var target = string.IsNullOrWhiteSpace(item.Server)
            ? item.Tool ?? "tool"
            : $"{item.Server}/{item.Tool ?? "tool"}";
        return $"{target} · {item.Status ?? (item.Success is null ? "pending" : item.Success.Value ? "success" : "failed")}";
    }

    private static string BuildToolResultPreview(ThreadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ItemError?.Message))
        {
            return item.ItemError.Message;
        }

        if (item.Result?.StructuredContent is { } structuredContent)
        {
            return PreviewMultiline(AppJson.PrettyPrint(structuredContent), 8, 420);
        }

        if (item.Result?.Content is { Count: > 0 } content)
        {
            return PreviewMultiline(string.Join('\n', content.Select(AppJson.ExtractContentText)), 8, 420);
        }

        if (item.ContentItems is { Count: > 0 } dynamicContent)
        {
            return PreviewMultiline(string.Join('\n', dynamicContent.Select(static content => content.Text ?? content.ImageUrl ?? content.Type ?? string.Empty)), 8, 420);
        }

        return item.Success is null ? "Waiting for tool output." : item.Success.Value ? "Tool completed successfully." : "Tool reported failure.";
    }

    private static string BuildDynamicToolContentPreview(ThreadItem item)
    {
        if (item.ContentItems is not { Count: > 0 })
        {
            return item.Success is null ? string.Empty : item.Success.Value ? "Tool completed successfully." : "Tool returned a failure result.";
        }

        return PreviewMultiline(string.Join('\n', item.ContentItems.Select(static content => content.Text ?? content.ImageUrl ?? content.Type ?? string.Empty)), 8, 420);
    }

    private static void AppendDeltaChunk(IDictionary<string, string> target, string? key, string? delta)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(delta))
        {
            return;
        }

        if (target.TryGetValue(key, out var existing))
        {
            target[key] = existing + delta;
            return;
        }

        target[key] = delta;
    }

    private static void AppendMessageChunk(IDictionary<string, string> target, string? key, string? message)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (target.TryGetValue(key, out var existing))
        {
            target[key] = string.IsNullOrWhiteSpace(existing) ? message : $"{existing}\n{message}";
            return;
        }

        target[key] = message;
    }

    private static string BuildToolDocument(ThreadTurn turn, ThreadItem item)
    {
        var prettyArguments = AppJson.PrettyPrint(item.Arguments);
        return string.Join('\n',
        [
            "# Tool Call",
            string.Empty,
            $"- turn: {turn.Id ?? "turn"}",
            $"- item: {item.Id ?? "item"}",
            $"- status: {item.Status ?? "unknown"}",
            $"- server: {item.Server ?? "client"}",
            $"- tool: {item.Tool ?? "tool"}",
            string.Empty,
            "## Arguments",
            string.IsNullOrWhiteSpace(prettyArguments) ? "{}" : prettyArguments,
            string.Empty,
            "## Result",
            BuildToolResultPreview(item)
        ]);
    }

    private string BuildPlanDocument(string turnId, ThreadItem item)
    {
        if (_latestTurnPlans.TryGetValue(turnId, out var planNotification) && planNotification.Plan is { Count: > 0 })
        {
            var lines = new List<string>
            {
                "# Turn Plan",
                string.Empty,
                planNotification.Explanation ?? string.Empty,
                string.Empty
            };

            lines.AddRange(planNotification.Plan.Select(step => $"- [{step.Status}] {step.Step}"));
            return string.Join('\n', lines);
        }

        return $"# Plan\n\n{item.Text ?? DescribeThreadItem(item)}";
    }

    private static string BuildReasoningBody(ThreadItem item)
    {
        var summary = AppJson.PrettyPrint(item.SummaryJson);
        return string.IsNullOrWhiteSpace(summary)
            ? DescribeThreadItem(item)
            : PreviewMultiline(summary, 5, 280);
    }

    private static string BuildReasoningDocument(string turnId, ThreadItem item)
    {
        return string.Join('\n',
        [
            "# Reasoning",
            string.Empty,
            $"- turn: {turnId}",
            $"- item: {item.Id ?? "item"}",
            string.Empty,
            "## Summary",
            string.IsNullOrWhiteSpace(AppJson.PrettyPrint(item.SummaryJson)) ? "No summary captured." : AppJson.PrettyPrint(item.SummaryJson),
            string.Empty,
            "## Content",
            string.IsNullOrWhiteSpace(AppJson.PrettyPrint(item.ContentJson)) ? "No raw reasoning blocks captured." : AppJson.PrettyPrint(item.ContentJson)
        ]);
    }

    private static string PreviewMultiline(string? value, int maxLines, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var lines = normalized.Split('\n').Take(maxLines).ToList();
        var preview = string.Join('\n', lines);
        return preview.Length <= maxCharacters ? preview : preview[..maxCharacters].TrimEnd() + "...";
    }

}
