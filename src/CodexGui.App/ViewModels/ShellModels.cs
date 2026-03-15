using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodexGui.AppServer.Models;

namespace CodexGui.App.ViewModels;

public sealed record StatusChipViewModel(string Label, string Value, IBrush AccentBrush, IBrush SurfaceBrush);

public sealed record ThreadListEntryViewModel(
    string Id,
    string Title,
    string Preview,
    string TimeLabel,
    string Meta,
    string Badge,
    IBrush AccentBrush,
    IBrush SurfaceBrush,
    string StatusType);

public sealed record DiffFileEntryViewModel(
    string Path,
    string Kind,
    string Summary,
    string Diff);

public sealed record ConversationItemViewModel(
    string ItemId,
    string TurnId,
    string Kind,
    string Role,
    string Title,
    string Body,
    string Meta,
    string Badge,
    string? CodePreviewLabel,
    string? CodePreview,
    string? OutputPreviewLabel,
    string? OutputPreview,
    string DocumentKind,
    string DocumentName,
    string DocumentMeta,
    string DocumentText,
    IBrush AccentBrush,
    IBrush SurfaceBrush,
    IBrush ForegroundBrush,
    IReadOnlyList<DiffFileEntryViewModel>? FileDiffs = null)
{
    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public bool HasCodePreview => !string.IsNullOrWhiteSpace(CodePreview);

    public bool HasOutputPreview => !string.IsNullOrWhiteSpace(OutputPreview);

    public bool HasFileDiffs => FileDiffs is { Count: > 0 };
}

public sealed record PendingInteractionOptionViewModel(string Label, string Description, IRelayCommand SelectCommand);

public sealed partial class PendingInteractionQuestionViewModel : ObservableObject
{
    public PendingInteractionQuestionViewModel(
        string id,
        string header,
        string question,
        bool isSecret,
        IEnumerable<ToolRequestUserInputOption>? options)
    {
        Id = id;
        Header = header;
        Question = question;
        IsSecret = isSecret;
        Options = new ObservableCollection<PendingInteractionOptionViewModel>();

        foreach (var option in options ?? Array.Empty<ToolRequestUserInputOption>())
        {
            var label = option.Label ?? string.Empty;
            Options.Add(new PendingInteractionOptionViewModel(
                label,
                option.Description ?? string.Empty,
                new RelayCommand(() => AnswerText = label)));
        }
    }

    public string Id { get; }

    public string Header { get; }

    public string Question { get; }

    public bool IsSecret { get; }

    public ObservableCollection<PendingInteractionOptionViewModel> Options { get; }

    [ObservableProperty]
    private string answerText = string.Empty;

    public bool HasOptions => Options.Count > 0;
}

public sealed partial class PendingInteractionViewModel : ObservableObject
{
    private readonly Action<AppServerServerRequestCompletion> _completeAction;
    private bool _isCompleted;

    public PendingInteractionViewModel(
        string requestKey,
        string method,
        string title,
        string detail,
        string meta,
        string badge,
        string? commandPreview,
        string? responsePlaceholder,
        bool showAcceptForSession,
        IEnumerable<PendingInteractionQuestionViewModel>? questions,
        IReadOnlyList<string>? proposedExecpolicyAmendment,
        NetworkPolicyAmendment? proposedNetworkPolicyAmendment,
        IBrush accentBrush,
        IBrush surfaceBrush,
        Action<AppServerServerRequestCompletion> completeAction)
    {
        RequestKey = requestKey;
        Method = method;
        Title = title;
        Detail = detail;
        Meta = meta;
        Badge = badge;
        CommandPreview = commandPreview;
        ResponsePlaceholder = responsePlaceholder;
        ShowAcceptForSession = showAcceptForSession;
        ProposedExecpolicyAmendment = proposedExecpolicyAmendment;
        ProposedNetworkPolicyAmendment = proposedNetworkPolicyAmendment;
        AccentBrush = accentBrush;
        SurfaceBrush = surfaceBrush;
        _completeAction = completeAction;

        Questions = new ObservableCollection<PendingInteractionQuestionViewModel>(questions ?? Array.Empty<PendingInteractionQuestionViewModel>());

        AcceptCommand = new RelayCommand(() => Complete(AppServerServerRequestCompletion.FromResult(new { decision = "accept" })));
        AcceptForSessionCommand = new RelayCommand(() => Complete(AppServerServerRequestCompletion.FromResult(new { decision = "acceptForSession" })));
        DeclineCommand = new RelayCommand(() => Complete(AppServerServerRequestCompletion.FromResult(new { decision = "decline" })));
        CancelCommand = new RelayCommand(() => Complete(AppServerServerRequestCompletion.FromResult(new { decision = "cancel" })));
        ApplyExecPolicyCommand = new RelayCommand(ApplyExecPolicy, CanApplyExecPolicy);
        ApplyNetworkPolicyCommand = new RelayCommand(ApplyNetworkPolicy, CanApplyNetworkPolicy);
        SubmitAnswersCommand = new RelayCommand(SubmitAnswers, CanSubmitAnswers);
        SubmitSuccessResultCommand = new RelayCommand(() => SubmitToolResult(true), CanSubmitToolResult);
        SubmitFailureResultCommand = new RelayCommand(() => SubmitToolResult(false), CanSubmitToolResult);
    }

