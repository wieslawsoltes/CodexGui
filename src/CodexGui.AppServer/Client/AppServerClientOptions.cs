namespace CodexGui.AppServer.Client;

public sealed record AppServerClientOptions(
    string Command,
    string Arguments,
    string WorkingDirectory,
    string ClientName,
    string ClientTitle,
    string ClientVersion,
    bool UseExperimentalApi,
    IReadOnlyList<string> OptOutNotificationMethods);