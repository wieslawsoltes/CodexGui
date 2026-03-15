using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexGui.AppServer.Models;

namespace CodexGui.AppServer.Client;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private const int MaxOverloadRetries = 3;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonElement EmptyObjectElement = CreateEmptyObjectElement();

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _connectionCancellationTokenSource;
    private Process? _process;
    private ClientWebSocket? _webSocket;
    private StreamWriter? _writer;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private Task? _webSocketTask;
    private long _requestId;

    public event EventHandler<AppServerNotificationEventArgs>? NotificationReceived;

    public event EventHandler<AppServerLogEventArgs>? LogMessageReceived;

    public event EventHandler<AppServerConnectionChangedEventArgs>? ConnectionStateChanged;

    public Func<AppServerServerRequestMessage, CancellationToken, Task<AppServerServerRequestCompletion>>? ServerRequestHandlerAsync { get; set; }

    public bool IsConnected { get; private set; }

    public string? ServerUserAgent { get; private set; }

    private static JsonElement CreateEmptyObjectElement()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    public async Task ConnectAsync(AppServerClientOptions options, CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        await DisconnectAsync().ConfigureAwait(false);

        try
        {
            if (TryGetRemoteEndpoint(options.Command, out var remoteEndpoint))
            {
                var webSocket = new ClientWebSocket();
                await webSocket.ConnectAsync(remoteEndpoint, cancellationToken).ConfigureAwait(false);
                _webSocket = webSocket;
                _connectionCancellationTokenSource = new CancellationTokenSource();
                _webSocketTask = Task.Run(() => ReadWebSocketLoopAsync(webSocket, _connectionCancellationTokenSource.Token));
                RaiseConnectionChanged(false, "Connecting", $"Connecting to remote app-server {remoteEndpoint}.");
                RaiseLog("session", $"Remote app-server transport connected: {remoteEndpoint}");
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = options.Command,
                    WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                        ? Environment.CurrentDirectory
                        : options.WorkingDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                foreach (var argument in CommandLineTokenizer.Tokenize(options.Arguments))
                {
                    startInfo.ArgumentList.Add(argument);
                }

                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                process.Exited += HandleProcessExited;

                try
                {
                    if (!process.Start())
                    {
                        throw new InvalidOperationException("Failed to start the Codex app-server process.");
                    }
                }
                catch
                {
                    process.Exited -= HandleProcessExited;
                    process.Dispose();
                    throw;
                }

                _process = process;
                _writer = process.StandardInput;
                _connectionCancellationTokenSource = new CancellationTokenSource();
                _stdoutTask = Task.Run(() => ReadStdOutLoopAsync(process.StandardOutput, _connectionCancellationTokenSource.Token));
                _stderrTask = Task.Run(() => ReadStdErrLoopAsync(process.StandardError, _connectionCancellationTokenSource.Token));

                RaiseConnectionChanged(false, "Connecting", $"Launching {options.Command} {options.Arguments}".Trim());
                RaiseLog("session", "App-server process started.");
            }

            var initializeResult = await SendRequestAsync<InitializeResult>(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = options.ClientName,
                        title = options.ClientTitle,
                        version = options.ClientVersion
                    },
                    capabilities = new
                    {
                        experimentalApi = options.UseExperimentalApi,
                        optOutNotificationMethods = options.OptOutNotificationMethods
                    }
                },
                cancellationToken).ConfigureAwait(false);

            ServerUserAgent = initializeResult?.UserAgent;
            await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);

            IsConnected = true;
            RaiseConnectionChanged(true, "Connected", ServerUserAgent ?? "Handshake complete");
            RaiseLog("session", "Connected to Codex app-server.");
        }
        catch
        {
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        ClientWebSocket? webSocket;
        StreamWriter? writer;
        CancellationTokenSource? connectionCancellationTokenSource;
        Task? stdoutTask;
        Task? stderrTask;
        Task? webSocketTask;

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            process = _process;
            webSocket = _webSocket;
            writer = _writer;
            connectionCancellationTokenSource = _connectionCancellationTokenSource;
            stdoutTask = _stdoutTask;
            stderrTask = _stderrTask;
            webSocketTask = _webSocketTask;

            _process = null;
            _webSocket = null;
            _writer = null;
            _connectionCancellationTokenSource = null;
            _stdoutTask = null;
            _stderrTask = null;
            _webSocketTask = null;
            ServerUserAgent = null;
            IsConnected = false;
        }
        finally
        {
            _sendLock.Release();
        }

        connectionCancellationTokenSource?.Cancel();

        if (writer is not null)
        {
            try
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (stdoutTask is not null)
        {
            try
            {
                await stdoutTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (stderrTask is not null)
        {
            try
            {
                await stderrTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (webSocket is not null)
        {
            try
            {
                if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                webSocket.Dispose();
            }
        }

        if (webSocketTask is not null)
        {
            try
            {
                await webSocketTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (process is not null)
        {
            process.Exited -= HandleProcessExited;

            try
            {
                if (!process.HasExited)
                {
                    if (!process.WaitForExit(1500))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        foreach (var pendingRequest in _pendingRequests)
        {
            pendingRequest.Value.TrySetException(new AppServerException("The app-server connection was closed."));
        }

        _pendingRequests.Clear();
        connectionCancellationTokenSource?.Dispose();
        RaiseConnectionChanged(false, "Disconnected", "The app-server transport is not active.");
    }

    public async Task<T?> SendRequestAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        EnsureWritableConnection();
        for (var attempt = 0; ; attempt++)
        {
            var requestId = Interlocked.Increment(ref _requestId);
            var completionSource = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestId] = completionSource;

            try
            {
                await SendMessageAsync(new
                {
                    method,
                    id = requestId,
                    @params = parameters
                }, cancellationToken).ConfigureAwait(false);

                var result = await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (result is null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return default;
                }

                return result.Value.Deserialize<T>(SerializerOptions);
            }
            catch (AppServerException exception) when (exception.Code == -32001 && attempt < MaxOverloadRetries)
            {
                var retryDelay = ComputeOverloadRetryDelay(attempt);
                RaiseLog("retry", $"Server overloaded for '{method}'. Retrying in {retryDelay.TotalMilliseconds:F0} ms.");
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }
    }

    public Task SendNotificationAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        EnsureWritableConnection();

        return SendMessageAsync(new
        {
            method,
            @params = parameters
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    private void EnsureWritableConnection()
    {
        if (_webSocket is { State: WebSocketState.Open })
        {
            return;
        }

        if (_writer is null || _process is null || _process.HasExited)
        {
            throw new InvalidOperationException("The app-server transport is not connected.");
        }
    }

    private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(message, SerializerOptions);
        await SendRawMessageAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRawMessageAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureWritableConnection();

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_webSocket is { State: WebSocketState.Open } webSocket)
            {
                var payloadBuffer = Encoding.UTF8.GetBytes(payload);
                await webSocket.SendAsync(payloadBuffer, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var writer = _writer;
                if (writer is null)
                {
                    throw new InvalidOperationException("The app-server transport is not connected.");
                }

                await writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadWebSocketLoopAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var payloadBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                payloadBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var payload = payloadBuilder.ToString();
                payloadBuilder.Clear();

                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                foreach (var line in payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    await ProcessIncomingLineAsync(line, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RaiseLog("error", $"Remote transport read failed: {exception.Message}");
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            IsConnected = false;
            RaiseConnectionChanged(false, "Disconnected", "The remote app-server transport is not active.");
        }
    }

    private async Task ReadStdOutLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                await ProcessIncomingLineAsync(line, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RaiseLog("error", $"Transport read failed: {exception.Message}");
        }
    }

    private async Task ReadStdErrLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    RaiseLog("stderr", line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RaiseLog("error", $"Stderr read failed: {exception.Message}");
        }
    }

    private async Task ProcessIncomingLineAsync(string line, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement.Clone();

        if (root.TryGetProperty("method", out var methodElement))
        {
            var method = methodElement.GetString() ?? "unknown";
            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : EmptyObjectElement;

            if (root.TryGetProperty("id", out var requestId))
            {
                RaiseLog("server-request", method);
                await HandleServerRequestAsync(requestId.Clone(), method, parameters, cancellationToken).ConfigureAwait(false);
                return;
            }

            NotificationReceived?.Invoke(this, new AppServerNotificationEventArgs(new AppServerNotificationMessage
            {
                Method = method,
                Parameters = parameters,
                ReceivedAt = DateTimeOffset.Now
            }));

            return;
        }

        if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number)
        {
            RaiseLog("transport", $"Ignored payload: {line}");
            return;
        }

        var id = idElement.GetInt64();
        if (!_pendingRequests.TryGetValue(id, out var completionSource))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            var message = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "Unknown app-server error.";
            var code = errorElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                ? codeElement.GetInt32()
                : (int?)null;
            completionSource.TrySetException(new AppServerException(message ?? "Unknown app-server error.", code));
            return;
        }

        var result = root.TryGetProperty("result", out var resultElement)
            ? resultElement.Clone()
            : (JsonElement?)null;
        completionSource.TrySetResult(result);
    }

    private async Task HandleServerRequestAsync(JsonElement requestId, string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (ServerRequestHandlerAsync is null)
        {
            await RejectServerRequestAsync(requestId, method, cancellationToken).ConfigureAwait(false);
            return;
        }

        AppServerServerRequestCompletion completion;

        try
        {
            completion = await ServerRequestHandlerAsync(new AppServerServerRequestMessage
            {
                RequestId = requestId,
                Method = method,
                Parameters = parameters,
                ReceivedAt = DateTimeOffset.Now
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            completion = AppServerServerRequestCompletion.FromError(-32603, exception.Message);
        }

        await SendServerRequestCompletionAsync(requestId, completion, cancellationToken).ConfigureAwait(false);
    }

    private Task RejectServerRequestAsync(JsonElement requestId, string method, CancellationToken cancellationToken)
    {
        return SendServerRequestCompletionAsync(
            requestId,
            AppServerServerRequestCompletion.FromError(-32601, $"{method} is not supported in the current CodexGui client."),
            cancellationToken);
    }

    private Task SendServerRequestCompletionAsync(JsonElement requestId, AppServerServerRequestCompletion completion, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["id"] = JsonNode.Parse(requestId.GetRawText())
        };

        if (completion.Error is not null)
        {
            var errorObject = new JsonObject
            {
                ["code"] = completion.Error.Code,
                ["message"] = completion.Error.Message
            };

            if (completion.Error.Data is not null)
            {
                errorObject["data"] = JsonSerializer.SerializeToNode(completion.Error.Data, SerializerOptions);
            }

            payload["error"] = errorObject;
        }
        else
        {
            payload["result"] = completion.Result is null
                ? new JsonObject()
                : JsonSerializer.SerializeToNode(completion.Result, SerializerOptions);
        }

        return SendRawMessageAsync(payload.ToJsonString(), cancellationToken);
    }

    private void HandleProcessExited(object? sender, EventArgs eventArgs)
    {
        if (sender is not Process exitedProcess)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var lockAcquired = false;
            try
            {
                await _sendLock.WaitAsync().ConfigureAwait(false);
                lockAcquired = true;

                if (!ReferenceEquals(_process, exitedProcess))
                {
                    return;
                }

                IsConnected = false;
                RaiseConnectionChanged(false, "Exited", "The app-server process terminated.");
                RaiseLog("session", "App-server process exited.");
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                if (lockAcquired)
                {
                    _sendLock.Release();
                }
            }
        });
    }

    private void RaiseLog(string source, string text)
    {
        LogMessageReceived?.Invoke(this, new AppServerLogEventArgs(new AppServerLogMessage
        {
            Source = source,
            Text = text,
            Timestamp = DateTimeOffset.Now
        }));
    }

    private void RaiseConnectionChanged(bool isConnected, string status, string? detail)
    {
        ConnectionStateChanged?.Invoke(this, new AppServerConnectionChangedEventArgs(isConnected, status, detail));
    }

    private static bool TryGetRemoteEndpoint(string value, out Uri endpoint)
    {
        endpoint = default!;

        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
            candidate is not null &&
            (string.Equals(candidate.Scheme, "ws", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(candidate.Scheme, "wss", StringComparison.OrdinalIgnoreCase)))
        {
            endpoint = candidate;
            return true;
        }
        return false;
    }

    private static TimeSpan ComputeOverloadRetryDelay(int attempt)
    {
        var baseDelayMs = 250 * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.Next(0, 200);
        return TimeSpan.FromMilliseconds(baseDelayMs + jitterMs);
    }
}