    public string RequestKey { get; }

    public string Method { get; }

    public string Title { get; }

    public string Detail { get; }

    public string Meta { get; }

    public string Badge { get; }

    public string? CommandPreview { get; }

    public string? ResponsePlaceholder { get; }

    public bool ShowAcceptForSession { get; }

    public IReadOnlyList<string>? ProposedExecpolicyAmendment { get; }

    public NetworkPolicyAmendment? ProposedNetworkPolicyAmendment { get; }

    public ObservableCollection<PendingInteractionQuestionViewModel> Questions { get; }

    public IBrush AccentBrush { get; }

    public IBrush SurfaceBrush { get; }

    public IRelayCommand AcceptCommand { get; }

    public IRelayCommand AcceptForSessionCommand { get; }

    public IRelayCommand DeclineCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ApplyExecPolicyCommand { get; }

    public IRelayCommand ApplyNetworkPolicyCommand { get; }

    public IRelayCommand SubmitAnswersCommand { get; }

    public IRelayCommand SubmitSuccessResultCommand { get; }

    public IRelayCommand SubmitFailureResultCommand { get; }

    [ObservableProperty]
    private string responseText = string.Empty;

    partial void OnResponseTextChanged(string value)
    {
        SubmitSuccessResultCommand.NotifyCanExecuteChanged();
        SubmitFailureResultCommand.NotifyCanExecuteChanged();
    }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);

    public bool HasCommandPreview => !string.IsNullOrWhiteSpace(CommandPreview);

    public bool HasQuestions => Questions.Count > 0;

    public bool HasToolResponseBox => Method is "item/tool/call" or "account/chatgptAuthTokens/refresh";

    public bool HasToolFailureResult => Method == "item/tool/call";

    public bool HasApprovalButtons => Method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval";

    public string SubmitSuccessButtonLabel => Method == "account/chatgptAuthTokens/refresh" ? "Submit Tokens" : "Return Success";

    public string SubmitFailureButtonLabel => Method == "account/chatgptAuthTokens/refresh" ? "Reject Refresh" : "Return Failure";

    public bool HasExecPolicyDecision => ProposedExecpolicyAmendment is { Count: > 0 };

    public bool HasNetworkPolicyDecision => ProposedNetworkPolicyAmendment is not null;

    private bool CanApplyExecPolicy() => !_isCompleted && ProposedExecpolicyAmendment is { Count: > 0 };

    private void ApplyExecPolicy()
    {
        if (ProposedExecpolicyAmendment is null)
        {
            return;
        }

        Complete(AppServerServerRequestCompletion.FromResult(new
        {
            decision = new
            {
                acceptWithExecpolicyAmendment = new
                {
                    execpolicy_amendment = ProposedExecpolicyAmendment
                }
            }
        }));
    }

    private bool CanApplyNetworkPolicy() => !_isCompleted && ProposedNetworkPolicyAmendment is not null;

    private void ApplyNetworkPolicy()
    {
        if (ProposedNetworkPolicyAmendment is null)
        {
            return;
        }

        Complete(AppServerServerRequestCompletion.FromResult(new
        {
            decision = new
            {
                applyNetworkPolicyAmendment = new
                {
                    network_policy_amendment = new
                    {
                        action = ProposedNetworkPolicyAmendment.Action,
                        host = ProposedNetworkPolicyAmendment.Host
                    }
                }
            }
        }));
    }

    private bool CanSubmitAnswers() => !_isCompleted && HasQuestions;

    private void SubmitAnswers()
    {
        var answers = Questions.ToDictionary(
            static question => question.Id,
            static question => new
            {
                answers = SplitAnswers(question.AnswerText)
            });

        Complete(AppServerServerRequestCompletion.FromResult(new { answers }));
    }

    private bool CanSubmitToolResult() => !_isCompleted && !string.IsNullOrWhiteSpace(ResponseText);

    private void SubmitToolResult(bool success)
    {
        if (Method == "account/chatgptAuthTokens/refresh")
        {
            if (!success)
            {
                Complete(AppServerServerRequestCompletion.FromError(-32000, "User rejected ChatGPT token refresh."));
                return;
            }

            var responseText = ResponseText.Trim();
            if (TryParseJsonResponse(responseText, out var parsedJson))
            {
                Complete(AppServerServerRequestCompletion.FromResult(parsedJson));
                return;
            }

            Complete(AppServerServerRequestCompletion.FromResult(new
            {
                accessToken = responseText
            }));
            return;
        }

        var contentItems = string.IsNullOrWhiteSpace(ResponseText)
            ? Array.Empty<object>()
            : new object[]
            {
                new
                {
                    type = "inputText",
                    text = ResponseText.Trim()
                }
            };

        Complete(AppServerServerRequestCompletion.FromResult(new
        {
            contentItems,
            success
        }));
    }

    private void Complete(AppServerServerRequestCompletion completion)
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        _completeAction(completion);
    }

    private static bool TryParseJsonResponse(string value, out JsonElement jsonElement)
    {
        jsonElement = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            jsonElement = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string[] SplitAnswers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var separators = value.Contains('\n', StringComparison.Ordinal)
            ? new[] { '\n' }
            : new[] { ',' };

        return value
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static answer => !string.IsNullOrWhiteSpace(answer))
            .ToArray();
    }
}

