using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CodexGui.Markdown.Services;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CodexGui.Markdown.Controls;

public sealed class MarkdownTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string?>(nameof(Markdown));

    public static readonly StyledProperty<Uri?> BaseUriProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, Uri?>(nameof(BaseUri));

    public static readonly StyledProperty<bool> IsEditingEnabledProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, bool>(nameof(IsEditingEnabled));

    public static readonly StyledProperty<MarkdownEditorPresentationMode> EditorPresentationModeProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, MarkdownEditorPresentationMode>(
            nameof(EditorPresentationMode),
            defaultValue: MarkdownEditorPresentationMode.Inline);

    private const double LinkClickDragThreshold = 4d;
    private static readonly Cursor LinkCursor = new(StandardCursorType.Hand);
    private readonly MarkdownKeyboardShortcutService _keyboardShortcutService = new();
    private readonly MarkdownCommandPaletteService _commandPaletteService = new();
    private readonly MarkdownStructuralEditingService _structuralEditingService = new();
    private readonly MarkdownOutlineService _outlineService = new();
    private readonly MarkdownSelectionOverlayHost _selectionOverlayHost = new();
    private IMarkdownRenderController _renderController = MarkdownRenderingServices.DefaultController;
    private IMarkdownHitTestingService _hitTestingService = MarkdownRenderingServices.DefaultHitTestingService;
    private IMarkdownEditingService _editingService = MarkdownRenderingServices.DefaultEditingService;
    private MarkdownEditorPreferences _editorPreferences = new();
    private MarkdownRenderResourceTracker? _renderResources;
    private MarkdownRenderResult? _lastRenderResult;
    private MarkdownEditorSession? _activeEditorSession;
    private MarkdownHitTestResult? _selectedHitTestResult;
    private MarkdownOutlineSnapshot _outlineSnapshot = MarkdownOutlineSnapshot.Empty;
    private MarkdownSourceSpan? _selectedSourceSpan;
    private readonly MarkdownEditHistory _editHistory = new();
    private int _renderGeneration;
    private double _lastMeasuredWidth = double.NaN;
    private double _effectiveViewportWidth = double.NaN;
    private Point? _pendingLinkPointerOrigin;
    private Uri? _pendingLinkUri;
    private bool _isApplyingInternalMarkdownChange;

    static MarkdownTextBlock()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.HandleMarkdownChanged());
        BaseUriProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        IsEditingEnabledProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.HandleEditingEnabledChanged());
        EditorPresentationModeProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        FontSizeProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        ForegroundProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        TextWrappingProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        BoundsProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.HandleBoundsChanged());
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public Uri? BaseUri
    {
        get => GetValue(BaseUriProperty);
        set => SetValue(BaseUriProperty, value);
    }

    public bool IsEditingEnabled
    {
        get => GetValue(IsEditingEnabledProperty);
        set => SetValue(IsEditingEnabledProperty, value);
    }

    public MarkdownEditorPresentationMode EditorPresentationMode
    {
        get => GetValue(EditorPresentationModeProperty);
        set => SetValue(EditorPresentationModeProperty, value);
    }

    public IMarkdownRenderController RenderController
    {
        get => _renderController;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_renderController, value))
            {
                return;
            }

            _renderController = value;
            RebuildMarkdown();
        }
    }

    public IMarkdownHitTestingService HitTestingService
    {
        get => _hitTestingService;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _hitTestingService = value;
        }
    }

    public IMarkdownEditingService EditingService
    {
        get => _editingService;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_editingService, value))
            {
                return;
            }

            _editingService = value;
            RebuildMarkdown();
        }
    }

    public MarkdownEditorPreferences EditorPreferences
    {
        get => _editorPreferences;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _editorPreferences = value;
            RebuildMarkdown();
        }
    }

    public MarkdownRenderResult? LastRenderResult => _lastRenderResult;

    public MarkdownParseResult? LastParseResult => _lastRenderResult?.ParseResult;

    public MarkdownRenderMap? LastRenderMap => _lastRenderResult?.RenderMap;

    public IReadOnlyList<MarkdownRenderDiagnostic> LastRenderDiagnostics => _lastRenderResult?.Diagnostics ?? Array.Empty<MarkdownRenderDiagnostic>();

    public MarkdownEditorSession? ActiveEditorSession => _activeEditorSession;

    public MarkdownHitTestResult? SelectedMarkdownElement => _selectedHitTestResult;

    public IReadOnlyList<MarkdownOutlineItem> Outline => _outlineSnapshot.Items;

    public IReadOnlyList<MarkdownOutlineItem> Breadcrumbs => _outlineSnapshot.Breadcrumbs;

    public MarkdownEditHistorySnapshot EditHistory => _editHistory.GetSnapshot();

    public bool CanUndoEdit => EditHistory.CanUndo;

    public bool CanRedoEdit => EditHistory.CanRedo;

    public event EventHandler<MarkdownEditedEventArgs>? MarkdownEdited;

    public event EventHandler<MarkdownEditCanceledEventArgs>? MarkdownEditCanceled;

    public event EventHandler<MarkdownSelectionChangedEventArgs>? MarkdownSelectionChanged;

    public event EventHandler? OutlineChanged;

    public event EventHandler? BreadcrumbsChanged;

    public MarkdownTextBlock()
    {
        Focusable = true;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        RebuildMarkdown();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        RebuildMarkdown();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        _effectiveViewportWidth = double.NaN;
        _lastRenderResult = null;
        _activeEditorSession = null;
        _selectedHitTestResult = null;
        _selectedSourceSpan = null;
        _outlineSnapshot = MarkdownOutlineSnapshot.Empty;
        _commandPaletteService.Hide(suppressDismiss: true);
        _selectionOverlayHost.Hide();
        ClearPendingLinkInteraction();
        ClearValue(CursorProperty);
        DisposeRenderResources();
    }

    private void HandleMarkdownChanged()
    {
        if (_activeEditorSession is not null)
        {
            CancelEdit(MarkdownEditCancellationReason.MarkdownChanged);
            return;
        }

        if (!_isApplyingInternalMarkdownChange)
        {
            _editHistory.Clear();
        }

        RebuildMarkdown();
    }

    private void HandleEditingEnabledChanged()
    {
        if (!IsEditingEnabled && _activeEditorSession is not null)
        {
            CancelEdit(MarkdownEditCancellationReason.EditingDisabled);
            return;
        }

        if (!IsEditingEnabled)
        {
            _commandPaletteService.Hide(suppressDismiss: true);
        }

        RebuildMarkdown();
    }

    private void HandleBoundsChanged()
    {
        if (Bounds.Width <= 0 && ResolveViewportWidth() <= 0)
        {
            return;
        }

        HandleAvailableWidthChanged(ResolveAvailableWidth());
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs eventArgs)
    {
        if (eventArgs.EffectiveViewport.Width <= 0)
        {
            return;
        }

        _effectiveViewportWidth = eventArgs.EffectiveViewport.Width;
        HandleAvailableWidthChanged(ResolveAvailableWidth());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (TextWrapping == TextWrapping.NoWrap)
        {
            return base.MeasureOverride(availableSize);
        }

        var constrainedWidth = ResolveAvailableWidth(availableSize.Width);
        if (constrainedWidth > 0 &&
            (double.IsInfinity(availableSize.Width) || constrainedWidth < availableSize.Width))
        {
            availableSize = new Size(constrainedWidth, availableSize.Height);
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (ShouldBypassSelectionPointerHandling())
        {
            return;
        }

        Focus();

        var point = e.GetPosition(this);
        var hit = HitTestMarkdown(point);
        if (hit is not null)
        {
            UpdateSelection(hit);
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            TryResolveLinkTarget(hit, out var navigateUri) &&
            navigateUri is not null)
        {
            _pendingLinkPointerOrigin = point;
            _pendingLinkUri = navigateUri;
        }
        else
        {
            ClearPendingLinkInteraction();
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (ShouldBypassSelectionPointerHandling())
        {
            return;
        }

        var point = e.GetPosition(this);
        UpdateLinkCursor(point);

        if (_pendingLinkPointerOrigin is { } pressedPoint &&
            HasExceededLinkClickDragThreshold(pressedPoint, point))
        {
            ClearPendingLinkInteraction();
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (ShouldBypassSelectionPointerHandling())
        {
            return;
        }

        var point = e.GetPosition(this);
        var pendingLinkUri = _pendingLinkUri;
        var pendingLinkPointerOrigin = _pendingLinkPointerOrigin;

        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton == MouseButton.Left &&
            pendingLinkUri is not null &&
            pendingLinkPointerOrigin is { } pressedPoint &&
            !HasExceededLinkClickDragThreshold(pressedPoint, point) &&
            TryResolveLinkTarget(point, out var releasedLinkUri) &&
            releasedLinkUri is not null &&
            Uri.Compare(
                pendingLinkUri,
                releasedLinkUri,
                UriComponents.AbsoluteUri,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            e.Handled = true;
            _ = LaunchUriAsync(releasedLinkUri);
        }

        ClearPendingLinkInteraction();
        UpdateLinkCursor(point);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        ClearPendingLinkInteraction();
        ClearValue(CursorProperty);
        base.OnPointerExited(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_commandPaletteService.IsOpen && e.Key == Key.Escape)
        {
            _commandPaletteService.Hide();
            e.Handled = true;
            return;
        }

        if (HandleEditHistoryShortcut(e))
        {
            return;
        }

        var action = _keyboardShortcutService.Resolve(e, new MarkdownKeyboardShortcutContext(_activeEditorSession is not null));
        if (TryHandleKeyboardAction(action))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void HandleAvailableWidthChanged(double width)
    {
        if (width <= 0 || TextWrapping == TextWrapping.NoWrap)
        {
            return;
        }

        if (double.IsNaN(_lastMeasuredWidth) || Math.Abs(_lastMeasuredWidth - width) > 1)
        {
            _lastMeasuredWidth = width;
            InvalidateMeasure();
            RebuildMarkdown();
        }
    }

    private bool ShouldBypassSelectionPointerHandling()
    {
        // While an inline editor is active, let embedded controls own pointer input instead of
        // letting SelectableTextBlock enter text-selection mode and interfere with popups.
        return _activeEditorSession is not null || _commandPaletteService.IsOpen;
    }

    private void UpdateLinkCursor(Point point)
    {
        if (TryResolveLinkTarget(point, out _))
        {
            SetCurrentValue(CursorProperty, LinkCursor);
        }
        else
        {
            ClearValue(CursorProperty);
        }
    }

    private bool TryResolveLinkTarget(Point point, out Uri? navigateUri)
    {
        return TryResolveLinkTarget(HitTestMarkdown(point), out navigateUri);
    }

    private bool TryResolveLinkTarget(MarkdownHitTestResult? hitTestResult, out Uri? navigateUri)
    {
        navigateUri = null;

        if (hitTestResult is null)
        {
            return false;
        }

        switch (hitTestResult.AstNode.Node)
        {
            case LinkInline { IsImage: false } linkInline:
                return MarkdownUriUtilities.TryResolveUri(BaseUri, linkInline.GetDynamicUrl?.Invoke() ?? linkInline.Url, out navigateUri);
            case AutolinkInline autolinkInline:
                var navigateUrl = autolinkInline.IsEmail
                    ? $"mailto:{autolinkInline.Url}"
                    : autolinkInline.Url;
                return MarkdownUriUtilities.TryResolveUri(BaseUri, navigateUrl, out navigateUri);
            default:
                return false;
        }
    }

    private Task LaunchUriAsync(Uri navigateUri)
    {
        return TopLevel.GetTopLevel(this) is { } topLevel
            ? topLevel.Launcher.LaunchUriAsync(navigateUri)
            : Task.CompletedTask;
    }

    private void ClearPendingLinkInteraction()
    {
        _pendingLinkPointerOrigin = null;
        _pendingLinkUri = null;
    }

    private static bool HasExceededLinkClickDragThreshold(Point origin, Point current)
    {
        var deltaX = current.X - origin.X;
        var deltaY = current.Y - origin.Y;
        var thresholdSquared = LinkClickDragThreshold * LinkClickDragThreshold;
        return ((deltaX * deltaX) + (deltaY * deltaY)) > thresholdSquared;
    }

    private void RebuildMarkdown()
    {
        ClearPendingLinkInteraction();
        ClearValue(CursorProperty);
        var previousResources = _renderResources;
        var resourceTracker = new MarkdownRenderResourceTracker();
        var cancellationScope = new MarkdownRenderCancellationScope();
        resourceTracker.Track(cancellationScope);
        var diagnostics = new MarkdownRenderDiagnostics();
        var renderGeneration = unchecked(++_renderGeneration);

        if (string.IsNullOrWhiteSpace(Markdown))
        {
            _lastRenderResult = MarkdownRenderResult.Empty(resourceTracker);
            _renderResources = resourceTracker;
            Text = string.Empty;
            Inlines = _lastRenderResult.Inlines;
            previousResources?.Dispose();
            RefreshInteractionStateAfterRender();
            return;
        }

        var request = new MarkdownRenderRequest
        {
            Markdown = Markdown,
            Context = new MarkdownRenderContext
            {
                BaseUri = BaseUri,
                RenderController = RenderController,
                FontSize = FontSize,
                FontFamily = FontFamily,
                Foreground = Foreground,
                TextWrapping = TextWrapping,
                AvailableWidth = ResolveAvailableWidth(),
                RenderGeneration = renderGeneration,
                ResourceTracker = resourceTracker,
                IsCurrentRender = IsCurrentRender,
                CancellationToken = cancellationScope.Token,
                Diagnostics = diagnostics,
                EditorState = CreateEditorState()
            }
        };

        var result = RenderController.Render(request);
        _lastRenderResult = result;
        Inlines = result.Inlines;
        Text = null;
        _renderResources = result.ResourceTracker;
        previousResources?.Dispose();
        RefreshInteractionStateAfterRender();
    }

    public MarkdownHitTestResult? HitTestMarkdown(Point point)
    {
        if (_lastRenderResult is null)
        {
            return null;
        }

        return HitTestingService.HitTest(new MarkdownHitTestRequest
        {
            Host = this,
            Point = point,
            TextLayout = TextLayout,
            Padding = Padding,
            RenderResult = _lastRenderResult
        });
    }

    public bool TryBeginEdit(Point point)
    {
        return HitTestMarkdown(point) is { } hit && TryBeginEdit(hit);
    }

    public bool TryBeginEdit(MarkdownHitTestResult hitTestResult)
    {
        ArgumentNullException.ThrowIfNull(hitTestResult);

        if (!IsEditingEnabled)
        {
            return false;
        }

        var session = EditingService.ResolveSession(new MarkdownEditorResolveRequest
        {
            HitTestResult = hitTestResult,
            Preferences = EditorPreferences
        });

        if (session is null)
        {
            return false;
        }

        _activeEditorSession = session;
        SetSelectionCore(hitTestResult, raiseEvent: true);
        _commandPaletteService.Hide(suppressDismiss: true);
        RebuildMarkdown();
        return true;
    }

    public void CancelEdit()
    {
        CancelEdit(MarkdownEditCancellationReason.UserRequested);
    }

    private void CancelEdit(MarkdownEditCancellationReason reason)
    {
        if (_activeEditorSession is null)
        {
            return;
        }

        var session = _activeEditorSession;
        _activeEditorSession = null;
        _commandPaletteService.Hide(suppressDismiss: true);
        RebuildMarkdown();
        MarkdownEditCanceled?.Invoke(this, new MarkdownEditCanceledEventArgs(session, reason));
    }

    private bool IsCurrentRender(int renderGeneration)
    {
        return renderGeneration == _renderGeneration;
    }

    private MarkdownEditorState? CreateEditorState()
    {
        if (!IsEditingEnabled)
        {
            return null;
        }

        return new MarkdownEditorState
        {
            EditingService = EditingService,
            Preferences = EditorPreferences.Clone(),
            ActiveSession = _activeEditorSession,
            PresentationMode = EditorPresentationMode,
            CommitReplacement = CommitEditorReplacement,
            InsertBlockBefore = InsertEditorBlockBefore,
            InsertBlockAfter = InsertEditorBlockAfter,
            RemoveBlock = RemoveEditorBlock,
            CancelEdit = CancelEdit
        };
    }

    private bool TryHandleKeyboardAction(MarkdownKeyboardShortcutAction action)
    {
        switch (action)
        {
            case MarkdownKeyboardShortcutAction.BeginEdit:
                return TryBeginEditCurrentSelection();
            case MarkdownKeyboardShortcutAction.OpenSlashCommands:
                return TryOpenSlashCommandPalette();
            case MarkdownKeyboardShortcutAction.SelectPreviousBlock:
                return TrySelectAdjacentBlock(-1);
            case MarkdownKeyboardShortcutAction.SelectNextBlock:
                return TrySelectAdjacentBlock(1);
            case MarkdownKeyboardShortcutAction.MoveBlockUp:
            case MarkdownKeyboardShortcutAction.MoveBlockDown:
            case MarkdownKeyboardShortcutAction.DuplicateBlock:
            case MarkdownKeyboardShortcutAction.DeleteBlock:
            case MarkdownKeyboardShortcutAction.PromoteBlock:
            case MarkdownKeyboardShortcutAction.DemoteBlock:
            case MarkdownKeyboardShortcutAction.SplitBlock:
            case MarkdownKeyboardShortcutAction.JoinBlock:
                return TryApplyStructuralAction(action);
            case MarkdownKeyboardShortcutAction.CancelInteraction:
                if (_commandPaletteService.IsOpen)
                {
                    _commandPaletteService.Hide();
                    return true;
                }

                if (_activeEditorSession is not null)
                {
                    CancelEdit();
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    private bool TryBeginEditCurrentSelection()
    {
        if (_selectedHitTestResult is { } selectedHit)
        {
            return TryBeginEdit(selectedHit);
        }

        var sessions = GetNavigableSessions();
        return sessions.Count > 0 &&
               TrySelectSourceSpan(sessions[0].SourceSpan) &&
               _selectedHitTestResult is { } firstHit &&
               TryBeginEdit(firstHit);
    }

    private bool TryOpenSlashCommandPalette()
    {
        if (!IsEditingEnabled)
        {
            return false;
        }

        if (!TryResolveInteractionTarget(out var session, out var hit) || _lastRenderResult is null)
        {
            return false;
        }

        if (!_structuralEditingService.TryFindNodeContainingSpan(_lastRenderResult.ParseResult, session.SourceSpan, out var node) ||
            node is null)
        {
            node = hit.AstNode.Node;
        }

        var templates = ResolveBlockTemplates(session, node);
        if (templates.Count == 0)
        {
            return false;
        }

        _commandPaletteService.Show(new MarkdownCommandPaletteRequest
        {
            PlacementTarget = this,
            PlacementRect = ResolvePlacementRect(hit),
            Templates = templates,
            Commit = selection => ApplySlashCommandSelection(session, selection),
            ReturnFocusTarget = this,
            Title = "Insert markdown block",
            Subtitle = "Choose a registered block template and decide where to place it."
        });

        return true;
    }

    private void ApplySlashCommandSelection(MarkdownEditorSession session, MarkdownCommandPaletteSelection selection)
    {
        if (_lastRenderResult is null)
        {
            return;
        }

        var currentMarkdown = Markdown ?? string.Empty;
        var sourceText = session.SourceSpan.Slice(currentMarkdown);
        var mode = selection.CommitMode;
        if (mode == MarkdownCommandPaletteCommitMode.InsertAfter &&
            string.IsNullOrWhiteSpace(MarkdownSourceEditing.NormalizeBlockText(sourceText)))
        {
            mode = MarkdownCommandPaletteCommitMode.Replace;
        }

        if (!_structuralEditingService.TryApplyTemplate(
                _lastRenderResult.ParseResult,
                currentMarkdown,
                session.SourceSpan,
                selection.Template,
                mode,
                out var result) ||
            result is null)
        {
            return;
        }

        ApplyStructuralEditResult(session, result);
    }

    private bool TryApplyStructuralAction(MarkdownKeyboardShortcutAction action)
    {
        if (!IsEditingEnabled)
        {
            return false;
        }

        if (!TryResolveInteractionTarget(out var session, out _) || _lastRenderResult is null)
        {
            return false;
        }

        var markdown = Markdown ?? string.Empty;
        MarkdownStructuralEditResult? result = null;

        switch (action)
        {
            case MarkdownKeyboardShortcutAction.MoveBlockUp:
                _ = _structuralEditingService.TryMoveBlock(_lastRenderResult.ParseResult, markdown, session.SourceSpan, -1, out result);
                break;
            case MarkdownKeyboardShortcutAction.MoveBlockDown:
                _ = _structuralEditingService.TryMoveBlock(_lastRenderResult.ParseResult, markdown, session.SourceSpan, 1, out result);
                break;
            case MarkdownKeyboardShortcutAction.DuplicateBlock:
                _ = _structuralEditingService.TryDuplicateBlock(_lastRenderResult.ParseResult, markdown, session.SourceSpan, out result);
                break;
            case MarkdownKeyboardShortcutAction.DeleteBlock:
                _ = _structuralEditingService.TryDeleteBlock(_lastRenderResult.ParseResult, markdown, session.SourceSpan, out result);
                break;
            case MarkdownKeyboardShortcutAction.PromoteBlock:
                _ = _structuralEditingService.TryPromoteBlock(_lastRenderResult.ParseResult, markdown, session.SourceSpan, out result);
                break;
            case MarkdownKeyboardShortcutAction.DemoteBlock:
                _ = _structuralEditingService.TryDemoteBlock(_lastRenderResult.ParseResult, markdown, session.SourceSpan, out result);
                break;
            case MarkdownKeyboardShortcutAction.SplitBlock:
                var activeTextBox = ResolveActiveEditorTextBox();
                if (activeTextBox is null)
                {
                    return false;
                }

                _ = _structuralEditingService.TrySplitBlock(
                    _lastRenderResult.ParseResult,
                    markdown,
                    session,
                    activeTextBox.Text ?? string.Empty,
                    activeTextBox.CaretIndex,
                    out result);
                break;
            case MarkdownKeyboardShortcutAction.JoinBlock:
                _ = _structuralEditingService.TryJoinWithNextParagraph(_lastRenderResult.ParseResult, markdown, session.SourceSpan, out result);
                break;
        }

        if (result is null)
        {
            return false;
        }

        ApplyStructuralEditResult(session, result);
        return true;
    }

    private void ApplyStructuralEditResult(MarkdownEditorSession session, MarkdownStructuralEditResult result)
    {
        var currentMarkdown = Markdown ?? string.Empty;
        _commandPaletteService.Hide(suppressDismiss: true);

        CompleteEditorChange(
            session,
            currentMarkdown,
            result.UpdatedMarkdown,
            result.ReplacementMarkdown,
            result.RevealStart,
            result.RevealLength,
            MapStructuralKind(result.Kind));
    }

    private static MarkdownEditOperation MapStructuralKind(MarkdownStructuralEditKind kind)
    {
        return kind switch
        {
            MarkdownStructuralEditKind.InsertBefore => MarkdownEditOperation.InsertBlockBefore,
            MarkdownStructuralEditKind.InsertAfter => MarkdownEditOperation.InsertBlockAfter,
            MarkdownStructuralEditKind.Remove => MarkdownEditOperation.RemoveBlock,
            MarkdownStructuralEditKind.Duplicate => MarkdownEditOperation.DuplicateBlock,
            MarkdownStructuralEditKind.MoveUp => MarkdownEditOperation.MoveBlockUp,
            MarkdownStructuralEditKind.MoveDown => MarkdownEditOperation.MoveBlockDown,
            MarkdownStructuralEditKind.Promote => MarkdownEditOperation.PromoteBlock,
            MarkdownStructuralEditKind.Demote => MarkdownEditOperation.DemoteBlock,
            MarkdownStructuralEditKind.Split => MarkdownEditOperation.SplitBlock,
            MarkdownStructuralEditKind.Join => MarkdownEditOperation.JoinBlock,
            _ => MarkdownEditOperation.Replace
        };
    }

    private bool TryResolveInteractionTarget(out MarkdownEditorSession session, out MarkdownHitTestResult hit)
    {
        if (_activeEditorSession is not null &&
            TryCreateHitForSourceSpan(_activeEditorSession.SourceSpan, out var activeHit) &&
            activeHit is not null)
        {
            session = _activeEditorSession;
            hit = activeHit;
            return true;
        }

        if (_selectedHitTestResult is not null)
        {
            var resolvedSession = EditingService.ResolveSession(new MarkdownEditorResolveRequest
            {
                HitTestResult = _selectedHitTestResult,
                Preferences = EditorPreferences
            });

            if (resolvedSession is not null)
            {
                session = resolvedSession;
                if (TryCreateHitForSourceSpan(resolvedSession.SourceSpan, out var resolvedHit) && resolvedHit is not null)
                {
                    hit = resolvedHit;
                }
                else
                {
                    hit = _selectedHitTestResult;
                }

                return true;
            }
        }

        session = null!;
        hit = null!;
        return false;
    }

    private IReadOnlyList<MarkdownEditorSession> GetNavigableSessions()
    {
        if (_lastRenderResult is null)
        {
            return Array.Empty<MarkdownEditorSession>();
        }

        HashSet<MarkdownSourceSpan> uniqueSpans = [];
        List<MarkdownEditorSession> sessions = [];
        foreach (var visualEntry in _lastRenderResult.RenderMap.VisualEntries.Values)
        {
            TryAddNavigableSession(
                new MarkdownHitTestResult(
                    visualEntry.AstNode,
                    visualEntry.ElementKind,
                    _lastRenderResult.ParseResult,
                    visual: visualEntry.Control),
                sessions,
                uniqueSpans);
        }

        foreach (var textEntry in _lastRenderResult.RenderMap.TextEntries)
        {
            TryAddNavigableSession(
                new MarkdownHitTestResult(
                    textEntry.AstNode,
                    textEntry.ElementKind,
                    _lastRenderResult.ParseResult,
                    renderedTextPosition: textEntry.RenderedTextStart),
                sessions,
                uniqueSpans);
        }

        sessions.Sort(static (left, right) => left.SourceSpan.Start.CompareTo(right.SourceSpan.Start));
        return sessions;
    }

    private void TryAddNavigableSession(
        MarkdownHitTestResult hit,
        ICollection<MarkdownEditorSession> sessions,
        ISet<MarkdownSourceSpan> uniqueSpans)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(uniqueSpans);

        var session = EditingService.ResolveSession(new MarkdownEditorResolveRequest
        {
            HitTestResult = hit,
            Preferences = EditorPreferences
        });

        if (session is null || !uniqueSpans.Add(session.SourceSpan))
        {
            return;
        }

        sessions.Add(session);
    }

    private bool TrySelectAdjacentBlock(int direction)
    {
        var sessions = GetNavigableSessions();
        if (sessions.Count == 0)
        {
            return false;
        }

        var currentSpan = _activeEditorSession?.SourceSpan ?? _selectedSourceSpan;
        if (currentSpan is null)
        {
            var fallback = direction > 0 ? sessions[0] : sessions[^1];
            return TrySelectSourceSpan(fallback.SourceSpan);
        }

        var currentIndex = FindSessionIndex(sessions, currentSpan.Value);
        if (currentIndex < 0)
        {
            currentIndex = FindNearestSessionIndex(sessions, currentSpan.Value.Start, direction);
        }

        if (currentIndex < 0)
        {
            currentIndex = direction > 0 ? 0 : sessions.Count - 1;
        }
        else
        {
            currentIndex = Math.Clamp(currentIndex + Math.Sign(direction), 0, sessions.Count - 1);
        }

        return TrySelectSourceSpan(sessions[currentIndex].SourceSpan);
    }

    private static int FindSessionIndex(IReadOnlyList<MarkdownEditorSession> sessions, MarkdownSourceSpan span)
    {
        for (var index = 0; index < sessions.Count; index++)
        {
            if (sessions[index].SourceSpan.Equals(span))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNearestSessionIndex(IReadOnlyList<MarkdownEditorSession> sessions, int sourceOffset, int direction)
    {
        if (direction > 0)
        {
            for (var index = 0; index < sessions.Count; index++)
            {
                if (sessions[index].SourceSpan.Start >= sourceOffset)
                {
                    return index;
                }
            }

            return sessions.Count - 1;
        }

        for (var index = sessions.Count - 1; index >= 0; index--)
        {
            if (sessions[index].SourceSpan.Start <= sourceOffset)
            {
                return index;
            }
        }

        return 0;
    }

    private void RefreshInteractionStateAfterRender()
    {
        RefreshSelectedHitAfterRender();
    }

    private void RefreshSelectedHitAfterRender()
    {
        if (_activeEditorSession is not null &&
            TryCreateHitForSourceSpan(_activeEditorSession.SourceSpan, out var activeHit) &&
            activeHit is not null)
        {
            SetSelectionCore(activeHit, raiseEvent: false);
            return;
        }

        if (_selectedSourceSpan is not { } selectedSourceSpan)
        {
            SetSelectionCore(null, raiseEvent: false);
            return;
        }

        if (TryCreateHitForSourceSpan(selectedSourceSpan, out var selectedHit) && selectedHit is not null)
        {
            SetSelectionCore(selectedHit, raiseEvent: false);
            return;
        }

        SetSelectionCore(null, raiseEvent: false);
    }

    private void UpdateOutlineSnapshot()
    {
        var previousOutline = _outlineSnapshot;
        var activeSpan = _activeEditorSession?.SourceSpan ?? _selectedSourceSpan;
        _outlineSnapshot = _outlineService.BuildSnapshot(_lastRenderResult?.ParseResult, activeSpan);

        if (!OutlineEquals(previousOutline.Items, _outlineSnapshot.Items))
        {
            OutlineChanged?.Invoke(this, EventArgs.Empty);
        }

        if (!OutlineEquals(previousOutline.Breadcrumbs, _outlineSnapshot.Breadcrumbs))
        {
            BreadcrumbsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool OutlineEquals(IReadOnlyList<MarkdownOutlineItem> left, IReadOnlyList<MarkdownOutlineItem> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Title, right[index].Title, StringComparison.Ordinal) ||
                !left[index].SourceSpan.Equals(right[index].SourceSpan))
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<MarkdownBlockTemplate> ResolveBlockTemplates(MarkdownEditorSession session, MarkdownObject node)
    {
        if (_lastRenderResult is null)
        {
            return Array.Empty<MarkdownBlockTemplate>();
        }

        using var tracker = new MarkdownRenderResourceTracker();
        var context = new MarkdownBlockTemplateContext
        {
            Node = node,
            ParseResult = _lastRenderResult.ParseResult,
            RenderContext = new MarkdownRenderContext
            {
                BaseUri = BaseUri,
                RenderController = RenderController,
                FontSize = FontSize,
                FontFamily = FontFamily,
                Foreground = Foreground,
                TextWrapping = this.TextWrapping,
                AvailableWidth = ResolveAvailableWidth(),
                RenderGeneration = _renderGeneration,
                ResourceTracker = tracker,
                IsCurrentRender = IsCurrentRender,
                CancellationToken = CancellationToken.None,
                Diagnostics = new MarkdownRenderDiagnostics(),
                EditorState = CreateEditorState()
            },
            Session = session,
            Preferences = EditorPreferences.Clone()
        };

        var templates = EditingService is MarkdownEditingService editingService
            ? editingService.GetBlockTemplates(context)
            : GetFallbackBlockTemplates(context);

        List<MarkdownBlockTemplate> distinctTemplates = [];
        HashSet<string> seenTemplateIds = [];
        foreach (var template in templates)
        {
            if (string.IsNullOrWhiteSpace(template.TemplateId) ||
                !seenTemplateIds.Add(template.TemplateId))
            {
                continue;
            }

            distinctTemplates.Add(template);
        }

        return distinctTemplates;
    }

    private static IReadOnlyList<MarkdownBlockTemplate> GetFallbackBlockTemplates(MarkdownBlockTemplateContext context)
    {
        IMarkdownBlockTemplateProvider[] providers =
        [
            new BuiltInMarkdownBlockTemplateProvider(),
            new BuiltInMarkdownMetadataTemplateProvider()
        ];

        List<MarkdownBlockTemplate> templates = [];
        foreach (var provider in providers)
        {
            foreach (var template in provider.GetTemplates(context) ?? Array.Empty<MarkdownBlockTemplate>())
            {
                if (string.IsNullOrWhiteSpace(template.TemplateId) ||
                    string.IsNullOrWhiteSpace(template.Label) ||
                    string.IsNullOrWhiteSpace(template.Markdown))
                {
                    continue;
                }

                templates.Add(template);
            }
        }

        return templates;
    }

    private bool TryCreateHitForSourceSpan(MarkdownSourceSpan sourceSpan, out MarkdownHitTestResult? hit)
    {
        hit = null;
        if (_lastRenderResult is null || sourceSpan.IsEmpty)
        {
            return false;
        }

        if (!_structuralEditingService.TryFindNodeContainingSpan(_lastRenderResult.ParseResult, sourceSpan, out var node) || node is null)
        {
            return false;
        }

        var astNode = MarkdownRenderedElementMetadata.CreateAstNodeInfo(node);
        if (astNode is null)
        {
            return false;
        }

        hit = new MarkdownHitTestResult(
            astNode,
            node is Markdig.Syntax.Block ? MarkdownRenderedElementKind.BlockControl : MarkdownRenderedElementKind.Text,
            _lastRenderResult.ParseResult,
            highlightRects: ResolveHighlightRects(sourceSpan),
            sourceSpanOverride: sourceSpan);
        return true;
    }

    private IReadOnlyList<Rect> ResolveHighlightRects(MarkdownSourceSpan sourceSpan)
    {
        if (_lastRenderResult is null)
        {
            return Array.Empty<Rect>();
        }

        List<Rect> rects = [];
        foreach (var textEntry in _lastRenderResult.RenderMap.TextEntries)
        {
            if (!Intersects(textEntry.AstNode.SourceSpan, sourceSpan) || TextLayout is null)
            {
                continue;
            }

            foreach (var rect in TextLayout.HitTestTextRange(textEntry.RenderedTextStart, textEntry.RenderedTextLength))
            {
                var translatedRect = new Rect(
                    rect.X + Padding.Left,
                    rect.Y + Padding.Top,
                    rect.Width,
                    rect.Height);
                if (translatedRect.Width > 0 || translatedRect.Height > 0)
                {
                    rects.Add(translatedRect);
                }
            }
        }

        foreach (var entry in _lastRenderResult.RenderMap.VisualEntries.Values)
        {
            if (!Intersects(entry.AstNode.SourceSpan, sourceSpan))
            {
                continue;
            }

            if (entry.Control.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            rects.Add(new Rect(origin, entry.Control.Bounds.Size));
        }

        return rects;
    }

    private static bool Intersects(MarkdownSourceSpan left, MarkdownSourceSpan right)
    {
        return !left.IsEmpty &&
               !right.IsEmpty &&
               left.Start < right.EndExclusive &&
               right.Start < left.EndExclusive;
    }

    private bool TrySelectSourceSpan(MarkdownSourceSpan sourceSpan)
    {
        _selectedSourceSpan = sourceSpan;
        if (!TryCreateHitForSourceSpan(sourceSpan, out var hit) || hit is null)
        {
            return false;
        }

        SetSelectionCore(hit, raiseEvent: true);
        return true;
    }

    private void UpdateSelection(MarkdownHitTestResult hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        SetSelectionCore(hit, raiseEvent: true);
    }

    private void SetSelectionCore(MarkdownHitTestResult? hit, bool raiseEvent)
    {
        var previous = _selectedHitTestResult;
        _selectedHitTestResult = hit;
        _selectedSourceSpan = hit?.SourceSpan;

        if (raiseEvent && !Nullable.Equals(previous?.SourceSpan, hit?.SourceSpan))
        {
            MarkdownSelectionChanged?.Invoke(this, new MarkdownSelectionChangedEventArgs(previous, hit));
        }

        UpdateOutlineSnapshot();
        UpdateSelectionOverlay();
    }

    private Rect ResolvePlacementRect(MarkdownHitTestResult hit)
    {
        if (hit.HighlightBounds is { Width: > 0, Height: > 0 } bounds)
        {
            return bounds.Inflate(4);
        }

        var width = Math.Min(Math.Max(ResolveAvailableWidth() - Padding.Left - Padding.Right, 320), 720);
        var height = Math.Max(FontSize * 2.5, 40);
        return new Rect(Padding.Left, Padding.Top, width, height);
    }

    private void UpdateSelectionOverlay()
    {
        if (_selectedHitTestResult?.HighlightBounds is not { Width: > 0, Height: > 0 } bounds)
        {
            _selectionOverlayHost.Hide();
            return;
        }

        var highlightRect = bounds.Inflate(_activeEditorSession is not null ? 3 : 1);
        if (_selectionOverlayHost.IsOpen)
        {
            _selectionOverlayHost.Update(this, highlightRect);
        }
        else
        {
            _selectionOverlayHost.Show(this, highlightRect);
        }
    }

    private TextBox? ResolveActiveEditorTextBox()
    {
        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focusedElement is TextBox focusedTextBox &&
            focusedTextBox.GetVisualAncestors().OfType<Visual>().Any(static ancestor => ancestor is MarkdownTextBlock))
        {
            return focusedTextBox;
        }

        return this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
    }

    public bool UndoEdit()
    {
        if (_activeEditorSession is not null)
        {
            return false;
        }

        var transaction = _editHistory.TryUndo();
        if (transaction is null)
        {
            return false;
        }

        ApplyHistoryTransaction(transaction, undo: true);
        return true;
    }

    public bool RedoEdit()
    {
        if (_activeEditorSession is not null)
        {
            return false;
        }

        var transaction = _editHistory.TryRedo();
        if (transaction is null)
        {
            return false;
        }

        ApplyHistoryTransaction(transaction, undo: false);
        return true;
    }

    private void CommitEditorReplacement(MarkdownEditorSession session, string replacementMarkdown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(replacementMarkdown);

        var currentMarkdown = Markdown ?? string.Empty;
        var updatedMarkdown = MarkdownSourceEditing.Replace(currentMarkdown, session.SourceSpan, replacementMarkdown);
        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown,
            revealStart: session.SourceSpan.Start,
            revealLength: replacementMarkdown.Length,
            MarkdownEditOperation.Replace);
    }

    private void InsertEditorBlockBefore(MarkdownEditorSession session, MarkdownBlockTemplate template, string currentBlockMarkdown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentBlockMarkdown);

        var currentMarkdown = Markdown ?? string.Empty;
        var replacementMarkdown = MarkdownSourceEditing.BuildBlockInsertionReplacement(
            currentBlockMarkdown,
            template.Markdown,
            insertBefore: true,
            out var revealStart,
            out var revealLength);
        var updatedMarkdown = MarkdownSourceEditing.Replace(currentMarkdown, session.SourceSpan, replacementMarkdown);

        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown,
            session.SourceSpan.Start + revealStart,
            revealLength,
            MarkdownEditOperation.InsertBlockBefore);
    }

    private void InsertEditorBlockAfter(MarkdownEditorSession session, MarkdownBlockTemplate template, string currentBlockMarkdown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentBlockMarkdown);

        var currentMarkdown = Markdown ?? string.Empty;
        var replacementMarkdown = MarkdownSourceEditing.BuildBlockInsertionReplacement(
            currentBlockMarkdown,
            template.Markdown,
            insertBefore: false,
            out var revealStart,
            out var revealLength);
        var updatedMarkdown = MarkdownSourceEditing.Replace(currentMarkdown, session.SourceSpan, replacementMarkdown);

        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown,
            session.SourceSpan.Start + revealStart,
            revealLength,
            MarkdownEditOperation.InsertBlockAfter);
    }

    private void RemoveEditorBlock(MarkdownEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var currentMarkdown = Markdown ?? string.Empty;
        var updatedMarkdown = MarkdownSourceEditing.Remove(currentMarkdown, session.SourceSpan);

        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            string.Empty,
            revealStart: Math.Clamp(session.SourceSpan.Start, 0, updatedMarkdown.Length),
            revealLength: 0,
            MarkdownEditOperation.RemoveBlock);
    }

    private void CompleteEditorChange(
        MarkdownEditorSession session,
        string currentMarkdown,
        string updatedMarkdown,
        string replacementMarkdown,
        int revealStart,
        int revealLength,
        MarkdownEditOperation operation)
    {
        _activeEditorSession = null;
        _commandPaletteService.Hide(suppressDismiss: true);

        revealStart = Math.Clamp(revealStart, 0, updatedMarkdown.Length);
        revealLength = Math.Clamp(revealLength, 0, Math.Max(updatedMarkdown.Length - revealStart, 0));
        _selectedSourceSpan = revealLength > 0 ? new MarkdownSourceSpan(revealStart, revealLength) : null;

        if (string.Equals(updatedMarkdown, currentMarkdown, StringComparison.Ordinal))
        {
            RebuildMarkdown();
            return;
        }

        var transaction = new MarkdownEditTransaction(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown,
            revealStart,
            revealLength,
            operation.ToString());
        _editHistory.Record(transaction);

        _isApplyingInternalMarkdownChange = true;
        try
        {
            SetCurrentValue(MarkdownProperty, updatedMarkdown);
        }
        finally
        {
            _isApplyingInternalMarkdownChange = false;
        }

        MarkdownEdited?.Invoke(this, new MarkdownEditedEventArgs(session, replacementMarkdown, updatedMarkdown, revealStart, revealLength, operation, transaction));
    }

    private double ResolveAvailableWidth(double availableWidth = double.NaN)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return Math.Max(availableWidth, 160);
        }

        var viewportWidth = ResolveViewportWidth();
        if (viewportWidth > 0)
        {
            return Math.Max(viewportWidth, 160);
        }

        return Bounds.Width > 0 ? Math.Max(Bounds.Width, 160) : 640;
    }

    private double ResolveViewportWidth()
    {
        if (this.FindAncestorOfType<ScrollViewer>() is { Viewport.Width: > 0 } scrollViewer)
        {
            return scrollViewer.Viewport.Width;
        }

        return _effectiveViewportWidth > 0 ? _effectiveViewportWidth : 0;
    }

    private void DisposeRenderResources()
    {
        _renderResources?.Dispose();
        _renderResources = null;
    }

    private bool HandleEditHistoryShortcut(KeyEventArgs eventArgs)
    {
        if (!IsEditingEnabled || _activeEditorSession is not null)
        {
            return false;
        }

        var modifiers = eventArgs.KeyModifiers;
        var isCommand = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        if (!isCommand)
        {
            return false;
        }

        if (eventArgs.Key == Key.Z)
        {
            var handled = modifiers.HasFlag(KeyModifiers.Shift) ? RedoEdit() : UndoEdit();
            eventArgs.Handled = handled;
            return handled;
        }

        if (eventArgs.Key == Key.Y)
        {
            var handled = RedoEdit();
            eventArgs.Handled = handled;
            return handled;
        }

        return false;
    }

    private void ApplyHistoryTransaction(MarkdownEditTransaction transaction, bool undo)
    {
        var updatedMarkdown = undo ? transaction.PreviousMarkdown : transaction.UpdatedMarkdown;
        var revealStart = Math.Clamp(transaction.RevealStart, 0, updatedMarkdown.Length);
        var revealLength = Math.Clamp(transaction.RevealLength, 0, Math.Max(updatedMarkdown.Length - revealStart, 0));
        var operation = undo ? MarkdownEditOperation.Undo : MarkdownEditOperation.Redo;
        _selectedSourceSpan = revealLength > 0 ? new MarkdownSourceSpan(revealStart, revealLength) : null;

        _isApplyingInternalMarkdownChange = true;
        try
        {
            SetCurrentValue(MarkdownProperty, updatedMarkdown);
        }
        finally
        {
            _isApplyingInternalMarkdownChange = false;
        }

        MarkdownEdited?.Invoke(
            this,
            new MarkdownEditedEventArgs(
                transaction.Session,
                transaction.ReplacementMarkdown,
                updatedMarkdown,
                revealStart,
                revealLength,
                operation,
                transaction));
    }
}

