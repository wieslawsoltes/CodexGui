using System.Text.Json.Serialization;

namespace CodexGui.AppServer.Models;

public sealed class AccountLoginStartResult
{
    [JsonPropertyName("loginId")]
    public string? LoginId { get; set; }

    [JsonPropertyName("authUrl")]
    public string? AuthUrl { get; set; }
}

public sealed class AccountLogoutResult
{
}

public sealed class AccountLoginCancelResult
{
}
