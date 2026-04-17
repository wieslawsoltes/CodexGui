using CodexGui.App.Services;
using CodexGui.App.ViewModels;
using CodexGui.AppServer.Client;
using CodexGui.AppServer.Models;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CodexGui.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Constructor_seeds_shell_state_with_semantic_tones()
    {
        var viewModel = new MainWindowViewModel();

        Assert.Equal("CodexGui", viewModel.CurrentWorkspaceTitle);
        Assert.Equal("Interactive desktop shell for Codex app-server sessions", viewModel.CurrentWorkspaceSubtitle);
        Assert.Equal(ShellTone.Neutral, viewModel.ConnectionTone);
        Assert.Equal(7, viewModel.StatusChips.Count);

        var placeholder = Assert.Single(viewModel.ConversationItems);
        Assert.Equal("placeholder", placeholder.Kind);
        Assert.Equal(ShellTone.Green, placeholder.AccentTone);
        Assert.Equal(ShellTone.Paper, placeholder.SurfaceTone);
        Assert.Equal(ShellTone.TextPrimary, placeholder.ForegroundTone);
    }

    [Fact]
    public void Connection_tone_tracks_busy_and_connected_state_transitions()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.IsBusy = true;
        Assert.Equal(ShellTone.Blue, viewModel.ConnectionTone);

        viewModel.IsConnected = true;
        Assert.Equal(ShellTone.Green, viewModel.ConnectionTone);

        viewModel.IsBusy = false;
        Assert.Equal(ShellTone.Green, viewModel.ConnectionTone);

        viewModel.IsConnected = false;
        Assert.Equal(ShellTone.Neutral, viewModel.ConnectionTone);
    }

    [Fact]
    public void Working_directory_and_login_state_are_projected_for_the_shell()
    {
        var viewModel = new MainWindowViewModel
        {
            WorkingDirectory = "/tmp/codex/workspace/",
            ActiveAccountLoginId = null
        };

        Assert.Equal("workspace", viewModel.WorkingDirectoryName);
        Assert.False(viewModel.HasActiveLogin);

        viewModel.ActiveAccountLoginId = "login_123";

        Assert.True(viewModel.HasActiveLogin);
    }

    [Fact]
    public void File_update_change_converter_accepts_non_string_kind_payloads()
    {
        var json =
            """
            {
              "path": "src/File.cs",
              "kind": { "type": "update", "reason": "rewrite" },
              "diff": "@@ -1 +1 @@"
            }
            """;

        var change = JsonSerializer.Deserialize<FileUpdateChange>(json);

        Assert.NotNull(change);
        Assert.Equal("src/File.cs", change!.Path);
        using var document = JsonDocument.Parse(change.Kind);
        Assert.Equal("update", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("rewrite", document.RootElement.GetProperty("reason").GetString());
        Assert.Equal("@@ -1 +1 @@", change.Diff);
    }

    [Fact]
    public async Task EnsureInitialConnectionAsync_connects_once_and_loads_threads()
    {
        var sessionService = new FakeCodexSessionService();
        var viewModel = new MainWindowViewModel(
            sessionService,
            new ImmediateUiDispatcher(),
            new GitDiffService(),
            new PendingInteractionFactory());

        await viewModel.EnsureInitialConnectionAsync();
        await viewModel.EnsureInitialConnectionAsync();

        Assert.Equal(1, sessionService.ConnectCallCount);
        Assert.True(viewModel.IsConnected);
        var thread = Assert.Single(viewModel.RecentThreads);
        Assert.Equal("thread-1", thread.Id);
        Assert.Equal("Connected", viewModel.ConnectionState);
    }

    [Fact]
    public async Task Selecting_thread_does_not_block_ui_and_runs_local_git_diff_once()
    {
        var gitDiffService = new BlockingGitDiffService();
        var sessionService = new FakeCodexSessionService(createThreadReadResult: static threadId => new ThreadReadResult
        {
            Thread = new ThreadDetail
            {
                Id = threadId,
                Name = "Thread One",
                Preview = "Test preview",
                Ephemeral = false,
                ModelProvider = "openai",
                CreatedAt = 1,
                UpdatedAt = 2,
                Status = new ThreadStatusInfo
                {
                    Type = "idle",
                    ActiveFlags = Array.Empty<string>()
                },
                Cwd = "/tmp/codex",
                Turns =
                [
                    new ThreadTurn
                    {
                        Id = "turn-1",
                        Status = "completed",
                        Items =
                        [
                            new ThreadItem
                            {
                                Id = "item-1",
                                Type = "fileChange",
                                Status = "completed",
                                Changes =
                                [
                                    new FileUpdateChange
                                    {
                                        Path = "src/File.cs",
                                        Kind = "update",
                                        Diff = string.Empty
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        });

        var viewModel = new MainWindowViewModel(
            sessionService,
            new ImmediateUiDispatcher(),
            gitDiffService,
            new PendingInteractionFactory());

        await viewModel.EnsureInitialConnectionAsync();
        var thread = Assert.Single(viewModel.RecentThreads);

        var stopwatch = Stopwatch.StartNew();
        viewModel.SelectedThread = thread;
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(200));
        await gitDiffService.WaitForStartAsync();

        gitDiffService.Release();
        await gitDiffService.WaitForCompletionAsync();

        Assert.Equal(1, gitDiffService.CallCount);
    }

    private sealed class FakeCodexSessionService(Func<string, ThreadReadResult>? createThreadReadResult = null) : ICodexSessionService
    {
        private readonly Func<string, ThreadReadResult>? _createThreadReadResult = createThreadReadResult;

        public event EventHandler<AppServerNotificationEventArgs>? NotificationReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<AppServerConnectionChangedEventArgs>? ConnectionStateChanged;

        public Func<AppServerServerRequestMessage, CancellationToken, Task<AppServerServerRequestCompletion>>? ServerRequestHandlerAsync { get; set; }

        public bool IsConnected { get; private set; }

        public int ConnectCallCount { get; private set; }

        public Task ConnectAsync(AppServerClientOptions options, CancellationToken cancellationToken = default)
        {
            ConnectCallCount++;
            IsConnected = true;
            ConnectionStateChanged?.Invoke(this, new AppServerConnectionChangedEventArgs(true, "Connected", "Connected for test."));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            ConnectionStateChanged?.Invoke(this, new AppServerConnectionChangedEventArgs(false, "Disconnected", "Disconnected for test."));
            return Task.CompletedTask;
        }

        public Task<T?> SendRequestAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default)
        {
            object? result = method switch
            {
                "thread/list" => new ThreadListResult
                {
                    Data =
                    [
                        new ThreadSummary
                        {
                            Id = "thread-1",
                            Name = "Thread One",
                            Preview = "Test preview",
                            Ephemeral = false,
                            ModelProvider = "openai",
                            CreatedAt = 1,
                            UpdatedAt = 2,
                            Status = new ThreadStatusInfo
                            {
                                Type = "idle",
                                ActiveFlags = Array.Empty<string>()
                            },
                            Cwd = "/tmp/codex"
                        }
                    ],
                    NextCursor = string.Empty
                },
                "thread/read" when _createThreadReadResult is not null => _createThreadReadResult(ExtractThreadId(parameters)),
                _ => default(T)
            };

            return Task.FromResult((T?)result);
        }

        private static string ExtractThreadId(object? parameters)
        {
            if (parameters is null)
            {
                return "thread-1";
            }

            var element = JsonSerializer.SerializeToElement(parameters);
            return element.TryGetProperty("threadId", out var threadIdElement)
                ? threadIdElement.GetString() ?? "thread-1"
                : "thread-1";
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingGitDiffService : IGitDiffService
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private int _callCount;

        public int CallCount => _callCount;

        public string BuildLocalGitDiff(ThreadItem item, string workingDirectory)
        {
            Interlocked.Increment(ref _callCount);
            _started.TrySetResult(true);
            _release.Wait(TimeSpan.FromSeconds(2));
            _completed.TrySetResult(true);
            return "diff --git a/src/File.cs b/src/File.cs\n@@ -1 +1 @@\n-old\n+new";
        }

        public Task WaitForStartAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public Task WaitForCompletionAsync() => _completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void Release() => _release.Set();
    }
}
