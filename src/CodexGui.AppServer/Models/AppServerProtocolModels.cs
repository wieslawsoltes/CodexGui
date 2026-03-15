using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexGui.AppServer.Models;

public sealed class AppServerNotificationMessage
{
    public required string Method { get; init; }

    public required JsonElement Parameters { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }
}

public sealed class AppServerLogMessage
{
    public required string Source { get; init; }

    public required string Text { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}

public sealed class AppServerNotificationEventArgs(AppServerNotificationMessage notification) : EventArgs
{
    public AppServerNotificationMessage Notification { get; } = notification;
}

public sealed class AppServerLogEventArgs(AppServerLogMessage message) : EventArgs
{
    public AppServerLogMessage Message { get; } = message;
}

public sealed class AppServerConnectionChangedEventArgs(bool isConnected, string status, string? detail) : EventArgs
{
    public bool IsConnected { get; } = isConnected;

    public string Status { get; } = status;

    public string? Detail { get; } = detail;
}

public sealed class AppServerServerRequestMessage
{
    public required JsonElement RequestId { get; init; }

    public required string Method { get; init; }

    public required JsonElement Parameters { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }
}

public sealed class AppServerServerRequestError
{
    public required int Code { get; init; }

    public required string Message { get; init; }

    public object? Data { get; init; }
}

public sealed class AppServerServerRequestCompletion
{
    public object? Result { get; init; }

    public AppServerServerRequestError? Error { get; init; }

    public static AppServerServerRequestCompletion FromResult(object? result) => new()
    {
        Result = result
    };

    public static AppServerServerRequestCompletion FromError(int code, string message, object? data = null) => new()
    {
        Error = new AppServerServerRequestError
        {
            Code = code,
            Message = message,
            Data = data
        }
    };
}

public sealed class AppServerException(string message, int? code = null) : Exception(message)
{
    public int? Code { get; } = code;
}
