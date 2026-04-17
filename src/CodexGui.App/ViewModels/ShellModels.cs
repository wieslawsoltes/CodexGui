using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodexGui.AppServer.Models;

namespace CodexGui.App.ViewModels;

public enum ShellTone
{
    TextPrimary,
    TextSecondary,
    TextMuted,
    Green,
    GreenSoft,
    Blue,
    BlueSoft,
    Amber,
    AmberSoft,
    Red,
    RedSoft,
    Neutral,
    NeutralSoft,
    Paper,
    PaperMuted,
    EditorSurface,
    EditorHeaderSurface,
    LineNumber,
    DiffAddSurface,
    DiffRemoveSurface,
    DiffMetaSurface,
    CommandSurface,
    ToolSurface
}

public static class ShellTones
{
    public const ShellTone TextPrimary = ShellTone.TextPrimary;
    public const ShellTone TextSecondary = ShellTone.TextSecondary;
    public const ShellTone TextMuted = ShellTone.TextMuted;
    public const ShellTone Green = ShellTone.Green;
    public const ShellTone GreenSoft = ShellTone.GreenSoft;
    public const ShellTone Blue = ShellTone.Blue;
    public const ShellTone BlueSoft = ShellTone.BlueSoft;
    public const ShellTone Amber = ShellTone.Amber;
    public const ShellTone AmberSoft = ShellTone.AmberSoft;
    public const ShellTone Red = ShellTone.Red;
    public const ShellTone RedSoft = ShellTone.RedSoft;
    public const ShellTone Neutral = ShellTone.Neutral;
    public const ShellTone NeutralSoft = ShellTone.NeutralSoft;
    public const ShellTone Paper = ShellTone.Paper;
    public const ShellTone PaperMuted = ShellTone.PaperMuted;
    public const ShellTone EditorSurface = ShellTone.EditorSurface;
    public const ShellTone EditorHeaderSurface = ShellTone.EditorHeaderSurface;
    public const ShellTone LineNumber = ShellTone.LineNumber;
    public const ShellTone DiffAddSurface = ShellTone.DiffAddSurface;
    public const ShellTone DiffRemoveSurface = ShellTone.DiffRemoveSurface;
    public const ShellTone DiffMetaSurface = ShellTone.DiffMetaSurface;
    public const ShellTone CommandSurface = ShellTone.CommandSurface;
    public const ShellTone ToolSurface = ShellTone.ToolSurface;
}

public sealed record StatusChipViewModel(string Label, string Value, ShellTone AccentTone, ShellTone SurfaceTone);

public sealed record ThreadListEntryViewModel(
    string Id,
    string Title,
    string Preview,
    string TimeLabel,
    string Meta,
    string Badge,
    ShellTone AccentTone,
    ShellTone SurfaceTone,
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
    ShellTone AccentTone,
    ShellTone SurfaceTone,
    ShellTone ForegroundTone,
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
        ShellTone accentTone,
        ShellTone surfaceTone,
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
        AccentTone = accentTone;
        SurfaceTone = surfaceTone;
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

    public ShellTone AccentTone { get; }

    public ShellTone SurfaceTone { get; }

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
