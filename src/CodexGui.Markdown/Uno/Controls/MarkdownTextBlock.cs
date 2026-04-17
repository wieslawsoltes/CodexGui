using System.Collections.Concurrent;
using System.Text;
using CodexGui.Markdown.Core;
using CodexGui.Markdown.Plugin.Math;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.System;
using WinTextWrapping = Microsoft.UI.Xaml.TextWrapping;

namespace CodexGui.Markdown.Controls;

public sealed class MarkdownTextBlock : UserControl
{
    private sealed record DefinitionListDraftEntry(string TermsText, string DefinitionMarkdown);

    private sealed record BlockHostEntry(MarkdownBlockNode Block, FrameworkElement Host);

    [Flags]
    private enum MarkdownRefreshFlags
    {
        None = 0,
        Parse = 1,
        Layout = 2,
        Materialize = 4,
        All = Parse | Layout | Materialize
    }

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(null, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty BaseUriProperty =
        DependencyProperty.Register(
            nameof(BaseUri),
            typeof(Uri),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(null, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty IsEditingEnabledProperty =
        DependencyProperty.Register(
            nameof(IsEditingEnabled),
            typeof(bool),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(false, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty EditorPresentationModeProperty =
        DependencyProperty.Register(
            nameof(EditorPresentationMode),
            typeof(MarkdownEditorPresentationMode),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(MarkdownEditorPresentationMode.Inline, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(
            nameof(TextWrapping),
            typeof(WinTextWrapping),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(WinTextWrapping.Wrap, OnMarkdownPropertyChanged));

    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] AlertKinds =
    [
        "note",
        "info",
        "tip",
        "success",
        "important",
        "warning",
        "caution",
        "danger",
        "error"
    ];

    private readonly MarkdownDocumentBuilder _documentBuilder;
    private readonly MarkdownLayoutService _layoutService;
    private readonly List<BlockHostEntry> _blockHosts = [];

    private TextBox? _activeEditorTextBox;
    private MarkdownDocumentModel? _document;
    private MarkdownLayoutDocument? _layout;
    private MarkdownEditorSession? _activeEditorSession;
    private MarkdownEditorPreferences _editorPreferences = new();
    private double _lastWidth = -1;
    private MarkdownRefreshFlags _pendingRefresh = MarkdownRefreshFlags.All;
    private bool _refreshQueued;

    public MarkdownTextBlock()
    {
        var registry = MarkdownRuntimeConfiguration.Snapshot();
        _documentBuilder = new MarkdownDocumentBuilder(registry);
        _layoutService = new MarkdownLayoutService(registry);
        Content = CreateRootPanel();
        Loaded += OnLoaded;
        SizeChanged += (_, args) =>
        {
            var availableWidth = ResolveAvailableWidth(args.NewSize.Width);
            if (Math.Abs(availableWidth - _lastWidth) > 0.5)
            {
                QueueRefresh(MarkdownRefreshFlags.Layout | MarkdownRefreshFlags.Materialize);
            }
        };
    }

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public new Uri? BaseUri
    {
        get => (Uri?)GetValue(BaseUriProperty);
        set => SetValue(BaseUriProperty, value);
    }

    public bool IsEditingEnabled
    {
        get => (bool)GetValue(IsEditingEnabledProperty);
        set => SetValue(IsEditingEnabledProperty, value);
    }

    public MarkdownEditorPresentationMode EditorPresentationMode
    {
        get => (MarkdownEditorPresentationMode)GetValue(EditorPresentationModeProperty);
        set => SetValue(EditorPresentationModeProperty, value);
    }

    public WinTextWrapping TextWrapping
    {
        get => (WinTextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public MarkdownDocumentModel? LastDocument => _document;

    public MarkdownParseResult? LastParseResult => _document?.ParseResult;

    public MarkdownLayoutDocument? LastLayout => _layout;

    public MarkdownEditorSession? ActiveEditorSession => _activeEditorSession;

    public MarkdownEditorPreferences EditorPreferences
    {
        get => _editorPreferences;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _editorPreferences = value;
            QueueRefresh(MarkdownRefreshFlags.Materialize);
        }
    }

    public event EventHandler<MarkdownEditedEventArgs>? MarkdownEdited;

    public event EventHandler<MarkdownEditCanceledEventArgs>? MarkdownEditCanceled;

    private static void OnMarkdownPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (MarkdownTextBlock)dependencyObject;
        if (args.Property == IsEditingEnabledProperty && !(bool)args.NewValue && control._activeEditorSession is not null)
        {
            control.CancelEdit(MarkdownEditCancellationReason.EditingDisabled);
            return;
        }

        if (args.Property == MarkdownProperty && control._activeEditorSession is not null)
        {
            control.CancelEdit(MarkdownEditCancellationReason.MarkdownChanged);
        }

        control.QueueRefresh(MapRefreshFlags(args.Property));
    }

    private static StackPanel CreateRootPanel()
    {
        return new StackPanel
        {
            Spacing = 10
        };
    }

    private static MarkdownRefreshFlags MapRefreshFlags(DependencyProperty dependencyProperty)
    {
        if (dependencyProperty == MarkdownProperty)
        {
            return MarkdownRefreshFlags.All;
        }

        return dependencyProperty == TextWrappingProperty
            ? MarkdownRefreshFlags.Layout | MarkdownRefreshFlags.Materialize
            : MarkdownRefreshFlags.Materialize;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var refreshFlags = MarkdownRefreshFlags.None;
        if (_document is null)
        {
            refreshFlags |= MarkdownRefreshFlags.Parse;
        }

        if (_layout is null)
        {
            refreshFlags |= MarkdownRefreshFlags.Layout;
        }

        if (Content is not Panel panel || panel.Children.Count == 0)
        {
            refreshFlags |= MarkdownRefreshFlags.Materialize;
        }

        if (refreshFlags != MarkdownRefreshFlags.None)
        {
            QueueRefresh(refreshFlags);
        }
    }

    private double ResolveAvailableWidth()
    {
        return ResolveAvailableWidth(ActualWidth);
    }

    private double ResolveAvailableWidth(double width)
    {
        var effectiveWidth = width;
        if (double.IsNaN(effectiveWidth) || effectiveWidth <= 0)
        {
            effectiveWidth = Width;
        }

        return effectiveWidth > 0 ? Math.Max(effectiveWidth - 8, 1) : 720;
    }

    private void QueueRefresh(MarkdownRefreshFlags refreshFlags)
    {
        _pendingRefresh |= refreshFlags;
        if (!IsLoaded || _refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        ProcessQueuedRefreshAsync();
    }

    private async void ProcessQueuedRefreshAsync()
    {
        await Task.Yield();

        while (IsLoaded)
        {
            var refreshFlags = _pendingRefresh;
            if (refreshFlags == MarkdownRefreshFlags.None)
            {
                break;
            }

            _pendingRefresh = MarkdownRefreshFlags.None;
            await RefreshAsync(refreshFlags);
        }

        _refreshQueued = false;
        if (IsLoaded && _pendingRefresh != MarkdownRefreshFlags.None)
        {
            QueueRefresh(MarkdownRefreshFlags.None);
        }
    }

    private async Task RefreshAsync(MarkdownRefreshFlags refreshFlags)
    {
        var markdownSnapshot = Markdown ?? string.Empty;
        var availableWidth = ResolveAvailableWidth();
        var requiresInteractiveLayout = IsEditingEnabled || _activeEditorSession is not null;
        var requiresDocument = _document is null ||
                               refreshFlags.HasFlag(MarkdownRefreshFlags.Parse) ||
                               !string.Equals(_document.ParseResult.OriginalMarkdown, markdownSnapshot, StringComparison.Ordinal);
        var requiresLayout = requiresInteractiveLayout &&
                             (_layout is null ||
                              requiresDocument ||
                              refreshFlags.HasFlag(MarkdownRefreshFlags.Layout) ||
                              Math.Abs(_lastWidth - availableWidth) > 0.5);

        if (requiresDocument || requiresLayout)
        {
            var currentDocument = _document;
            var currentLayout = requiresInteractiveLayout ? _layout : null;
            var result = await Task.Run(() =>
            {
                var document = requiresDocument ? _documentBuilder.Build(markdownSnapshot) : currentDocument!;
                var layout = requiresLayout
                    ? _layoutService.Layout(document, availableWidth)
                    : currentLayout;
                return (Document: document, Layout: layout);
            });

            if (!IsLoaded)
            {
                return;
            }

            if (!string.Equals(markdownSnapshot, Markdown ?? string.Empty, StringComparison.Ordinal))
            {
                QueueRefresh(MarkdownRefreshFlags.All);
                return;
            }

            var currentWidth = ResolveAvailableWidth();
            if (Math.Abs(currentWidth - availableWidth) > 0.5)
            {
                QueueRefresh(MarkdownRefreshFlags.Layout | MarkdownRefreshFlags.Materialize);
                return;
            }

            _document = result.Document;
            _layout = result.Layout;
            _lastWidth = requiresLayout ? availableWidth : -1;
        }

        MaterializeDocument();
    }

    private void MaterializeDocument()
    {
        _blockHosts.Clear();
        if (_activeEditorSession is null && !IsEditingEnabled)
        {
            Content = CreateSharedRendererView(_document?.Blocks, Markdown);
            return;
        }

        if (_layout is null)
        {
            var emptyPanel = CreateRootPanel();
            Content = emptyPanel;
            return;
        }

        var panel = CreateRootPanel();
        foreach (var blockLayout in _layout.Blocks)
        {
            var element = RenderBlock(blockLayout.Block, blockLayout, allowEditing: true);
            if (element is not null)
            {
                panel.Children.Add(element);
                if (element is FrameworkElement host)
                {
                    _blockHosts.Add(new BlockHostEntry(blockLayout.Block, host));
                }
            }
        }

        Content = panel;
    }

    private UIElement? RenderBlock(MarkdownBlockNode block, MarkdownBlockLayout? blockLayout, bool allowEditing)
    {
        if (_activeEditorSession is not null && _activeEditorSession.SourceSpan == block.SourceSpan)
        {
            return RenderEditor(block, _activeEditorSession);
        }

        var element = CreateBlockContent(block, blockLayout);
        if (element is null)
        {
            return null;
        }

        if (_activeEditorSession is not null &&
            _activeEditorSession.SourceSpan != block.SourceSpan &&
            _activeEditorSession.HostSourceSpan == block.SourceSpan)
        {
            return WrapBlockHost(
                block,
                new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        element,
                        CreateEditorChrome(_activeEditorSession.Title, CreateInlineEditor(_activeEditorSession))
                    }
                },
                allowEditing);
        }

        return WrapBlockHost(block, element, allowEditing);
    }

    private UIElement? CreateBlockContent(MarkdownBlockNode block, MarkdownBlockLayout? blockLayout)
    {
        _ = blockLayout;
        return CreateSharedRendererView([block], markdown: null);
    }

    private UIElement CreateSharedRendererView(IReadOnlyList<MarkdownBlockNode>? blocks, string? markdown)
    {
        var view = new MarkdownThreadMessageView
        {
            BaseUri = BaseUri,
            FontSize = FontSize,
            Foreground = Foreground,
            TextWrapping = TextWrapping
        };

        if (blocks is not null)
        {
            view.BlocksSource = blocks;
        }
        else
        {
            view.Markdown = markdown;
        }

        return view;
    }

    private UIElement CreateNestedRendererView(string markdown, double horizontalPadding = 0)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = string.Empty
            };
        }

        var nested = CreateSharedRendererView(blocks: null, markdown: markdown);
        if (horizontalPadding <= 0)
        {
            return nested;
        }

        return new Border
        {
            Padding = new Thickness(horizontalPadding, 0, 0, 0),
            Child = nested
        };
    }

    public MarkdownHitRegion? HitTestMarkdown(Point point)
    {
        EnsureInteractiveLayout();
        if (_layout?.HitTest(point.X, point.Y) is { } inlineHit)
        {
            return inlineHit;
        }

        return TryHitTestBlockHost(point, out var hostHit) ? hostHit : null;
    }

    private void EnsureInteractiveLayout()
    {
        if (_document is null)
        {
            _document = _documentBuilder.Build(Markdown ?? string.Empty);
        }

        var availableWidth = ResolveAvailableWidth();
        if (_layout is null || Math.Abs(_lastWidth - availableWidth) > 0.5)
        {
            _layout = _layoutService.Layout(_document, availableWidth);
            _lastWidth = availableWidth;
        }
    }

    public bool TryBeginEdit(Point point)
    {
        if (!IsEditingEnabled)
        {
            return false;
        }

        return HitTestMarkdown(point) is { } hit && TryBeginEdit(hit);
    }

    public bool TryBeginEdit(MarkdownHitRegion hitRegion)
    {
        ArgumentNullException.ThrowIfNull(hitRegion);
        if (string.Equals(hitRegion.Kind, "inline-math", StringComparison.Ordinal) &&
            TryBeginInlineMathEdit(hitRegion.SourceSpan))
        {
            return true;
        }

        return TryResolveBlock(hitRegion.SourceSpan) is { } hitBlock && TryBeginEdit(hitBlock);
    }

    public void CancelEdit()
    {
        CancelEdit(MarkdownEditCancellationReason.UserRequested);
    }

    private bool TryBeginEdit(MarkdownBlockNode block)
    {
        if (!IsEditingEnabled || block.SourceSpan.IsEmpty)
        {
            return false;
        }

        var sourceText = block.SourceSpan.Slice(Markdown ?? string.Empty);
        _activeEditorTextBox = null;
        _activeEditorSession = new MarkdownEditorSession(
            ResolveEditorId(block),
            ResolveEditorFeature(block),
            block.SourceSpan,
            ResolveEditorTitle(block),
            block.GetType().Name,
            sourceText);
        QueueRefresh(MarkdownRefreshFlags.Materialize);
        return true;
    }

    private void CancelEdit(MarkdownEditCancellationReason reason)
    {
        if (_activeEditorSession is null)
        {
            return;
        }

        var session = _activeEditorSession;
        _activeEditorSession = null;
        _activeEditorTextBox = null;
        QueueRefresh(MarkdownRefreshFlags.Materialize);
        MarkdownEditCanceled?.Invoke(this, new MarkdownEditCanceledEventArgs(session, reason));
    }

    private UIElement WrapBlockHost(MarkdownBlockNode block, UIElement element, bool allowEditing)
    {
        _ = block;
        _ = allowEditing;
        return element;
    }

    private UIElement RenderEditor(MarkdownBlockNode block, MarkdownEditorSession session)
    {
        if (block is MarkdownCodeBlock codeBlock)
        {
            if (TryCreateCodeEditor(block, session, codeBlock) is { } editorView)
            {
                return CreateEditorChrome(session.Title, editorView);
            }

            return CreateEditorChrome(
                session.Title,
                CreateGenericCodeEditor(session, codeBlock));
        }

        if (block is MarkdownYamlFrontMatterBlock yamlFrontMatterBlock)
        {
            return CreateEditorChrome(session.Title, CreateYamlEditor(session, yamlFrontMatterBlock));
        }

        if (block is MarkdownLinkReferenceBlock linkReferenceBlock)
        {
            return CreateEditorChrome(session.Title, CreateLinkReferenceEditor(session, linkReferenceBlock));
        }

        if (block is MarkdownAbbreviationBlock abbreviationBlock)
        {
            return CreateEditorChrome(session.Title, CreateAbbreviationEditor(session, abbreviationBlock));
        }

        if (block is MarkdownFootnoteBlock footnoteBlock)
        {
            return CreateEditorChrome(session.Title, CreateFootnoteEditor(session, footnoteBlock));
        }

        if (block is MarkdownAlertBlock alertBlock)
        {
            return CreateEditorChrome(session.Title, CreateAlertEditor(session, alertBlock));
        }

        if (block is MarkdownCustomContainerBlock customContainerBlock)
        {
            return CreateEditorChrome(session.Title, CreateCustomContainerEditor(session, customContainerBlock));
        }

        if (block is MarkdownDefinitionListBlock definitionListBlock)
        {
            return CreateEditorChrome(session.Title, CreateDefinitionListEditor(session, definitionListBlock));
        }

        if (block is MarkdownFigureBlock figureBlock)
        {
            return CreateEditorChrome(session.Title, CreateFigureEditor(session, figureBlock));
        }

        if (block is MarkdownFooterBlock footerBlock)
        {
            return CreateEditorChrome(session.Title, CreateFooterEditor(session, footerBlock));
        }

        if (block is MarkdownMathBlock mathBlock)
        {
            return CreateEditorChrome(session.Title, CreateMathEditor(session, mathBlock));
        }

        if (block is MarkdownMermaidBlock mermaidBlock)
        {
            return CreateEditorChrome(session.Title, CreateMermaidEditor(session, mermaidBlock));
        }

        var editor = new TextBox
        {
            Text = session.CurrentMarkdown,
            AcceptsReturn = true,
            TextWrapping = WinTextWrapping.Wrap,
            MinHeight = 180,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = Math.Max(FontSize - 1, 12)
        };

        return CreateEditorChrome(
            session.Title,
            new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    editor,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new Button
                            {
                                Content = "Commit"
                            }.Also(button => button.Click += (_, _) => CommitEditorReplacement(session, editor.Text)),
                            new Button
                            {
                                Content = "Cancel"
                            }.Also(button => button.Click += (_, _) => CancelEdit(MarkdownEditCancellationReason.UserRequested))
                        }
                    }
                }
            });
    }

    private UIElement CreateInlineEditor(MarkdownEditorSession session)
    {
        if (session.EditorId == MathMarkdownEditorIds.Inline)
        {
            return CreateInlineMathEditor(session);
        }

        return new TextBlock
        {
            Text = "Inline editor is unavailable for this markdown element.",
            Foreground = ToneBrush("#64748B"),
            TextWrapping = WinTextWrapping.Wrap
        };
    }

    private UIElement CreateYamlEditor(MarkdownEditorSession session, MarkdownYamlFrontMatterBlock yamlFrontMatterBlock)
    {
        var editor = new TextBox
        {
            Text = yamlFrontMatterBlock.Yaml,
            AcceptsReturn = true,
            TextWrapping = WinTextWrapping.NoWrap,
            MinHeight = 220,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = Math.Max(FontSize - 1, 12)
        };

        return CreateEditorBody(
            editor,
            () => CommitEditorReplacement(session, MarkdownSourceEditing.BuildYamlFrontMatter(editor.Text)));
    }

    private UIElement CreateLinkReferenceEditor(MarkdownEditorSession session, MarkdownLinkReferenceBlock linkReferenceBlock)
    {
        var labelEditor = CreateSingleLineEditor(linkReferenceBlock.Label);
        var urlEditor = CreateSingleLineEditor(linkReferenceBlock.Url);
        var titleEditor = CreateSingleLineEditor(linkReferenceBlock.Title ?? string.Empty);

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateLabeledField("Label", labelEditor),
                    CreateLabeledField("URL", urlEditor),
                    CreateLabeledField("Title", titleEditor)
                }
            },
            () => CommitEditorReplacement(
                session,
                MarkdownSourceEditing.BuildLinkReferenceDefinition(labelEditor.Text, urlEditor.Text, titleEditor.Text)));
    }

    private UIElement CreateAbbreviationEditor(MarkdownEditorSession session, MarkdownAbbreviationBlock abbreviationBlock)
    {
        var labelEditor = CreateSingleLineEditor(abbreviationBlock.Label);
        var meaningEditor = CreateSingleLineEditor(abbreviationBlock.Meaning);

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateLabeledField("Label", labelEditor),
                    CreateLabeledField("Meaning", meaningEditor)
                }
            },
            () => CommitEditorReplacement(
                session,
                MarkdownSourceEditing.BuildAbbreviationMarkdown(labelEditor.Text, meaningEditor.Text)));
    }

    private UIElement CreateFootnoteEditor(MarkdownEditorSession session, MarkdownFootnoteBlock footnoteBlock)
    {
        var labelEditor = CreateSingleLineEditor(footnoteBlock.Label);
        var bodyEditor = new TextBox
        {
            Text = footnoteBlock.BodyMarkdown,
            AcceptsReturn = true,
            TextWrapping = WinTextWrapping.Wrap,
            MinHeight = 180,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = Math.Max(FontSize - 1, 12)
        };

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateLabeledField("Label", labelEditor),
                    CreateLabeledField("Body", bodyEditor)
                }
            },
            () => CommitEditorReplacement(
                session,
                MarkdownSourceEditing.BuildFootnoteMarkdown(labelEditor.Text, bodyEditor.Text)));
    }

    private UIElement CreateAlertEditor(MarkdownEditorSession session, MarkdownAlertBlock alertBlock)
    {
        var kindSelector = new ComboBox
        {
            ItemsSource = AlertKinds,
            SelectedItem = alertBlock.Kind,
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var bodyEditor = CreateMultiLineEditor(alertBlock.BodyMarkdown, minHeight: 160);
        var previewHost = CreatePreviewHost();
        var descriptionText = new TextBlock
        {
            Foreground = ToneBrush("#64748B"),
            TextWrapping = WinTextWrapping.Wrap
        };

        string BuildMarkdown()
        {
            return MarkdownSourceEditing.BuildAlertBlock(kindSelector.SelectedItem?.ToString(), bodyEditor.Text);
        }

        void UpdatePreview()
        {
            var selectedKind = kindSelector.SelectedItem?.ToString() ?? alertBlock.Kind;
            descriptionText.Text = MarkdownAlertViewFactory.ResolveDescription(selectedKind);
            previewHost.Child = RenderAlertBlock(new MarkdownAlertBlock(
                selectedKind,
                MarkdownCalloutSurfaceFactory.FormatLabel(selectedKind),
                bodyEditor.Text,
                alertBlock.SourceSpan));
        }

        kindSelector.SelectionChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateLabeledField("Alert kind", kindSelector),
                    descriptionText,
                    CreateLabeledField("Body", bodyEditor),
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, BuildMarkdown()),
            bodyEditor);
    }

    private UIElement CreateCustomContainerEditor(MarkdownEditorSession session, MarkdownCustomContainerBlock customContainerBlock)
    {
        var kindEditor = CreateSingleLineEditor(customContainerBlock.Kind);
        var argumentsEditor = CreateSingleLineEditor(customContainerBlock.Arguments ?? string.Empty);
        var bodyEditor = CreateMultiLineEditor(customContainerBlock.BodyMarkdown, minHeight: 170);
        var previewHost = CreatePreviewHost();

        string BuildMarkdown()
        {
            return MarkdownSourceEditing.BuildCustomContainer(kindEditor.Text, argumentsEditor.Text, bodyEditor.Text);
        }

        void UpdatePreview()
        {
            var normalizedKind = MarkdownSourceEditing.NormalizeInlineText(kindEditor.Text).ToLowerInvariant();
            previewHost.Child = RenderCustomContainerBlock(new MarkdownCustomContainerBlock(
                normalizedKind,
                MarkdownCalloutSurfaceFactory.ResolveTitle(normalizedKind, "Container"),
                MarkdownSourceEditing.NormalizeInlineText(argumentsEditor.Text),
                bodyEditor.Text,
                customContainerBlock.SourceSpan));
        }

        kindEditor.TextChanged += (_, _) => UpdatePreview();
        argumentsEditor.TextChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateLabeledField("Container type", kindEditor),
                    CreateLabeledField("Arguments", argumentsEditor),
                    CreateLabeledField("Body", bodyEditor),
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, BuildMarkdown()),
            bodyEditor);
    }

    private UIElement CreateDefinitionListEditor(MarkdownEditorSession session, MarkdownDefinitionListBlock definitionListBlock)
    {
        var drafts = CreateDefinitionDrafts(definitionListBlock);
        var entriesPanel = new StackPanel
        {
            Spacing = 10
        };
        var previewHost = CreatePreviewHost();

        string BuildMarkdown()
        {
            return BuildDefinitionListMarkdown(drafts);
        }

        void UpdatePreview()
        {
            previewHost.Child = RenderDefinitionListBlock(BuildDefinitionListPreview(drafts, definitionListBlock.SourceSpan));
        }

        void RebuildEditors()
        {
            entriesPanel.Children.Clear();

            for (var index = 0; index < drafts.Count; index++)
            {
                var entryIndex = index;
                var draft = drafts[entryIndex];
                var termsEditor = CreateMultiLineEditor(draft.TermsText, minHeight: 86);
                var definitionEditor = CreateMultiLineEditor(draft.DefinitionMarkdown, minHeight: 150);
                var removeButton = CreateSecondaryButton("Remove entry", () =>
                {
                    if (drafts.Count <= 1)
                    {
                        return;
                    }

                    drafts.RemoveAt(entryIndex);
                    RebuildEditors();
                    UpdatePreview();
                });
                removeButton.IsEnabled = drafts.Count > 1;

                termsEditor.TextChanged += (_, _) =>
                {
                    drafts[entryIndex] = drafts[entryIndex] with { TermsText = termsEditor.Text ?? string.Empty };
                    UpdatePreview();
                };
                definitionEditor.TextChanged += (_, _) =>
                {
                    drafts[entryIndex] = drafts[entryIndex] with { DefinitionMarkdown = definitionEditor.Text ?? string.Empty };
                    UpdatePreview();
                };

                entriesPanel.Children.Add(new Border
                {
                    BorderBrush = ToneBrush("#CBD5E1"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new Grid
                            {
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                                    new ColumnDefinition { Width = GridLength.Auto }
                                },
                                ColumnSpacing = 8,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = $"Entry {entryIndex + 1}",
                                        FontWeight = FontWeights.SemiBold,
                                        VerticalAlignment = VerticalAlignment.Center
                                    },
                                    removeButton.Also(static button => Grid.SetColumn(button, 1))
                                }
                            },
                            CreateLabeledField("Terms (one per line)", termsEditor),
                            CreateLabeledField("Definition markdown", definitionEditor)
                        }
                    }
                });
            }
        }

        var addEntryButton = CreateSecondaryButton("Add entry", () =>
        {
            drafts.Add(new DefinitionListDraftEntry("New term", "Definition text."));
            RebuildEditors();
            UpdatePreview();
        });

        RebuildEditors();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Edit each definition entry as structured terms and markdown content instead of manually maintaining `:` prefixes.",
                        Foreground = ToneBrush("#64748B"),
                        TextWrapping = WinTextWrapping.Wrap
                    },
                    addEntryButton,
                    entriesPanel,
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, BuildMarkdown()));
    }

    private UIElement CreateFigureEditor(MarkdownEditorSession session, MarkdownFigureBlock figureBlock)
    {
        var leadingCaptionEditor = CreateSingleLineEditor(figureBlock.LeadingCaptionMarkdown ?? string.Empty);
        var bodyEditor = CreateMultiLineEditor(figureBlock.BodyMarkdown, minHeight: 220);
        var trailingCaptionEditor = CreateSingleLineEditor(figureBlock.TrailingCaptionMarkdown ?? string.Empty);
        var previewHost = CreatePreviewHost();
        var snippetToolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        snippetToolbar.Children.Add(CreateSecondaryButton("Insert image", () => AppendSnippet(bodyEditor, "![Alt text](https://example.com/image.png)")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)));
        snippetToolbar.Children.Add(CreateSecondaryButton("Insert note", () => AppendSnippet(bodyEditor, "Supporting figure notes go here.")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)));

        string BuildMarkdown()
        {
            return MarkdownSourceEditing.BuildFigure(leadingCaptionEditor.Text, bodyEditor.Text, trailingCaptionEditor.Text);
        }

        void UpdatePreview()
        {
            previewHost.Child = RenderFigureBlock(new MarkdownFigureBlock(
                bodyEditor.Text,
                MarkdownSourceEditing.NormalizeInlineMarkdown(leadingCaptionEditor.Text),
                MarkdownSourceEditing.NormalizeInlineMarkdown(trailingCaptionEditor.Text),
                figureBlock.SourceSpan));
        }

        leadingCaptionEditor.TextChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        trailingCaptionEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateLabeledField("Opening caption", leadingCaptionEditor),
                    CreateLabeledField("Body", bodyEditor),
                    snippetToolbar,
                    CreateLabeledField("Closing caption", trailingCaptionEditor),
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, BuildMarkdown()),
            bodyEditor);
    }

    private UIElement CreateFooterEditor(MarkdownEditorSession session, MarkdownFooterBlock footerBlock)
    {
        var bodyEditor = CreateMultiLineEditor(footerBlock.BodyMarkdown, minHeight: 160);
        var previewHost = CreatePreviewHost();

        void UpdatePreview()
        {
            previewHost.Child = RenderFooterBlock(new MarkdownFooterBlock(bodyEditor.Text, footerBlock.SourceSpan));
        }

        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Edit footer markdown without manually maintaining the `^^` prefix.",
                        Foreground = ToneBrush("#64748B"),
                        TextWrapping = WinTextWrapping.Wrap
                    },
                    bodyEditor,
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, MarkdownSourceEditing.BuildFooter(bodyEditor.Text)),
            bodyEditor);
    }

    private UIElement CreateMathEditor(MarkdownEditorSession session, MarkdownMathBlock mathBlock)
    {
        var editor = CreateMultiLineEditor(mathBlock.Expression, minHeight: 180, wrap: WinTextWrapping.NoWrap);
        var previewHost = CreatePreviewHost();
        var snippetToolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                CreateSecondaryButton("frac", () => AppendSnippet(editor, @"\frac{a}{b}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("sqrt", () => AppendSnippet(editor, @"\sqrt{x}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("sum", () => AppendSnippet(editor, @"\sum_{i=0}^{n}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("int", () => AppendSnippet(editor, @"\int_{a}^{b}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("hat", () => AppendSnippet(editor, @"\hat{x}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("text", () => AppendSnippet(editor, @"\text{label}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("matrix", () => AppendSnippet(editor, @"\begin{bmatrix} a & b \\ c & d \end{bmatrix}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("cases", () => AppendSnippet(editor, @"\begin{cases} x & x > 0 \\ 0 & x = 0 \end{cases}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("aligned", () => AppendSnippet(editor, @"\begin{aligned} a &= b + c \\ d &= e - f \end{aligned}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8))
            }
        };

        void UpdatePreview()
        {
            previewHost.Child = MarkdownMathViewFactory.CreateBlockView(editor.Text, FontSize, Foreground as Brush);
        }

        editor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Edit TeX-style math source and review the live formula preview below.",
                        Foreground = ToneBrush("#64748B"),
                        TextWrapping = WinTextWrapping.Wrap
                    },
                    snippetToolbar,
                    editor,
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, MarkdownSourceEditing.BuildMathBlock(editor.Text)),
            editor);
    }

    private UIElement CreateInlineMathEditor(MarkdownEditorSession session)
    {
        var editor = CreateSingleLineEditor(MarkdownSourceEditing.NormalizeInlineMath(session.CurrentMarkdown));
        var previewHost = CreatePreviewHost();
        var snippetToolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                CreateSecondaryButton("frac", () => AppendInlineSnippet(editor, @"\frac{a}{b}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("sqrt", () => AppendInlineSnippet(editor, @"\sqrt{x}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("sum", () => AppendInlineSnippet(editor, @"\sum_{i=0}^{n}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("int", () => AppendInlineSnippet(editor, @"\int_{a}^{b}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("hat", () => AppendInlineSnippet(editor, @"\hat{x}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8)),
                CreateSecondaryButton("text", () => AppendInlineSnippet(editor, @"\text{label}")).Also(static button => button.Margin = new Thickness(0, 0, 8, 8))
            }
        };

        void UpdatePreview()
        {
            previewHost.Child = MarkdownMathViewFactory.CreateInlineView(editor.Text, FontSize, Foreground as Brush);
        }

        editor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Edit inline math and review the live preview below.",
                        Foreground = ToneBrush("#64748B"),
                        TextWrapping = WinTextWrapping.Wrap
                    },
                    snippetToolbar,
                    editor,
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, MarkdownSourceEditing.BuildInlineMath(editor.Text)),
            editor);
    }

    private UIElement CreateMermaidEditor(MarkdownEditorSession session, MarkdownMermaidBlock mermaidBlock)
    {
        var editor = CreateMultiLineEditor(mermaidBlock.DiagramSource, minHeight: 220, wrap: WinTextWrapping.NoWrap);
        var previewHost = CreatePreviewHost();
        var syntaxSelector = new ComboBox
        {
            ItemsSource = new[] { "Fence", "Container" },
            SelectedItem = ResolveMermaidSourceSyntax(session.CurrentMarkdown),
            MinWidth = 140,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var argumentsEditor = CreateSingleLineEditor(ResolveMermaidContainerArguments(session.CurrentMarkdown));

        string BuildMarkdown()
        {
            return string.Equals(syntaxSelector.SelectedItem?.ToString(), "Container", StringComparison.Ordinal)
                ? MarkdownSourceEditing.BuildCustomContainer("mermaid", MarkdownSourceEditing.NormalizeInlineText(argumentsEditor.Text), editor.Text)
                : MarkdownSourceEditing.BuildMermaidFence(editor.Text);
        }

        void UpdatePreview()
        {
            previewHost.Child = MarkdownMermaidViewFactory.CreateBlockView(
                new MarkdownMermaidBlock(
                    editor.Text,
                    "mermaid",
                    MarkdownSourceEditing.NormalizeInlineText(argumentsEditor.Text),
                    string.Equals(syntaxSelector.SelectedItem?.ToString(), "Container", StringComparison.Ordinal)
                        ? MarkdownMermaidBlockSyntax.CustomContainer
                        : MarkdownMermaidBlockSyntax.CodeFence,
                    mermaidBlock.SourceSpan),
                ResolveAvailableWidth(),
                FontSize,
                Foreground as Brush);
            argumentsEditor.Visibility = string.Equals(syntaxSelector.SelectedItem?.ToString(), "Container", StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        syntaxSelector.SelectionChanged += (_, _) => UpdatePreview();
        argumentsEditor.TextChanged += (_, _) => UpdatePreview();
        editor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        return CreateEditorBody(
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    CreateLabeledField("Syntax", syntaxSelector),
                    CreateLabeledField("Container args", argumentsEditor),
                    editor,
                    previewHost
                }
            },
            () => CommitEditorReplacement(session, BuildMarkdown()),
            editor);
    }

    private UIElement CreateEditorBody(UIElement editorContent, Action onCommit, TextBox? preferredFormattingTarget = null)
    {
        ArgumentNullException.ThrowIfNull(editorContent);
        ArgumentNullException.ThrowIfNull(onCommit);

        _activeEditorTextBox = preferredFormattingTarget;

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                editorContent,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Button
                        {
                            Content = "Commit"
                        }.Also(button => button.Click += (_, _) => onCommit()),
                        new Button
                        {
                            Content = "Cancel"
                        }.Also(button => button.Click += (_, _) => CancelEdit(MarkdownEditCancellationReason.UserRequested))
                    }
                }
            }
        };
    }

    private TextBox CreateSingleLineEditor(string text)
    {
        return TrackFormattingTarget(new TextBox
        {
            Text = text,
            AcceptsReturn = false,
            TextWrapping = WinTextWrapping.NoWrap,
            FontSize = Math.Max(FontSize - 1, 12)
        });
    }

    private TextBox CreateMultiLineEditor(string text, double minHeight, WinTextWrapping wrap = WinTextWrapping.Wrap)
    {
        return TrackFormattingTarget(new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = wrap,
            MinHeight = minHeight,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = Math.Max(FontSize - 1, 12),
            VerticalContentAlignment = VerticalAlignment.Top
        });
    }

    private TextBox TrackFormattingTarget(TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        textBox.GotFocus += (_, _) => _activeEditorTextBox = textBox;
        return textBox;
    }

    private Border CreatePreviewHost()
    {
        return new Border
        {
            Background = ToneBrush("#F8FAFC"),
            BorderBrush = ToneBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = EditorPresentationMode == MarkdownEditorPresentationMode.Inline
                ? new Thickness(8, 6, 8, 6)
                : new Thickness(10)
        };
    }

    private static Button CreateSecondaryButton(string label, Action onClick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onClick);

        var button = new Button
        {
            Content = label,
            MinWidth = 72
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void AppendSnippet(TextBox textBox, string snippet)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(snippet);

        var current = MarkdownSourceEditing.NormalizeBlockText(textBox.Text);
        if (current.Length > 0)
        {
            current += "\n\n";
        }

        current += snippet;
        textBox.Text = current;
        textBox.SelectionStart = current.Length;
        textBox.SelectionLength = 0;
    }

    private static void AppendInlineSnippet(TextBox textBox, string snippet)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(snippet);

        var current = MarkdownSourceEditing.NormalizeInlineMath(textBox.Text);
        if (current.Length > 0 && !char.IsWhiteSpace(current[^1]))
        {
            current += " ";
        }

        current += snippet;
        textBox.Text = current;
        textBox.SelectionStart = current.Length;
        textBox.SelectionLength = 0;
    }

    private static List<DefinitionListDraftEntry> CreateDefinitionDrafts(MarkdownDefinitionListBlock block)
    {
        var drafts = new List<DefinitionListDraftEntry>(Math.Max(block.Items.Count, 1));
        foreach (var item in block.Items)
        {
            drafts.Add(new DefinitionListDraftEntry(
                string.Join('\n', item.Terms.Select(static term => term.Markdown)),
                item.DefinitionMarkdown));
        }

        if (drafts.Count == 0)
        {
            drafts.Add(new DefinitionListDraftEntry("Term", "Definition text."));
        }

        return drafts;
    }

    private static string BuildDefinitionListMarkdown(IReadOnlyList<DefinitionListDraftEntry> drafts)
    {
        var items = drafts.Select(static draft => new MarkdownDefinitionItemNode(
            SplitDefinitionTerms(draft.TermsText),
            MarkdownSourceEditing.NormalizeBlockText(draft.DefinitionMarkdown))).ToArray();
        return MarkdownSourceEditing.BuildDefinitionList(items);
    }

    private static MarkdownDefinitionListBlock BuildDefinitionListPreview(
        IReadOnlyList<DefinitionListDraftEntry> drafts,
        MarkdownSourceSpan sourceSpan)
    {
        var items = drafts.Select(static draft => new MarkdownDefinitionItemNode(
            SplitDefinitionTerms(draft.TermsText),
            MarkdownSourceEditing.NormalizeBlockText(draft.DefinitionMarkdown))).ToArray();
        return new MarkdownDefinitionListBlock(items, sourceSpan);
    }

    private static IReadOnlyList<MarkdownDefinitionTermNode> SplitDefinitionTerms(string? termsText)
    {
        var normalized = MarkdownSourceEditing.NormalizeLineEndings(termsText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return
            [
                new MarkdownDefinitionTermNode("Term", "Term")
            ];
        }

        return normalized
            .Split('\n', StringSplitOptions.None)
            .Select(static line => MarkdownSourceEditing.NormalizeInlineMarkdown(line))
            .Where(static line => line.Length > 0)
            .Select(static line => new MarkdownDefinitionTermNode(line, line))
            .ToArray();
    }

    private static string ResolveMermaidSourceSyntax(string source)
    {
        return TryResolveMermaidContainerArguments(source, out _)
            ? "Container"
            : "Fence";
    }

    private static string ResolveMermaidContainerArguments(string source)
    {
        return TryResolveMermaidContainerArguments(source, out var arguments)
            ? arguments
            : string.Empty;
    }

    private static bool TryResolveMermaidContainerArguments(string source, out string arguments)
    {
        var normalized = MarkdownSourceEditing.NormalizeLineEndings(source).TrimStart();
        if (!normalized.StartsWith(":::", StringComparison.Ordinal))
        {
            arguments = string.Empty;
            return false;
        }

        var firstLineEnd = normalized.IndexOf('\n');
        var descriptor = firstLineEnd >= 0 ? normalized[..firstLineEnd] : normalized;
        var trimmedDescriptor = descriptor.Trim(':', ' ', '\t');
        if (trimmedDescriptor.StartsWith("mermaid", StringComparison.OrdinalIgnoreCase))
        {
            arguments = trimmedDescriptor["mermaid".Length..].TrimStart();
            return true;
        }

        if (!trimmedDescriptor.StartsWith("diagram", StringComparison.OrdinalIgnoreCase))
        {
            arguments = string.Empty;
            return false;
        }

        var remainder = trimmedDescriptor["diagram".Length..].TrimStart();
        if (!remainder.StartsWith("mermaid", StringComparison.OrdinalIgnoreCase))
        {
            arguments = string.Empty;
            return false;
        }

        arguments = remainder["mermaid".Length..].TrimStart();
        return true;
    }

    private static UIElement CreateLabeledField(string label, Control editor)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeights.SemiBold
                },
                editor
            }
        };
    }

    private UIElement CreateEditorChrome(string title, UIElement body)
    {
        var inlineMode = EditorPresentationMode == MarkdownEditorPresentationMode.Inline;
        var actionBar = CreateEditorActionBar(_activeEditorSession);
        return new Border
        {
            Background = inlineMode ? ToneBrush("#F8FAFC") : ToneBrush("#FFFFFF"),
            BorderBrush = ToneBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = inlineMode ? new Thickness(12) : new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = ToneBrush("#0F172A")
                    },
                    actionBar,
                    body
                }
            }
        };
    }

    private void CommitEditorReplacement(MarkdownEditorSession session, string replacementMarkdown)
    {
        var currentMarkdown = Markdown ?? string.Empty;
        var updatedMarkdown = MarkdownSourceEditing.Replace(currentMarkdown, session.SourceSpan, replacementMarkdown ?? string.Empty);
        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown ?? string.Empty,
            session.SourceSpan.Start,
            replacementMarkdown?.Length ?? 0,
            MarkdownEditOperation.Replace);
    }

    private void InsertEditorBlockBefore(MarkdownEditorSession session, MarkdownBlockTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var currentMarkdown = Markdown ?? string.Empty;
        var replacementMarkdown = MarkdownSourceEditing.BuildBlockInsertionReplacement(
            session.CurrentMarkdown,
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

    private void InsertEditorBlockAfter(MarkdownEditorSession session, MarkdownBlockTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var currentMarkdown = Markdown ?? string.Empty;
        var replacementMarkdown = MarkdownSourceEditing.BuildBlockInsertionReplacement(
            session.CurrentMarkdown,
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
        var currentMarkdown = Markdown ?? string.Empty;
        var updatedMarkdown = MarkdownSourceEditing.Remove(currentMarkdown, session.SourceSpan);
        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            string.Empty,
            Math.Clamp(session.SourceSpan.Start, 0, updatedMarkdown.Length),
            0,
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
        _activeEditorTextBox = null;
        if (string.Equals(currentMarkdown, updatedMarkdown, StringComparison.Ordinal))
        {
            QueueRefresh(MarkdownRefreshFlags.Materialize);
            return;
        }

        Markdown = updatedMarkdown;
        MarkdownEdited?.Invoke(this, new MarkdownEditedEventArgs(
            session,
            replacementMarkdown,
            updatedMarkdown,
            Math.Clamp(revealStart, 0, updatedMarkdown.Length),
            Math.Clamp(revealLength, 0, Math.Max(updatedMarkdown.Length - Math.Clamp(revealStart, 0, updatedMarkdown.Length), 0)),
            operation));
    }

    private static string ResolveEditorId(MarkdownBlockNode block)
    {
        return block switch
        {
            MarkdownHeadingBlock => "heading",
            MarkdownParagraphBlock => "paragraph",
            MarkdownListBlock => "list",
            MarkdownTableBlock => "table",
            MarkdownCodeBlock => "code",
            MarkdownYamlFrontMatterBlock => MarkdownBuiltInEditorIds.YamlFrontMatter,
            MarkdownAlertBlock => "alert",
            MarkdownCustomContainerBlock => "custom-container",
            MarkdownDefinitionListBlock => "definition-list",
            MarkdownFigureBlock => "figure",
            MarkdownLinkReferenceBlock => MarkdownBuiltInEditorIds.LinkReference,
            MarkdownAbbreviationBlock => MarkdownBuiltInEditorIds.Abbreviation,
            MarkdownFootnoteBlock => MarkdownBuiltInEditorIds.Footnote,
            MarkdownFooterBlock => "footer",
            MarkdownMathBlock => "math",
            MarkdownMermaidBlock => "mermaid",
            _ => "markdown"
        };
    }

    private static MarkdownEditorFeature ResolveEditorFeature(MarkdownBlockNode block)
    {
        return block switch
        {
            MarkdownHeadingBlock => MarkdownEditorFeature.Heading,
            MarkdownParagraphBlock => MarkdownEditorFeature.Paragraph,
            MarkdownListBlock => MarkdownEditorFeature.List,
            MarkdownTableBlock => MarkdownEditorFeature.Table,
            MarkdownCodeBlock => MarkdownEditorFeature.Code,
            MarkdownYamlFrontMatterBlock => MarkdownEditorFeature.YamlFrontMatter,
            MarkdownAlertBlock => MarkdownEditorFeature.Alert,
            MarkdownCustomContainerBlock => MarkdownEditorFeature.CustomContainer,
            MarkdownDefinitionListBlock => MarkdownEditorFeature.DefinitionList,
            MarkdownFigureBlock => MarkdownEditorFeature.Figure,
            MarkdownLinkReferenceBlock => MarkdownEditorFeature.LinkReference,
            MarkdownAbbreviationBlock => MarkdownEditorFeature.Abbreviation,
            MarkdownFootnoteBlock => MarkdownEditorFeature.Footnote,
            MarkdownFooterBlock => MarkdownEditorFeature.Footer,
            MarkdownMathBlock => MarkdownEditorFeature.Math,
            MarkdownMermaidBlock => MarkdownEditorFeature.Mermaid,
            _ => MarkdownEditorFeature.Paragraph
        };
    }

    private static string ResolveEditorTitle(MarkdownBlockNode block)
    {
        return block switch
        {
            MarkdownHeadingBlock heading => $"Edit heading h{heading.Level}",
            MarkdownParagraphBlock => "Edit paragraph",
            MarkdownListBlock => "Edit list block",
            MarkdownTableBlock => "Edit table block",
            MarkdownCodeBlock => "Edit fenced code block",
            MarkdownYamlFrontMatterBlock => "Edit YAML front matter",
            MarkdownAlertBlock alert => $"Edit {alert.Kind} alert",
            MarkdownCustomContainerBlock customContainer => $"Edit {customContainer.Kind} container",
            MarkdownDefinitionListBlock => "Edit definition list",
            MarkdownFigureBlock => "Edit figure block",
            MarkdownLinkReferenceBlock linkReferenceBlock => $"Edit link reference [{linkReferenceBlock.Label}]",
            MarkdownAbbreviationBlock abbreviationBlock => $"Edit abbreviation {abbreviationBlock.Label}",
            MarkdownFootnoteBlock footnoteBlock => $"Edit footnote {footnoteBlock.Label}",
            MarkdownFooterBlock => "Edit footer block",
            MarkdownMathBlock => "Edit math block",
            MarkdownMermaidBlock => "Edit Mermaid block",
            _ => "Edit markdown block"
        };
    }

    private UIElement CreateGenericCodeEditor(MarkdownEditorSession session, MarkdownCodeBlock codeBlock)
    {
        var languageBox = new TextBox
        {
            Text = codeBlock.LanguageHint ?? string.Empty,
            PlaceholderText = "language"
        };

        var editor = new TextBox
        {
            Text = codeBlock.Code,
            AcceptsReturn = true,
            TextWrapping = WinTextWrapping.NoWrap,
            MinHeight = 220,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = Math.Max(FontSize - 1, 12)
        };

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                languageBox,
                editor,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Button
                        {
                            Content = "Commit"
                        }.Also(button => button.Click += (_, _) => CommitEditorReplacement(
                            session,
                            MarkdownSourceEditing.BuildCodeFence(languageBox.Text, editor.Text))),
                        new Button
                        {
                            Content = "Cancel"
                        }.Also(button => button.Click += (_, _) => CancelEdit(MarkdownEditCancellationReason.UserRequested))
                    }
                }
            }
        };
    }

    private UIElement? TryCreateCodeEditor(MarkdownBlockNode block, MarkdownEditorSession session, MarkdownCodeBlock codeBlock)
    {
        if (EditorPreferences.TryGetPreferredEditor(MarkdownEditorFeature.Code, out var preferredEditorId) &&
            string.Equals(preferredEditorId, MarkdownBuiltInEditorIds.Code, StringComparison.Ordinal))
        {
            return null;
        }

        var editorType = Type.GetType(
            "CodexGui.Markdown.Plugin.TextMate.TextMateCodeEditorView, CodexGui.Markdown.Plugin.TextMate",
            throwOnError: false);
        if (editorType is null)
        {
            return null;
        }

        return Activator.CreateInstance(
            editorType,
            codeBlock.Code,
            codeBlock.LanguageHint,
            (Action<string, string?>)((updatedCode, updatedLanguageHint) =>
                CommitEditorReplacement(session, MarkdownSourceEditing.BuildCodeFence(updatedLanguageHint ?? string.Empty, updatedCode))),
            (Action)(() => CancelEdit(MarkdownEditCancellationReason.UserRequested))) as UIElement;
    }

    private UIElement RenderInlineBlock(MarkdownBlockNode block, MarkdownInlineBlockLayout? inlineLayout)
    {
        var effectiveLayout = inlineLayout;
        if (effectiveLayout is null)
        {
            var document = new MarkdownDocumentModel(_document?.ParseResult ?? MarkdownParseResult.Empty, [block]);
            var layout = _layoutService.Layout(document, ResolveAvailableWidth());
            effectiveLayout = layout.Blocks.FirstOrDefault() as MarkdownInlineBlockLayout;
        }

        if (effectiveLayout is null)
        {
            return new TextBlock
            {
                Text = string.Empty
            };
        }

        var lines = new StackPanel
        {
            Spacing = 0
        };

        foreach (var line in effectiveLayout.Lines)
        {
            var row = TryCreateInlineLineElement(line);
            if (row is not null)
            {
                lines.Children.Add(row);
                continue;
            }

            var fallbackRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0
            };

            foreach (var fragment in line.Fragments)
            {
                if (fragment.GapBefore > 0)
                {
                    fallbackRow.Children.Add(new Border
                    {
                        Width = fragment.GapBefore
                    });
                }

                fallbackRow.Children.Add(CreateInlineFragmentElement(fragment));
            }

            lines.Children.Add(fallbackRow);
        }

        if (block is MarkdownHeadingBlock headingBlock)
        {
            lines.Margin = headingBlock.Level == 1
                ? new Thickness(0, 8, 0, 4)
                : new Thickness(0, 4, 0, 2);
        }

        return lines;
    }

    private FrameworkElement? TryCreateInlineLineElement(MarkdownInlineLayoutLine line)
    {
        if (line.Fragments.Count == 0)
        {
            return null;
        }

        if (TryCreateUniformInlineTextBlock(line, out var uniformTextBlock))
        {
            return uniformTextBlock;
        }

        if (!CanUseInlineTextBlock(line))
        {
            return null;
        }

        var textBlock = new TextBlock
        {
            TextWrapping = WinTextWrapping.NoWrap,
            IsTextSelectionEnabled = true
        };

        foreach (var fragment in line.Fragments)
        {
            AppendStyledInline(textBlock.Inlines, fragment.LeadingWhitespace, fragment.Style, linkTarget: null);
            AppendStyledInline(textBlock.Inlines, fragment.Text, fragment.Style, fragment.LinkTarget);
        }

        return textBlock;
    }

    private bool TryCreateUniformInlineTextBlock(MarkdownInlineLayoutLine line, out TextBlock? textBlock)
    {
        textBlock = null;
        var first = line.Fragments[0];
        if (!IsPlainInlineFragment(first) || !string.IsNullOrWhiteSpace(first.LinkTarget))
        {
            return false;
        }

        for (var index = 1; index < line.Fragments.Count; index++)
        {
            var current = line.Fragments[index];
            if (!IsPlainInlineFragment(current) ||
                !string.IsNullOrWhiteSpace(current.LinkTarget) ||
                !string.Equals(current.SemanticClass, first.SemanticClass, StringComparison.Ordinal) ||
                current.Style != first.Style)
            {
                return false;
            }
        }

        textBlock = CreateTextBlock(line.Text, first.Style, linkTarget: null);
        return true;
    }

    private static bool CanUseInlineTextBlock(MarkdownInlineLayoutLine line)
    {
        foreach (var fragment in line.Fragments)
        {
            if (!IsPlainInlineFragment(fragment))
            {
                return false;
            }

            if ((fragment.Style.Underline || fragment.Style.Strikethrough) &&
                string.IsNullOrWhiteSpace(fragment.LinkTarget))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlainInlineFragment(MarkdownInlineLayoutFragment fragment)
    {
        return !string.Equals(fragment.SemanticClass, "image", StringComparison.Ordinal) &&
               !string.Equals(fragment.SemanticClass, "math", StringComparison.Ordinal) &&
               !fragment.Style.Code &&
               !fragment.Style.Marked &&
               !fragment.Style.Inserted &&
               !fragment.Style.Superscript &&
               !fragment.Style.Subscript;
    }

    private void AppendStyledInline(InlineCollection inlines, string text, MarkdownTextStyle style, string? linkTarget)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var run = new Run
        {
            Text = text
        };
        ApplyTextElementStyle(run, style, linkTarget);

        if (!string.IsNullOrWhiteSpace(linkTarget))
        {
            var hyperlink = new Hyperlink();
            hyperlink.Inlines.Add(run);
            hyperlink.Click += (_, _) => _ = LaunchLinkAsync(linkTarget);
            inlines.Add(hyperlink);
            return;
        }

        inlines.Add(run);
    }

    private void ApplyTextElementStyle(TextElement element, MarkdownTextStyle style, string? linkTarget)
    {
        element.FontFamily = new FontFamily(style.Code ? "Cascadia Mono" : (FontFamily?.Source ?? "Segoe UI"));
        element.FontSize = style.Code
            ? Math.Max(FontSize - 1, 12)
            : (style.Superscript || style.Subscript ? Math.Max(FontSize - 2, 10) : FontSize);
        element.FontWeight = style.Bold ? FontWeights.SemiBold : FontWeights.Normal;
        element.FontStyle = style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        element.Foreground = ResolveForeground(style, linkTarget);
    }

    private FrameworkElement CreateInlineFragmentElement(MarkdownInlineLayoutFragment fragment)
    {
        if (string.Equals(fragment.SemanticClass, "image", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(fragment.LinkTarget))
        {
            return CreateImageFragmentElement(fragment);
        }

        if (string.Equals(fragment.SemanticClass, "math", StringComparison.Ordinal))
        {
            return (FrameworkElement)MarkdownMathViewFactory.CreateInlineView(fragment.Text, FontSize, Foreground as Brush);
        }

        if (fragment.Style.Code)
        {
            return new Border
            {
                Background = ToneBrush("#F1F5F9"),
                BorderBrush = ToneBrush("#CBD5E1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Child = CreateTextBlock(fragment.Text, fragment.Style, fragment.LinkTarget)
            };
        }

        if (fragment.Style.Marked || fragment.Style.Inserted)
        {
            return new Border
            {
                Background = fragment.Style.Marked ? ToneBrush("#FFF1B8") : ToneBrush("#F0FDF4"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 0, 2, 0),
                Child = CreateTextBlock(fragment.Text, fragment.Style, fragment.LinkTarget)
            };
        }

        return CreateTextBlock(fragment.Text, fragment.Style, fragment.LinkTarget);
    }

    private FrameworkElement CreateImageFragmentElement(MarkdownInlineLayoutFragment fragment)
    {
        var host = new Border
        {
            Background = ToneBrush("#F8FAFC"),
            BorderBrush = ToneBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Width = Math.Max(fragment.Width + 18, 136),
            Height = 96
        };

        var fallback = new TextBlock
        {
            Text = fragment.Text,
            TextWrapping = WinTextWrapping.Wrap,
            Foreground = ToneBrush("#475569"),
            IsTextSelectionEnabled = true
        };

        if (!MarkdownUriUtilities.TryResolveUri(BaseUri, fragment.LinkTarget, out var uri) || uri is null || !uri.IsAbsoluteUri)
        {
            host.Child = fallback;
            return host;
        }

        var bitmap = new BitmapImage
        {
            UriSource = uri
        };

        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform
        };
        image.ImageFailed += (_, _) => host.Child = fallback;
        host.Child = image;

        return host;
    }

    private TextBlock CreateTextBlock(string text, MarkdownTextStyle style, string? linkTarget)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = WinTextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
            Margin = style.Superscript
                ? new Thickness(0, -4, 0, 0)
                : (style.Subscript ? new Thickness(0, 4, 0, 0) : new Thickness(0))
        };
        ApplyTextBlockStyle(textBlock, style, linkTarget);

        if (!string.IsNullOrWhiteSpace(linkTarget))
        {
            textBlock.PointerReleased += OnLinkPointerReleased;
            textBlock.Tag = linkTarget;
        }

        return textBlock;
    }

    private void ApplyTextBlockStyle(TextBlock textBlock, MarkdownTextStyle style, string? linkTarget)
    {
        textBlock.FontFamily = new FontFamily(style.Code ? "Cascadia Mono" : (FontFamily?.Source ?? "Segoe UI"));
        textBlock.FontSize = style.Code
            ? Math.Max(FontSize - 1, 12)
            : (style.Superscript || style.Subscript ? Math.Max(FontSize - 2, 10) : FontSize);
        textBlock.FontWeight = style.Bold ? FontWeights.SemiBold : FontWeights.Normal;
        textBlock.FontStyle = style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        textBlock.Foreground = ResolveForeground(style, linkTarget);
    }

    private UIElement RenderCodeBlock(MarkdownCodeBlock codeBlock, MarkdownCodeBlockLayout? blockLayout)
    {
        var effectiveLayout = blockLayout;
        if (effectiveLayout is null)
        {
            var document = new MarkdownDocumentModel(_document?.ParseResult ?? MarkdownParseResult.Empty, [codeBlock]);
            var layout = _layoutService.Layout(document, ResolveAvailableWidth());
            effectiveLayout = layout.Blocks.OfType<MarkdownCodeBlockLayout>().FirstOrDefault();
        }

        if (effectiveLayout is null)
        {
            return new TextBlock
            {
                Text = codeBlock.Code
            };
        }

        var body = new StackPanel
        {
            Spacing = 2
        };

        foreach (var line in effectiveLayout.Lines)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = WinTextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = Math.Max(FontSize - 1, 12),
                IsTextSelectionEnabled = true
            };

            foreach (var run in line.Runs)
            {
                textBlock.Inlines.Add(new Run
                {
                    Text = run.Text,
                    Foreground = ResolveForeground(run.Style, null),
                    FontWeight = run.Style.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                    FontStyle = run.Style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal
                });
            }

            body.Children.Add(textBlock);
        }

        return new Border
        {
            Background = ToneBrush("#F8FAFC"),
            BorderBrush = ToneBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = GridLength.Auto },
                            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                        },
                        Children =
                        {
                            new TextBlock
                            {
                                Text = FormatLanguageLabel(codeBlock.LanguageHint),
                                FontWeight = FontWeights.SemiBold,
                                Foreground = ToneBrush("#334155")
                            },
                            new TextBlock
                            {
                                Text = effectiveLayout.Engine,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Foreground = ToneBrush("#64748B")
                            }.Also(static meta => Grid.SetColumn(meta, 1))
                        }
                    },
                    new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = body
                    }
                }
            }
        };
    }

    private UIElement RenderYamlFrontMatterBlock(MarkdownYamlFrontMatterBlock yamlFrontMatterBlock)
    {
        return RenderCodeBlock(
            new MarkdownCodeBlock(yamlFrontMatterBlock.Yaml, "yaml", yamlFrontMatterBlock.SourceSpan),
            blockLayout: null);
    }

    private UIElement RenderLinkReferenceBlock(MarkdownLinkReferenceBlock linkReferenceBlock)
    {
        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = linkReferenceBlock.Url,
                    FontFamily = new FontFamily("Cascadia Mono"),
                    Foreground = ToneBrush("#2563EB"),
                    IsTextSelectionEnabled = true
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(linkReferenceBlock.Title))
        {
            body.Children.Add(new TextBlock
            {
                Text = linkReferenceBlock.Title,
                Foreground = ToneBrush("#64748B"),
                TextWrapping = WinTextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });
        }

        return CreateMetadataCard($"[{linkReferenceBlock.Label}]", "Reference definition", body);
    }

    private UIElement RenderAbbreviationBlock(MarkdownAbbreviationBlock abbreviationBlock)
    {
        return CreateMetadataCard(
            abbreviationBlock.Label,
            "Abbreviation definition",
            new TextBlock
            {
                Text = abbreviationBlock.Meaning,
                TextWrapping = WinTextWrapping.Wrap,
                Foreground = ToneBrush("#0F172A"),
                IsTextSelectionEnabled = true
            });
    }

    private UIElement RenderFootnoteBlock(MarkdownFootnoteBlock footnoteBlock)
    {
        return CreateMetadataCard(
            $"[{footnoteBlock.Order}] {footnoteBlock.Label}",
            "Footnote",
            RenderNestedMarkdown(footnoteBlock.BodyMarkdown, horizontalPadding: 20));
    }

    private UIElement CreateMetadataCard(string title, string subtitle, UIElement body)
    {
        return new Border
        {
            Background = ToneBrush("#FFFFFF"),
            BorderBrush = ToneBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = ToneBrush("#0F172A"),
                        IsTextSelectionEnabled = true
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        Foreground = ToneBrush("#64748B"),
                        IsTextSelectionEnabled = true
                    },
                    body
                }
            }
        };
    }

    private UIElement RenderQuoteBlock(MarkdownQuoteBlock quoteBlock)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };

        foreach (var child in quoteBlock.Blocks)
        {
            if (RenderBlock(child, blockLayout: null, allowEditing: false) is { } element)
            {
                stack.Children.Add(element);
            }
        }

        return new Border
        {
            Background = ToneBrush("#F8FAFC"),
            BorderBrush = ToneBrush("#0EA5E9"),
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 8, 0, 8),
            Child = stack
        };
    }

    private UIElement RenderListBlock(MarkdownListBlock listBlock)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };

        foreach (var item in listBlock.Items)
        {
            var content = new StackPanel
            {
                Spacing = 6
            };

            foreach (var block in item.Blocks)
            {
                if (RenderBlock(block, blockLayout: null, allowEditing: false) is { } child)
                {
                    content.Children.Add(child);
                }
            }

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                },
                ColumnSpacing = 10
            };

            row.Children.Add(new TextBlock
            {
                Text = item.Marker,
                FontWeight = FontWeights.SemiBold,
                Foreground = ToneBrush("#0F172A"),
                VerticalAlignment = VerticalAlignment.Top
            });

            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            stack.Children.Add(row);
        }

        return stack;
    }

    private UIElement RenderTableBlock(MarkdownTableBlock tableBlock)
    {
        var grid = new Grid
        {
            BorderBrush = ToneBrush("#CBD5E1"),
            BorderThickness = new Thickness(1)
        };

        var columnCount = tableBlock.Rows.Count == 0 ? 0 : tableBlock.Rows.Max(static row => row.Cells.Count);
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var rowIndex = 0; rowIndex < tableBlock.Rows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = tableBlock.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                var cell = new Border
                {
                    Background = row.IsHeader ? ToneBrush("#F8FAFC") : ToneBrush("#FFFFFF"),
                    BorderBrush = ToneBrush("#CBD5E1"),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(8),
                    Child = new MarkdownThreadMessageView
                    {
                        Markdown = row.Cells[columnIndex],
                        BaseUri = BaseUri,
                        FontSize = FontSize,
                        Foreground = Foreground,
                        TextWrapping = WinTextWrapping.Wrap
                    }
                };

                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private UIElement RenderAlertBlock(MarkdownAlertBlock alertBlock)
    {
        return MarkdownAlertViewFactory.CreateBlockView(alertBlock, RenderNestedMarkdown, FontSize);
    }

    private UIElement RenderCustomContainerBlock(MarkdownCustomContainerBlock customContainerBlock)
    {
        return MarkdownCustomContainerViewFactory.CreateBlockView(customContainerBlock, RenderNestedMarkdown, FontSize);
    }

    private UIElement RenderDefinitionListBlock(MarkdownDefinitionListBlock definitionListBlock)
    {
        return MarkdownDefinitionListViewFactory.CreateBlockView(definitionListBlock, RenderNestedMarkdown, FontSize);
    }

    private UIElement RenderFigureBlock(MarkdownFigureBlock figureBlock)
    {
        return MarkdownFigureViewFactory.CreateBlockView(figureBlock, RenderNestedMarkdown, FontSize);
    }

    private UIElement RenderFooterBlock(MarkdownFooterBlock footerBlock)
    {
        return MarkdownFooterViewFactory.CreateBlockView(footerBlock, RenderNestedMarkdown, FontSize);
    }

    private UIElement RenderMathBlock(MarkdownMathBlock mathBlock)
    {
        return MarkdownMathViewFactory.CreateBlockView(mathBlock.Expression, FontSize, Foreground as Brush);
    }

    private UIElement RenderMermaidBlock(MarkdownMermaidBlock mermaidBlock)
    {
        return MarkdownMermaidViewFactory.CreateBlockView(
            mermaidBlock,
            ResolveAvailableWidth(),
            FontSize,
            Foreground as Brush);
    }

    private UIElement RenderFormulaBlock(string body, string title, string accent, string background)
    {
        return CreateCard(title, body, accent, background, monospace: true);
    }

    private UIElement CreateCard(string title, string body, string accent, string background, bool monospace = false)
    {
        return new Border
        {
            Background = ToneBrush(background),
            BorderBrush = ToneBrush(accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = ToneBrush(accent)
                    },
                    monospace
                        ? new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            Content = new TextBlock
                            {
                                Text = body,
                                FontFamily = new FontFamily("Cascadia Mono"),
                                TextWrapping = WinTextWrapping.NoWrap,
                                Foreground = ToneBrush("#0F172A")
                            }
                        }
                        : RenderNestedMarkdown(body, horizontalPadding: 24)
                }
            }
        };
    }

    private UIElement RenderNestedMarkdown(string markdown, double horizontalPadding = 0)
    {
        return CreateNestedRendererView(markdown, horizontalPadding);
    }

    private async void OnLinkPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string linkTarget })
        {
            return;
        }

        await LaunchLinkAsync(linkTarget);
    }

    private async Task LaunchLinkAsync(string linkTarget)
    {
        if (!MarkdownUriUtilities.TryResolveUri(BaseUri, linkTarget, out var uri) || uri is null)
        {
            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch
        {
        }
    }

    private Brush ResolveForeground(MarkdownTextStyle style, string? linkTarget)
    {
        if (!string.IsNullOrWhiteSpace(style.Foreground))
        {
            return ToneBrush(style.Foreground);
        }

        if (!string.IsNullOrWhiteSpace(linkTarget))
        {
            return ToneBrush("#2563EB");
        }

        return Foreground as Brush ?? ToneBrush("#0F172A");
    }

    internal static SolidColorBrush ResolveSharedBrush(string hex)
    {
        return BrushCache.GetOrAdd(hex, static value => new SolidColorBrush(ColorHelper.FromArgb(
            ParseChannel(value, 0),
            ParseChannel(value, 1),
            ParseChannel(value, 2),
            ParseChannel(value, 3))));
    }

    private static SolidColorBrush ToneBrush(string hex)
    {
        return ResolveSharedBrush(hex);
    }

    private static byte ParseChannel(string hex, int channel)
    {
        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length == 6)
        {
            normalized = $"FF{normalized}";
        }

        return Convert.ToByte(normalized.Substring(channel * 2, 2), 16);
    }

    private static string FormatLanguageLabel(string? languageHint)
    {
        return MarkdownSourceEditing.NormalizeLanguageHint(languageHint) switch
        {
            "c#" or "cs" or "csharp" => "C#",
            "json" or "jsonc" => "JSON",
            "xml" => "XML",
            "xaml" or "axaml" => "XAML",
            "md" or "markdown" => "Markdown",
            "bash" or "sh" or "shell" => "Shell",
            "py" or "python" => "Python",
            "ts" or "typescript" => "TypeScript",
            "tsx" => "TSX",
            "js" or "javascript" => "JavaScript",
            var other when !string.IsNullOrWhiteSpace(other) => other.ToUpperInvariant(),
            _ => "Code"
        };
    }

    private UIElement CreateEditorActionBar(MarkdownEditorSession? session)
    {
        if (session is null)
        {
            return new Grid();
        }

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        foreach (var button in CreateFormattingButtons())
        {
            actionRow.Children.Add(button);
        }

        var templates = MarkdownEditorCatalog.GetTemplates(session.Feature);
        if (templates.Count > 0)
        {
            var templateBox = new ComboBox
            {
                ItemsSource = templates,
                DisplayMemberPath = nameof(MarkdownBlockTemplate.Label),
                PlaceholderText = "Insert block…",
                MinWidth = 168
            };

            actionRow.Children.Add(templateBox);
            actionRow.Children.Add(new Button
            {
                Content = "Before",
                MinWidth = 72
            }.Also(button => button.Click += (_, _) =>
            {
                if (templateBox.SelectedItem is MarkdownBlockTemplate template)
                {
                    InsertEditorBlockBefore(session, template);
                }
            }));
            actionRow.Children.Add(new Button
            {
                Content = "After",
                MinWidth = 72
            }.Also(button => button.Click += (_, _) =>
            {
                if (templateBox.SelectedItem is MarkdownBlockTemplate template)
                {
                    InsertEditorBlockAfter(session, template);
                }
            }));
        }

        actionRow.Children.Add(new Button
        {
            Content = "Remove",
            MinWidth = 72,
            Foreground = ToneBrush("#B42318")
        }.Also(button => button.Click += (_, _) => RemoveEditorBlock(session)));

        return actionRow;
    }

    private IEnumerable<Button> CreateFormattingButtons()
    {
        if (_activeEditorSession is null ||
            _activeEditorSession.Feature is MarkdownEditorFeature.Code or MarkdownEditorFeature.Math or MarkdownEditorFeature.Mermaid)
        {
            yield break;
        }

        yield return CreateFormattingButton("Bold", "**", "**");
        yield return CreateFormattingButton("Italic", "_", "_");
        yield return CreateFormattingButton("Code", "`", "`");
        yield return new Button
        {
            Content = "Link",
            MinWidth = 56
        }.Also(button => button.Click += (_, _) => ApplyLinkFormatting());
    }

    private Button CreateFormattingButton(string label, string prefix, string suffix)
    {
        return new Button
        {
            Content = label,
            MinWidth = 56
        }.Also(button => button.Click += (_, _) => ApplyWrapperFormatting(prefix, suffix));
    }

    private void ApplyWrapperFormatting(string prefix, string suffix)
    {
        if (ResolveFormattingTarget() is not { } textBox)
        {
            return;
        }

        var text = textBox.Text ?? string.Empty;
        var (start, end) = ResolveFormattingRange(textBox, text);
        var selectedText = end > start ? text[start..end] : string.Empty;
        var replacement = string.Concat(prefix, selectedText, suffix);
        textBox.Text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));

        var caretIndex = start + prefix.Length;
        if (selectedText.Length > 0)
        {
            textBox.SelectionStart = caretIndex;
            textBox.SelectionLength = selectedText.Length;
        }
        else
        {
            textBox.SelectionStart = caretIndex;
            textBox.SelectionLength = 0;
        }
    }

    private void ApplyLinkFormatting()
    {
        if (ResolveFormattingTarget() is not { } textBox)
        {
            return;
        }

        var text = textBox.Text ?? string.Empty;
        var (start, end) = ResolveFormattingRange(textBox, text);
        var selectedText = end > start ? text[start..end] : string.Empty;
        var replacement = $"[{selectedText}]()";
        textBox.Text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));

        var caretIndex = selectedText.Length > 0 ? start + selectedText.Length + 3 : start + 1;
        textBox.SelectionStart = caretIndex;
        textBox.SelectionLength = 0;
    }

    private TextBox? ResolveFormattingTarget()
    {
        if (_activeEditorTextBox is not null)
        {
            return _activeEditorTextBox;
        }

        return Content is FrameworkElement root ? FindDescendantTextBox(root) : null;
    }

    private MarkdownBlockNode? TryResolveBlock(MarkdownSourceSpan sourceSpan)
    {
        return sourceSpan.IsEmpty || _document is null
            ? null
            : _document.Blocks.FirstOrDefault(block => block.SourceSpan == sourceSpan);
    }

    private bool TryBeginInlineMathEdit(MarkdownSourceSpan sourceSpan)
    {
        if (_document is null || sourceSpan.IsEmpty)
        {
            return false;
        }

        var hostBlock = _document.Blocks.FirstOrDefault(block =>
            !block.SourceSpan.IsEmpty &&
            block.SourceSpan.Contains(sourceSpan.Start) &&
            block.SourceSpan.Contains(Math.Max(sourceSpan.EndExclusive - 1, sourceSpan.Start)));
        if (hostBlock is null)
        {
            return false;
        }

        _activeEditorTextBox = null;
        _activeEditorSession = new MarkdownEditorSession(
            MathMarkdownEditorIds.Inline,
            MarkdownEditorFeature.Math,
            sourceSpan,
            "Edit inline math",
            "MathInline",
            MarkdownSourceEditing.NormalizeInlineMath(sourceSpan.Slice(Markdown ?? string.Empty)),
            hostBlock.SourceSpan);
        QueueRefresh(MarkdownRefreshFlags.Materialize);
        return true;
    }

    private bool TryHitTestBlockHost(Point point, out MarkdownHitRegion? hitRegion)
    {
        foreach (var entry in _blockHosts)
        {
            if (!TryGetRelativeBounds(entry.Host, out var bounds) || !bounds.Contains(point))
            {
                continue;
            }

            hitRegion = new MarkdownHitRegion(
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                entry.Block.SourceSpan,
                LinkTarget: null,
                Kind: "block");
            return true;
        }

        hitRegion = null;
        return false;
    }

    private bool TryGetRelativeBounds(FrameworkElement element, out Rect bounds)
    {
        if (element.TransformToVisual(this) is { } transform)
        {
            var topLeft = transform.TransformPoint(new Point(0, 0));
            bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
            return bounds.Width >= 0 && bounds.Height >= 0;
        }

        bounds = default;
        return false;
    }

    private static TextBox? FindDescendantTextBox(FrameworkElement root)
    {
        if (root is TextBox textBox)
        {
            return textBox;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            if (VisualTreeHelper.GetChild(root, index) is FrameworkElement child &&
                FindDescendantTextBox(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static (int Start, int End) ResolveFormattingRange(TextBox textBox, string text)
    {
        var selectionStart = Math.Clamp(textBox.SelectionStart, 0, text.Length);
        var selectionEnd = Math.Clamp(textBox.SelectionStart + textBox.SelectionLength, 0, text.Length);
        if (selectionEnd > selectionStart)
        {
            return (selectionStart, selectionEnd);
        }

        return TryExpandSelectionToWord(text, selectionStart, out var wordStart, out var wordEnd)
            ? (wordStart, wordEnd)
            : (selectionStart, selectionEnd);
    }

    private static bool TryExpandSelectionToWord(string text, int caretIndex, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var index = Math.Clamp(caretIndex, 0, text.Length);
        if (index == text.Length || !IsFormattingWordCharacter(text[index]))
        {
            if (index > 0 && IsFormattingWordCharacter(text[index - 1]))
            {
                index--;
            }
            else
            {
                return false;
            }
        }

        start = index;
        end = index + 1;
        while (start > 0 && IsFormattingWordCharacter(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length && IsFormattingWordCharacter(text[end]))
        {
            end++;
        }

        return end > start;
    }

    private static bool IsFormattingWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '-';
    }
}

public enum MarkdownEditOperation
{
    Replace,
    InsertBlockBefore,
    InsertBlockAfter,
    RemoveBlock
}

public sealed class MarkdownEditorSession(
    string editorId,
    MarkdownEditorFeature feature,
    MarkdownSourceSpan sourceSpan,
    string title,
    string nodeKind,
    string currentMarkdown,
    MarkdownSourceSpan? hostSourceSpan = null)
{
    public string EditorId { get; } = editorId;

    public MarkdownEditorFeature Feature { get; } = feature;

    public MarkdownSourceSpan SourceSpan { get; } = sourceSpan;

    public string Title { get; } = title;

    public string NodeKind { get; } = nodeKind;

    public string CurrentMarkdown { get; } = currentMarkdown;

    public MarkdownSourceSpan HostSourceSpan { get; } = hostSourceSpan ?? sourceSpan;
}

public sealed class MarkdownEditedEventArgs(
    MarkdownEditorSession session,
    string replacementMarkdown,
    string updatedMarkdown,
    int revealStart,
    int revealLength,
    MarkdownEditOperation operation) : EventArgs
{
    public MarkdownEditorSession Session { get; } = session;

    public string ReplacementMarkdown { get; } = replacementMarkdown;

    public string UpdatedMarkdown { get; } = updatedMarkdown;

    public int RevealStart { get; } = revealStart;

    public int RevealLength { get; } = revealLength;

    public MarkdownEditOperation Operation { get; } = operation;
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

internal static class FrameworkElementExtensions
{
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
