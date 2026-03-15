using CodexGui.App.ViewModels;
using CodexGui.AppServer.Models;

namespace CodexGui.App.Services;

internal interface IPendingInteractionFactory
{
    PendingInteractionViewModel? Create(
        AppServerServerRequestMessage request,
        Action<AppServerServerRequestCompletion> completeAction);
}

internal sealed class PendingInteractionFactory : IPendingInteractionFactory
{
    public PendingInteractionViewModel? Create(
        AppServerServerRequestMessage request,
        Action<AppServerServerRequestCompletion> completeAction)
    {
        var requestKey = request.RequestId.GetRawText();

        return request.Method switch
        {
            "item/commandExecution/requestApproval" => CreateCommandExecutionApproval(request, requestKey, completeAction),
            "item/fileChange/requestApproval" => CreateFileChangeApproval(request, requestKey, completeAction),
            "item/tool/requestUserInput" => CreateToolUserInput(request, requestKey, completeAction),
            "item/tool/call" => CreateDynamicToolCall(request, requestKey, completeAction),
            "account/chatgptAuthTokens/refresh" => CreateChatGptTokenRefresh(request, requestKey, completeAction),
            _ => null
        };
    }

    private static PendingInteractionViewModel CreateCommandExecutionApproval(
        AppServerServerRequestMessage request,
        string requestKey,
        Action<AppServerServerRequestCompletion> completeAction)
    {
        var parameters = AppJson.Deserialize<CommandExecutionRequestApprovalParams>(request.Parameters);
        var networkContext = parameters?.NetworkApprovalContext is null
            ? string.Empty
            : $"\n\nNetwork destination: {parameters.NetworkApprovalContext.Host}:{parameters.NetworkApprovalContext.Port?.ToString() ?? "?"} ({parameters.NetworkApprovalContext.Protocol ?? "unknown"}).";

        return new PendingInteractionViewModel(
            requestKey: requestKey,
            method: request.Method,
            title: "Command approval required",
            detail: $"Codex wants to execute a command in the active workspace.{networkContext}",
            meta: $"{parameters?.ThreadId ?? "thread"} · {parameters?.TurnId ?? "turn"}",
            badge: parameters?.Reason ?? (parameters?.NetworkApprovalContext is null ? "approval" : "network"),
            commandPreview: parameters?.Command ?? parameters?.Cwd,
            responsePlaceholder: null,
            showAcceptForSession: true,
            questions: null,
            proposedExecpolicyAmendment: parameters?.ProposedExecpolicyAmendment?.ToArray(),
            proposedNetworkPolicyAmendment: parameters?.ProposedNetworkPolicyAmendments?.FirstOrDefault(),
            accentBrush: ShellBrushes.Amber,
            surfaceBrush: ShellBrushes.Paper,
            completeAction: completeAction);
    }

    private static PendingInteractionViewModel CreateFileChangeApproval(
        AppServerServerRequestMessage request,
        string requestKey,
        Action<AppServerServerRequestCompletion> completeAction)
    {
        var parameters = AppJson.Deserialize<FileChangeRequestApprovalParams>(request.Parameters);
        var detail = string.IsNullOrWhiteSpace(parameters?.GrantRoot)
            ? "Codex proposed file changes that require confirmation before they are written."
            : $"Codex wants permission to write under {parameters.GrantRoot}.";

        return new PendingInteractionViewModel(
            requestKey: requestKey,
            method: request.Method,
            title: "File change approval required",
            detail: detail,
            meta: $"{parameters?.ThreadId ?? "thread"} · {parameters?.TurnId ?? "turn"}",
            badge: parameters?.Reason ?? "file changes",
            commandPreview: parameters?.GrantRoot,
            responsePlaceholder: null,
            showAcceptForSession: true,
            questions: null,
            proposedExecpolicyAmendment: null,
            proposedNetworkPolicyAmendment: null,
            accentBrush: ShellBrushes.Amber,
            surfaceBrush: ShellBrushes.Paper,
            completeAction: completeAction);
    }

    private static PendingInteractionViewModel CreateToolUserInput(
        AppServerServerRequestMessage request,
        string requestKey,
        Action<AppServerServerRequestCompletion> completeAction)
    {
        var parameters = AppJson.Deserialize<ToolRequestUserInputParams>(request.Parameters);
        var questions = parameters?.Questions?.Select(question => new PendingInteractionQuestionViewModel(
            question.Id ?? Guid.NewGuid().ToString("N"),
            question.Header ?? "Question",
            question.Question ?? string.Empty,
            question.IsSecret,
            question.Options)).ToList();

        return new PendingInteractionViewModel(
            requestKey: requestKey,
            method: request.Method,
            title: "Tool needs user input",
            detail: "Answer the tool prompt so Codex can continue the turn.",
            meta: $"{parameters?.ThreadId ?? "thread"} · {parameters?.TurnId ?? "turn"}",
            badge: $"{questions?.Count ?? 0} question(s)",
            commandPreview: null,
            responsePlaceholder: null,
            showAcceptForSession: false,
            questions: questions,
            proposedExecpolicyAmendment: null,
            proposedNetworkPolicyAmendment: null,
            accentBrush: ShellBrushes.Blue,
            surfaceBrush: ShellBrushes.Paper,
            completeAction: completeAction);
    }

    private static PendingInteractionViewModel CreateDynamicToolCall(
        AppServerServerRequestMessage request,
        string requestKey,
        Action<AppServerServerRequestCompletion> completeAction)
    {
        var parameters = AppJson.Deserialize<DynamicToolCallParams>(request.Parameters);

        return new PendingInteractionViewModel(
            requestKey: requestKey,
            method: request.Method,
            title: "Dynamic tool call",
            detail: "Provide a client-side tool result to let Codex continue.",
            meta: $"{parameters?.ThreadId ?? "thread"} · {parameters?.TurnId ?? "turn"}",
            badge: parameters?.Tool ?? "tool",
            commandPreview: AppJson.PrettyPrint(parameters?.Arguments),
            responsePlaceholder: "Return text that Codex should receive from the tool.",
            showAcceptForSession: false,
            questions: null,
            proposedExecpolicyAmendment: null,
            proposedNetworkPolicyAmendment: null,
            accentBrush: ShellBrushes.Blue,
            surfaceBrush: ShellBrushes.Paper,
            completeAction: completeAction);
    }

    private static PendingInteractionViewModel CreateChatGptTokenRefresh(
        AppServerServerRequestMessage request,
        string requestKey,
        Action<AppServerServerRequestCompletion> completeAction)
    {
        return new PendingInteractionViewModel(
            requestKey: requestKey,
            method: request.Method,
            title: "ChatGPT token refresh",
            detail: "Codex requested fresh ChatGPT auth tokens. Paste a JSON object matching your token handoff payload.",
            meta: "account · chatgptAuthTokens",
            badge: "auth",
            commandPreview: AppJson.PrettyPrint(request.Parameters),
            responsePlaceholder: "{ \"accessToken\": \"...\", \"idToken\": \"...\" }",
            showAcceptForSession: false,
            questions: null,
            proposedExecpolicyAmendment: null,
            proposedNetworkPolicyAmendment: null,
            accentBrush: ShellBrushes.Blue,
            surfaceBrush: ShellBrushes.Paper,
            completeAction: completeAction);
    }
}