public enum MarkdownEditOperation
{
    Replace,
    InsertBlockBefore,
    InsertBlockAfter,
    RemoveBlock,
    DuplicateBlock,
    MoveBlockUp,
    MoveBlockDown,
    PromoteBlock,
    DemoteBlock,
    SplitBlock,
    JoinBlock,
    Undo,
    Redo
}

public sealed class MarkdownEditedEventArgs(
    MarkdownEditorSession session,
    string replacementMarkdown,
    string updatedMarkdown,
    int revealStart,
    int revealLength,
    MarkdownEditOperation operation,
    MarkdownEditTransaction transaction) : EventArgs
{
    public MarkdownEditorSession Session { get; } = session;

    public string ReplacementMarkdown { get; } = replacementMarkdown;

    public string UpdatedMarkdown { get; } = updatedMarkdown;

    public int RevealStart { get; } = revealStart;

    public int RevealLength { get; } = revealLength;

    public MarkdownEditOperation Operation { get; } = operation;

    public MarkdownEditTransaction Transaction { get; } = transaction;
}

public enum MarkdownEditCancellationReason
{
    UserRequested,
    MarkdownChanged,
    EditingDisabled
}

public sealed class MarkdownEditCanceledEventArgs(MarkdownEditorSession session, MarkdownEditCancellationReason reason) : EventArgs
{
    public MarkdownEditorSession Session { get; } = session;

    public MarkdownEditCancellationReason Reason { get; } = reason;
}

public sealed class MarkdownSelectionChangedEventArgs(MarkdownHitTestResult? previousSelection, MarkdownHitTestResult? currentSelection) : EventArgs
{
    public MarkdownHitTestResult? PreviousSelection { get; } = previousSelection;

    public MarkdownHitTestResult? CurrentSelection { get; } = currentSelection;
}
