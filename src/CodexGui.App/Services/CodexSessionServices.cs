using CodexGui.AppServer.Client;
using CodexGui.AppServer.Models;

namespace CodexGui.App.Services;

internal interface ICodexSessionService : IAsyncDisposable
{
    event EventHandler<AppServerNotificationEventArgs>? NotificationReceived;

    event EventHandler<AppServerConnectionChangedEventArgs>? ConnectionStateChanged;

    Func<AppServerServerRequestMessage, CancellationToken, Task<AppServerServerRequestCompletion>>? ServerRequestHandlerAsync { get; set; }

    bool IsConnected { get; }

    Task ConnectAsync(AppServerClientOptions options, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<T?> SendRequestAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default);
}

internal sealed class CodexSessionService(CodexAppServerClient client) : ICodexSessionService
{
    private readonly CodexAppServerClient _client = client;

    public event EventHandler<AppServerNotificationEventArgs>? NotificationReceived
    {
        add => _client.NotificationReceived += value;
        remove => _client.NotificationReceived -= value;
    }

    public event EventHandler<AppServerConnectionChangedEventArgs>? ConnectionStateChanged
    {
        add => _client.ConnectionStateChanged += value;
        remove => _client.ConnectionStateChanged -= value;
    }

    public Func<AppServerServerRequestMessage, CancellationToken, Task<AppServerServerRequestCompletion>>? ServerRequestHandlerAsync
    {
        get => _client.ServerRequestHandlerAsync;
        set => _client.ServerRequestHandlerAsync = value;
    }

    public bool IsConnected => _client.IsConnected;

    public Task ConnectAsync(AppServerClientOptions options, CancellationToken cancellationToken = default)
        => _client.ConnectAsync(options, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _client.DisconnectAsync(cancellationToken);

    public Task<T?> SendRequestAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default)
        => _client.SendRequestAsync<T>(method, parameters, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

internal sealed class NullCodexSessionService : ICodexSessionService
{
    public static NullCodexSessionService Instance { get; } = new();

    private NullCodexSessionService()
    {
    }

    public event EventHandler<AppServerNotificationEventArgs>? NotificationReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<AppServerConnectionChangedEventArgs>? ConnectionStateChanged
    {
        add { }
        remove { }
    }

    public Func<AppServerServerRequestMessage, CancellationToken, Task<AppServerServerRequestCompletion>>? ServerRequestHandlerAsync { get; set; }

    public bool IsConnected => false;

    public Task ConnectAsync(AppServerClientOptions options, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<T?> SendRequestAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default)
        => Task.FromResult<T?>(default);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