public static class ShellBrushes
{
    public static readonly IBrush TextPrimary = Create("#171717");
    public static readonly IBrush TextSecondary = Create("#454545");
    public static readonly IBrush TextMuted = Create("#707070");
    public static readonly IBrush Green = Create("#1F7A3D");
    public static readonly IBrush GreenSoft = Create("#EAF6EE");
    public static readonly IBrush Blue = Create("#0A56C2");
    public static readonly IBrush BlueSoft = Create("#EAF1FE");
    public static readonly IBrush Amber = Create("#B06A10");
    public static readonly IBrush AmberSoft = Create("#FFF4E8");
    public static readonly IBrush Red = Create("#B42318");
    public static readonly IBrush RedSoft = Create("#FDEDEC");
    public static readonly IBrush Neutral = Create("#636363");
    public static readonly IBrush NeutralSoft = Create("#EFEFEF");
    public static readonly IBrush Paper = Create("#FFFFFF");
    public static readonly IBrush PaperMuted = Create("#F8F8F8");
    public static readonly IBrush EditorSurface = Create("#F6F6F6");
    public static readonly IBrush EditorHeaderSurface = Create("#EFEFEF");
    public static readonly IBrush LineNumber = Create("#848484");
    public static readonly IBrush DiffAddSurface = Create("#EAF6EE");
    public static readonly IBrush DiffRemoveSurface = Create("#FDEDEC");
    public static readonly IBrush DiffMetaSurface = Create("#F3F3F3");
    public static readonly IBrush CommandSurface = Create("#EEF3FD");
    public static readonly IBrush ToolSurface = Create("#FFF4E8");

    private static IBrush Create(string color) => new SolidColorBrush(Color.Parse(color));
}
