using CodexGui.Markdown.Core;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pretext;
using Pretext.Uno.Controls;
using Windows.System;
using WinTextWrapping = Microsoft.UI.Xaml.TextWrapping;

namespace CodexGui.Markdown.Controls;

public sealed class MarkdownThreadMessageView : UserControl
{
    private const double BlockGap = 10;
    private const double HardBreakGap = 4;
    private const double ListIndent = 20;
    private const double ListMarkerGap = 10;
    private const double BlockquoteIndent = 18;
    private const double RailOffset = 6;
    private const double RuleHeight = 14;
    private const double CodePaddingX = 12;
    private const double CodePaddingY = 10;
    private const double CodeExtraWidth = 12;
    private const double ChipExtraWidth = 14;
    private const double HeadingOneScale = 1.42;
    private const double HeadingTwoScale = 1.18;
    private const double BodyLineHeightRatio = 1.45;
    private const double HeadingLineHeightRatio = 1.3;

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownThreadMessageView),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    public static readonly DependencyProperty BaseUriProperty =
        DependencyProperty.Register(
            nameof(BaseUri),
            typeof(Uri),
            typeof(MarkdownThreadMessageView),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(
            nameof(TextWrapping),
            typeof(WinTextWrapping),
            typeof(MarkdownThreadMessageView),
            new PropertyMetadata(WinTextWrapping.Wrap, OnRenderPropertyChanged));

    private readonly MarkdownDocumentBuilder _documentBuilder;
    private readonly MarkdownLayoutService _layoutService;
    private readonly Grid _host;
    private readonly UiRenderScheduler _renderScheduler;

    private IReadOnlyList<PreparedBlock>? _preparedBlocks;
    private IReadOnlyList<MarkdownBlockNode>? _blocksSource;
    private int _preparedVersion;
    private int _renderedVersion = -1;
    private bool _refreshQueued;
    private bool _renderRequested;
    private bool _renderScheduled;
    private double _lastKnownWidth = -1;
    private double _lastRenderedWidth = -1;

    public MarkdownThreadMessageView()
    {
        var registry = MarkdownRuntimeConfiguration.Snapshot();
        _documentBuilder = new MarkdownDocumentBuilder(registry);
        _layoutService = new MarkdownLayoutService(registry);
        _host = new Grid();
        _renderScheduler = new UiRenderScheduler(DispatcherQueue, RenderPreparedBlocks);

        Content = _host;
        Loaded += (_, _) =>
        {
            if (_preparedBlocks is not null)
            {
                ScheduleRender();
            }
            else
            {
                QueueRefresh();
            }
        };
        SizeChanged += OnSizeChanged;
    }

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public IReadOnlyList<MarkdownBlockNode>? BlocksSource
    {
        get => _blocksSource;
        set
        {
            if (ReferenceEquals(_blocksSource, value))
            {
                return;
            }

            _blocksSource = value;
            if (value is not null)
            {
                _preparedBlocks = BuildPreparedBlocks(string.Empty, value);
                _preparedVersion++;
                _refreshQueued = false;
                ScheduleRender();
                return;
            }

            _preparedBlocks = null;
            QueueRefresh();
        }
    }

    public new Uri? BaseUri
    {
        get => (Uri?)GetValue(BaseUriProperty);
        set => SetValue(BaseUriProperty, value);
    }

    public WinTextWrapping TextWrapping
    {
        get => (WinTextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    private static void OnRenderPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (MarkdownThreadMessageView)dependencyObject;
        if (args.Property == MarkdownProperty)
        {
            control.QueueRefresh();
            return;
        }

        control.ScheduleRender();
    }

    private void QueueRefresh()
    {
        if (_blocksSource is not null)
        {
            _preparedBlocks = BuildPreparedBlocks(string.Empty, _blocksSource);
            _refreshQueued = false;
            ScheduleRender();
            return;
        }

        _refreshQueued = true;
        if (IsLoaded)
        {
            RefreshAsync();
        }
    }

    private async void RefreshAsync()
    {
        await Task.Yield();

        while (IsLoaded && _refreshQueued)
        {
            _refreshQueued = false;
            var blockSourceSnapshot = _blocksSource;
            var markdownSnapshot = Markdown ?? string.Empty;
            var blocks = await Task.Run(() => BuildPreparedBlocks(markdownSnapshot, blockSourceSnapshot));

            if (!IsLoaded)
            {
                return;
            }

            if (!ReferenceEquals(blockSourceSnapshot, _blocksSource))
            {
                _refreshQueued = true;
                continue;
            }

            if (blockSourceSnapshot is null &&
                !string.Equals(markdownSnapshot, Markdown ?? string.Empty, StringComparison.Ordinal))
            {
                _refreshQueued = true;
                continue;
            }

            _preparedBlocks = blocks;
            _preparedVersion++;
            ScheduleRender();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!TryResolveAvailableWidth(args.NewSize.Width, out var width))
        {
            return;
        }

        if (Math.Abs(width - _lastKnownWidth) <= 0.5)
        {
            return;
        }

        _lastKnownWidth = width;
        ScheduleRender();
    }

    private void ScheduleRender()
    {
        _renderRequested = true;
        if (!IsLoaded || _renderScheduled)
        {
            return;
        }

        _renderScheduled = true;
        _renderScheduler.Schedule();
    }

    private void RenderPreparedBlocks()
    {
        _renderScheduled = false;
        if (!IsLoaded)
        {
            return;
        }

        if (!_renderRequested)
        {
            return;
        }

        _renderRequested = false;

        if (_preparedBlocks is null)
        {
            _host.Children.Clear();
            return;
        }

        if (!TryResolveAvailableWidth(out var availableWidth))
        {
            _renderRequested = true;
            return;
        }

        if (_renderedVersion == _preparedVersion && Math.Abs(_lastRenderedWidth - availableWidth) <= 0.5)
        {
            return;
        }

        var stack = new StackPanel
        {
            Spacing = 0
        };

        foreach (var block in _preparedBlocks)
        {
            var element = RenderBlock(block, availableWidth);
            if (element is null)
            {
                continue;
            }

            element.Margin = new Thickness(0, block.MarginTop, 0, 0);
            stack.Children.Add(element);
        }

        _host.Children.Clear();
        _host.Children.Add(stack);
        _renderedVersion = _preparedVersion;
        _lastRenderedWidth = availableWidth;

        if (_renderRequested)
        {
            ScheduleRender();
        }
    }

    private IReadOnlyList<PreparedBlock> BuildPreparedBlocks(string markdown, IReadOnlyList<MarkdownBlockNode>? blocksSource)
    {
        var prepared = new List<PreparedBlock>(blocksSource?.Count ?? 8);
        if (blocksSource is not null)
        {
            AppendPreparedBlocks(prepared, blocksSource, new RenderContext(0, 0));
            return prepared;
        }

        var document = _documentBuilder.Build(markdown);
        AppendPreparedBlocks(prepared, document.Blocks, new RenderContext(0, 0));
        return prepared;
    }

    private void AppendPreparedBlocks(List<PreparedBlock> target, IReadOnlyList<MarkdownBlockNode> blocks, RenderContext context)
    {
        var isFirst = true;
        foreach (var block in blocks)
        {
            var marginTop = isFirst ? 0 : BlockGap;
            isFirst = false;

            switch (block)
            {
                case MarkdownParagraphBlock paragraph:
                    AppendInlineGroup(target, paragraph.Flows, BlockVariant.Body, context, marginTop);
                    break;

                case MarkdownHeadingBlock heading:
                    AppendInlineGroup(
                        target,
                        heading.Flows,
                        heading.Level <= 1 ? BlockVariant.Heading1 : heading.Level == 2 ? BlockVariant.Heading2 : BlockVariant.Body,
                        context,
                        marginTop + 2);
                    break;

                case MarkdownCodeBlock code:
                    target.Add(CreatePreparedCodeBlock(CreateDecoration(context, marginTop), code.Code, code.LanguageHint));
                    break;

                case MarkdownYamlFrontMatterBlock yaml:
                    target.Add(CreatePreparedCodeBlock(CreateDecoration(context, marginTop), yaml.Yaml, "yaml"));
                    break;

                case MarkdownRuleBlock:
                    target.Add(new PreparedRuleBlock(CreateDecoration(context, marginTop), RuleHeight));
                    break;

                case MarkdownQuoteBlock quote:
                {
                    var startIndex = target.Count;
                    AppendPreparedBlocks(target, quote.Blocks, new RenderContext(context.ListDepth, context.QuoteDepth + 1));
                    ApplyFirstMargin(target, startIndex, marginTop);
                    break;
                }

                case MarkdownListBlock list:
                    AppendListBlocks(target, list, context, marginTop);
                    break;

                default:
                    target.Add(new PreparedNodeBlock(CreateDecoration(context, marginTop), block));
                    break;
            }
        }
    }

    private void AppendListBlocks(List<PreparedBlock> target, MarkdownListBlock list, RenderContext context, double firstMargin)
    {
        var itemContext = new RenderContext(context.ListDepth + 1, context.QuoteDepth);
        var isFirstItem = true;
        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            var startIndex = target.Count;
            AppendPreparedBlocks(target, item.Blocks, itemContext);
            if (target.Count == startIndex)
            {
                continue;
            }

            var firstBlockIndex = startIndex;
            target[firstBlockIndex] = target[firstBlockIndex] with
            {
                MarginTop = isFirstItem ? firstMargin : BlockGap
            };
            DecorateListItem(target, firstBlockIndex, item.Marker);
            isFirstItem = false;
        }
    }

    private static void ApplyFirstMargin(List<PreparedBlock> target, int startIndex, double marginTop)
    {
        if (startIndex < 0 || startIndex >= target.Count)
        {
            return;
        }

        target[startIndex] = target[startIndex] with { MarginTop = marginTop };
    }

    private void AppendInlineGroup(
        List<PreparedBlock> target,
        IReadOnlyList<MarkdownInlineFlow> flows,
        BlockVariant variant,
        RenderContext context,
        double firstMargin)
    {
        var isFirst = true;
        foreach (var flow in flows)
        {
            var block = BuildInlineBlock(flow, variant, context, isFirst ? firstMargin : HardBreakGap);
            if (block is not null)
            {
                target.Add(block);
                isFirst = false;
            }
        }
    }

    private PreparedInlineBlock? BuildInlineBlock(
        MarkdownInlineFlow flow,
        BlockVariant variant,
        RenderContext context,
        double marginTop)
    {
        var pieces = BuildInlinePieces(flow, variant);
        if (pieces.Count == 0)
        {
            return null;
        }

        var richItems = new RichInlineItem[pieces.Count];
        var classNames = new string[pieces.Count];
        var hrefs = new string?[pieces.Count];
        for (var index = 0; index < pieces.Count; index++)
        {
            var piece = pieces[index];
            richItems[index] = new RichInlineItem(piece.Text, piece.Font, piece.BreakMode, piece.ExtraWidth);
            classNames[index] = piece.ClassName;
            hrefs[index] = piece.Href;
        }

        var lineHeight = variant switch
        {
            BlockVariant.Heading1 => Math.Max(24, Math.Round(FontSize * HeadingOneScale * HeadingLineHeightRatio)),
            BlockVariant.Heading2 => Math.Max(20, Math.Round(FontSize * HeadingTwoScale * HeadingLineHeightRatio)),
            _ => Math.Max(18, Math.Round(FontSize * BodyLineHeightRatio))
        };

        return new PreparedInlineBlock(
            CreateDecoration(context, marginTop),
            PretextLayout.PrepareRichInline(richItems),
            classNames,
            hrefs,
            lineHeight);
    }

    private List<InlinePiece> BuildInlinePieces(MarkdownInlineFlow flow, BlockVariant variant)
    {
        var pieces = new List<InlinePiece>(flow.Fragments.Count);
        foreach (var fragment in flow.Fragments)
        {
            if (string.IsNullOrEmpty(fragment.Text))
            {
                continue;
            }

            var piece = CreateInlinePiece(fragment, variant);
            if (pieces.Count > 0 && CanMergeInlinePieces(pieces[^1], piece))
            {
                pieces[^1] = piece with { Text = pieces[^1].Text + piece.Text };
            }
            else
            {
                pieces.Add(piece);
            }
        }

        return pieces;
    }

    private InlinePiece CreateInlinePiece(MarkdownInlineFragment fragment, BlockVariant variant)
    {
        var className = variant switch
        {
            BlockVariant.Heading1 => "frag frag--heading-1",
            BlockVariant.Heading2 => "frag frag--heading-2",
            _ => "frag frag--body"
        };

        var fontFamily = variant switch
        {
            BlockVariant.Heading1 or BlockVariant.Heading2 => "Georgia",
            _ => FontFamily?.Source ?? "Segoe UI"
        };

        var fontSize = variant switch
        {
            BlockVariant.Heading1 => Math.Max(16, Math.Round(FontSize * HeadingOneScale)),
            BlockVariant.Heading2 => Math.Max(15, Math.Round(FontSize * HeadingTwoScale)),
            _ => FontSize
        };

        if (fragment.Style.Code)
        {
            className = "frag frag--code";
            fontFamily = "Cascadia Mono";
        }
        else if (string.Equals(fragment.SemanticClass, "image", StringComparison.Ordinal))
        {
            className = "frag frag--chip frag--image";
        }
        else if (string.Equals(fragment.SemanticClass, "math", StringComparison.Ordinal))
        {
            className = "frag frag--chip frag--math";
            fontFamily = "Cambria Math";
        }
        else if (fragment.Style.Marked)
        {
            className += " frag--marked";
        }
        else if (fragment.Style.Inserted)
        {
            className += " frag--inserted";
        }

        if (!string.IsNullOrWhiteSpace(fragment.LinkTarget))
        {
            className += " is-link";
        }

        if (fragment.Style.Bold)
        {
            className += " is-strong";
        }

        if (fragment.Style.Italic)
        {
            className += " is-em";
        }

        if (fragment.Style.Strikethrough)
        {
            className += " is-del";
        }

        if (fragment.Style.Underline && string.IsNullOrWhiteSpace(fragment.LinkTarget))
        {
            className += " is-underline";
        }

        var breakMode = fragment.IsAtomic || fragment.Style.Code ||
                        string.Equals(fragment.SemanticClass, "image", StringComparison.Ordinal) ||
                        string.Equals(fragment.SemanticClass, "math", StringComparison.Ordinal)
            ? RichInlineBreakMode.Never
            : RichInlineBreakMode.Normal;

        var extraWidth = fragment.Style.Code || className.Contains("frag--chip", StringComparison.Ordinal)
            ? (fragment.Style.Code ? CodeExtraWidth : ChipExtraWidth)
            : 0;

        return new InlinePiece(
            fragment.Text,
            BuildFont(fontFamily, fontSize, fragment.Style.Bold, fragment.Style.Italic, !string.IsNullOrWhiteSpace(fragment.LinkTarget)),
            className,
            fragment.LinkTarget,
            breakMode,
            extraWidth);
    }

    private PreparedCodeBlock CreatePreparedCodeBlock(BlockDecoration decoration, string code, string? languageHint)
    {
        var normalizedCode = code ?? string.Empty;
        var fontSize = Math.Max(14, FontSize - 1);
        var lineHeight = Math.Max(18, Math.Round((FontSize - 1) * BodyLineHeightRatio));
        var highlight = _layoutService.HighlightCode(normalizedCode, languageHint);
        var lines = MarkdownCodeHighlighting.SplitRunsByLine(highlight.Runs);

        return new PreparedCodeBlock(
            decoration,
            lines,
            fontSize,
            lineHeight,
            languageHint);
    }

    private BlockDecoration CreateDecoration(RenderContext context, double marginTop)
    {
        var listIndent = Math.Max(0, context.ListDepth - 1) * ListIndent;
        var contentLeft = listIndent + context.QuoteDepth * BlockquoteIndent;
        var quoteRailLefts = new double[context.QuoteDepth];
        for (var index = 0; index < context.QuoteDepth; index++)
        {
            quoteRailLefts[index] = listIndent + index * BlockquoteIndent + RailOffset;
        }

        return new BlockDecoration(contentLeft, marginTop, null, null, quoteRailLefts);
    }

    private void DecorateListItem(List<PreparedBlock> blocks, int firstBlockIndex, string markerText)
    {
        var markerWidth = PretextLayout.MeasureNaturalWidth(PretextLayout.PrepareWithSegments(
            markerText,
            BuildFont(FontFamily?.Source ?? "Segoe UI", FontSize, bold: true, italic: false, mediumWeight: false)));
        var markerArea = markerWidth + ListMarkerGap;

        for (var index = firstBlockIndex; index < blocks.Count; index++)
        {
            blocks[index] = blocks[index] with
            {
                ContentLeft = blocks[index].ContentLeft + markerArea
            };
        }

        blocks[firstBlockIndex] = blocks[firstBlockIndex] with
        {
            MarkerLeft = blocks[firstBlockIndex].ContentLeft - markerArea,
            MarkerText = markerText
        };
    }

    private FrameworkElement? RenderBlock(PreparedBlock block, double availableWidth)
    {
        return block switch
        {
            PreparedInlineBlock inline => WrapDecoratedBlock(block, RenderInlineBlock(inline, availableWidth)),
            PreparedCodeBlock code => WrapDecoratedBlock(block, RenderCodeBlock(code, availableWidth)),
            PreparedRuleBlock => WrapDecoratedBlock(block, RenderRuleBlock(availableWidth - block.ContentLeft)),
            PreparedNodeBlock node => WrapDecoratedBlock(block, RenderNodeBlock(node.Node, availableWidth - block.ContentLeft)),
            _ => null
        };
    }

    private FrameworkElement RenderInlineBlock(PreparedInlineBlock block, double availableWidth)
    {
        var lineWidth = Math.Max(1, availableWidth - block.ContentLeft);
        var lines = new StackPanel
        {
            Spacing = 0
        };

        PretextLayout.WalkRichInlineLineRanges(block.Flow, lineWidth, range =>
        {
            var line = PretextLayout.MaterializeRichInlineLineRange(block.Flow, range);
            lines.Children.Add(BuildInlineRow(block, line));
        });

        return lines;
    }

    private FrameworkElement BuildInlineRow(PreparedInlineBlock block, RichInlineLine line)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0
        };

        foreach (var fragment in line.Fragments)
        {
            var className = block.ClassNames[fragment.ItemIndex];
            var href = block.Hrefs[fragment.ItemIndex];
            var element = BuildInlineFragment(className, href, fragment.Text);
            if (fragment.GapBefore > 0)
            {
                element.Margin = new Thickness(fragment.GapBefore, 0, 0, 0);
            }

            row.Children.Add(element);
        }

        return row;
    }

    private FrameworkElement BuildInlineFragment(string className, string? href, string text)
    {
        if (className.Contains("frag--code", StringComparison.Ordinal))
        {
            return new Border
            {
                Background = MarkdownTextBlock.ResolveSharedBrush("#F1F5F9"),
                BorderBrush = MarkdownTextBlock.ResolveSharedBrush("#CBD5E1"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Child = BuildTextElement(className, href, text, "Cascadia Mono")
            };
        }

        if (className.Contains("frag--chip", StringComparison.Ordinal))
        {
            return new Border
            {
                Background = MarkdownTextBlock.ResolveSharedBrush("#EFF6FF"),
                BorderBrush = MarkdownTextBlock.ResolveSharedBrush("#BFDBFE"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(7, 1, 7, 2),
                Child = BuildTextElement(className, href, text, className.Contains("frag--math", StringComparison.Ordinal) ? "Cambria Math" : FontFamily?.Source ?? "Segoe UI")
            };
        }

        if (className.Contains("frag--marked", StringComparison.Ordinal) || className.Contains("frag--inserted", StringComparison.Ordinal))
        {
            return new Border
            {
                Background = className.Contains("frag--marked", StringComparison.Ordinal)
                    ? MarkdownTextBlock.ResolveSharedBrush("#FFF1B8")
                    : MarkdownTextBlock.ResolveSharedBrush("#DCFCE7"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 0, 2, 0),
                Child = BuildTextElement(className, href, text)
            };
        }

        return BuildTextElement(className, href, text);
    }

    private TextBlock BuildTextElement(string className, string? href, string text, string? familyOverride = null)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = WinTextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
            Foreground = className.Contains("is-link", StringComparison.Ordinal)
                ? MarkdownTextBlock.ResolveSharedBrush("#2563EB")
                : Foreground as Brush ?? MarkdownTextBlock.ResolveSharedBrush("#0F172A"),
            FontFamily = new FontFamily(familyOverride ?? ResolveClassFontFamily(className)),
            FontWeight = className.Contains("is-strong", StringComparison.Ordinal) ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = className.Contains("is-em", StringComparison.Ordinal) ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            FontSize = ResolveClassFontSize(className)
        };

        var decorations = Windows.UI.Text.TextDecorations.None;
        if (className.Contains("is-link", StringComparison.Ordinal) || className.Contains("is-underline", StringComparison.Ordinal))
        {
            decorations |= Windows.UI.Text.TextDecorations.Underline;
        }

        if (className.Contains("is-del", StringComparison.Ordinal))
        {
            decorations |= Windows.UI.Text.TextDecorations.Strikethrough;
        }

        textBlock.TextDecorations = decorations;
        if (!string.IsNullOrWhiteSpace(href))
        {
            textBlock.Tag = href;
            textBlock.PointerReleased += OnLinkPointerReleased;
        }

        return textBlock;
    }

    private string ResolveClassFontFamily(string className)
    {
        return className.Contains("frag--heading-1", StringComparison.Ordinal) ||
               className.Contains("frag--heading-2", StringComparison.Ordinal)
            ? "Georgia"
            : FontFamily?.Source ?? "Segoe UI";
    }

    private double ResolveClassFontSize(string className)
    {
        if (className.Contains("frag--heading-1", StringComparison.Ordinal))
        {
            return Math.Max(16, Math.Round(FontSize * HeadingOneScale));
        }

        if (className.Contains("frag--heading-2", StringComparison.Ordinal))
        {
            return Math.Max(15, Math.Round(FontSize * HeadingTwoScale));
        }

        return FontSize;
    }

    private FrameworkElement RenderCodeBlock(PreparedCodeBlock block, double availableWidth)
    {
        var body = new StackPanel
        {
            Spacing = 0
        };

        foreach (var lineRuns in block.Lines)
        {
            body.Children.Add(CreateCodeLine(lineRuns, block.FontSize, block.LineHeight));
        }

        return new Border
        {
            Background = MarkdownTextBlock.ResolveSharedBrush("#F8FAFC"),
            BorderBrush = MarkdownTextBlock.ResolveSharedBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(CodePaddingX, CodePaddingY, CodePaddingX, CodePaddingY),
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
                                Text = string.IsNullOrWhiteSpace(block.LanguageHint) ? "code" : block.LanguageHint,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = MarkdownTextBlock.ResolveSharedBrush("#334155")
                            }
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

    private TextBlock CreateCodeLine(IReadOnlyList<MarkdownStyledTextRun> lineRuns, double fontSize, double lineHeight)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = WinTextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = fontSize,
            LineHeight = lineHeight,
            MinHeight = lineHeight,
            Foreground = Foreground as Brush ?? MarkdownTextBlock.ResolveSharedBrush("#0F172A")
        };

        foreach (var run in lineRuns)
        {
            textBlock.Inlines.Add(CreateCodeRun(run));
        }

        if (textBlock.Inlines.Count == 0)
        {
            textBlock.Text = string.Empty;
        }

        return textBlock;
    }

    private static Run CreateCodeRun(MarkdownStyledTextRun run)
    {
        return new Run
        {
            Text = run.Text,
            Foreground = string.IsNullOrWhiteSpace(run.Style.Foreground)
                ? MarkdownTextBlock.ResolveSharedBrush("#0F172A")
                : MarkdownTextBlock.ResolveSharedBrush(run.Style.Foreground),
            FontWeight = run.Style.Bold ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = run.Style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            TextDecorations = ResolveTextDecorations(run.Style)
        };
    }

    private static Windows.UI.Text.TextDecorations ResolveTextDecorations(MarkdownTextStyle style)
    {
        var decorations = Windows.UI.Text.TextDecorations.None;
        if (style.Underline)
        {
            decorations |= Windows.UI.Text.TextDecorations.Underline;
        }

        if (style.Strikethrough)
        {
            decorations |= Windows.UI.Text.TextDecorations.Strikethrough;
        }

        return decorations;
    }

    private FrameworkElement RenderRuleBlock(double width)
    {
        return new Border
        {
            Height = RuleHeight,
            Child = new Rectangle
            {
                Height = 1,
                Fill = MarkdownTextBlock.ResolveSharedBrush("#D0D7DE"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            }
        };
    }

    private FrameworkElement RenderNodeBlock(MarkdownBlockNode block, double availableWidth)
    {
        return block switch
        {
            MarkdownTableBlock tableBlock => RenderTableBlock(tableBlock),
            MarkdownAlertBlock alertBlock => (FrameworkElement)MarkdownAlertViewFactory.CreateBlockView(alertBlock, RenderNestedMarkdown, FontSize),
            MarkdownCustomContainerBlock customContainerBlock => (FrameworkElement)MarkdownCustomContainerViewFactory.CreateBlockView(customContainerBlock, RenderNestedMarkdown, FontSize),
            MarkdownDefinitionListBlock definitionListBlock => (FrameworkElement)MarkdownDefinitionListViewFactory.CreateBlockView(definitionListBlock, RenderNestedMarkdown, FontSize),
            MarkdownFigureBlock figureBlock => (FrameworkElement)MarkdownFigureViewFactory.CreateBlockView(figureBlock, RenderNestedMarkdown, FontSize),
            MarkdownFooterBlock footerBlock => (FrameworkElement)MarkdownFooterViewFactory.CreateBlockView(footerBlock, RenderNestedMarkdown, FontSize),
            MarkdownMathBlock mathBlock => (FrameworkElement)MarkdownMathViewFactory.CreateBlockView(mathBlock.Expression, FontSize, Foreground as Brush),
            MarkdownMermaidBlock mermaidBlock => (FrameworkElement)MarkdownMermaidViewFactory.CreateBlockView(mermaidBlock, availableWidth, FontSize, Foreground as Brush),
            MarkdownLinkReferenceBlock linkReferenceBlock => CreateMetadataCard($"[{linkReferenceBlock.Label}]", "Reference definition", new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    BuildTextElement("frag frag--body is-link", linkReferenceBlock.Url, linkReferenceBlock.Url, "Cascadia Mono"),
                    string.IsNullOrWhiteSpace(linkReferenceBlock.Title)
                        ? new TextBlock { Text = string.Empty, Visibility = Visibility.Collapsed }
                        : new TextBlock
                        {
                            Text = linkReferenceBlock.Title,
                            TextWrapping = WinTextWrapping.Wrap,
                            Foreground = MarkdownTextBlock.ResolveSharedBrush("#64748B"),
                            IsTextSelectionEnabled = true
                        }
                }
            }),
            MarkdownAbbreviationBlock abbreviationBlock => CreateMetadataCard(abbreviationBlock.Label, "Abbreviation definition", new TextBlock
            {
                Text = abbreviationBlock.Meaning,
                TextWrapping = WinTextWrapping.Wrap,
                Foreground = Foreground as Brush ?? MarkdownTextBlock.ResolveSharedBrush("#0F172A"),
                IsTextSelectionEnabled = true
            }),
            MarkdownFootnoteBlock footnoteBlock => CreateMetadataCard(
                $"[{footnoteBlock.Order}] {footnoteBlock.Label}",
                "Footnote",
                RenderNestedMarkdown(footnoteBlock.BodyMarkdown, 0)),
            MarkdownHtmlBlock htmlBlock => CreateFormulaBlock(htmlBlock.Html, "HTML", "#475569", "#F8FAFC"),
            MarkdownFallbackBlock fallbackBlock => CreateFormulaBlock(fallbackBlock.BodyMarkdown, fallbackBlock.Title, "#475569", "#F8FAFC"),
            _ => CreateFormulaBlock(block.ToString() ?? string.Empty, block.GetType().Name, "#475569", "#F8FAFC")
        };
    }

    private FrameworkElement RenderTableBlock(MarkdownTableBlock tableBlock)
    {
        var grid = new Grid
        {
            BorderBrush = MarkdownTextBlock.ResolveSharedBrush("#CBD5E1"),
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
                    Background = row.IsHeader ? MarkdownTextBlock.ResolveSharedBrush("#F8FAFC") : MarkdownTextBlock.ResolveSharedBrush("#FFFFFF"),
                    BorderBrush = MarkdownTextBlock.ResolveSharedBrush("#CBD5E1"),
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(8),
                    Child = RenderNestedMarkdown(row.Cells[columnIndex], 0)
                };

                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, columnIndex);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private FrameworkElement WrapDecoratedBlock(PreparedBlock block, FrameworkElement content)
    {
        var grid = new Grid();
        content.Margin = new Thickness(block.ContentLeft, 0, 0, 0);
        grid.Children.Add(content);

        foreach (var railLeft in block.QuoteRailLefts)
        {
            grid.Children.Add(new Border
            {
                Width = 3,
                Background = MarkdownTextBlock.ResolveSharedBrush("#CBD5E1"),
                CornerRadius = new CornerRadius(999),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(railLeft, 0, 0, 0)
            });
        }

        if (!string.IsNullOrWhiteSpace(block.MarkerText) && block.MarkerLeft is double markerLeft)
        {
            grid.Children.Add(new TextBlock
            {
                Text = block.MarkerText,
                Foreground = MarkdownTextBlock.ResolveSharedBrush("#64748B"),
                FontWeight = FontWeights.SemiBold,
                FontSize = Math.Max(11, FontSize - 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(markerLeft, 0, 0, 0)
            });
        }

        return grid;
    }

    private FrameworkElement RenderNestedMarkdown(string markdown, double horizontalPadding)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock { Text = string.Empty };
        }

        var nested = new MarkdownThreadMessageView
        {
            Markdown = markdown,
            BaseUri = BaseUri,
            FontSize = FontSize,
            Foreground = Foreground,
            TextWrapping = TextWrapping
        };

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

    private FrameworkElement CreateMetadataCard(string title, string subtitle, UIElement body)
    {
        return new Border
        {
            Background = MarkdownTextBlock.ResolveSharedBrush("#FFFFFF"),
            BorderBrush = MarkdownTextBlock.ResolveSharedBrush("#CBD5E1"),
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
                        Foreground = MarkdownTextBlock.ResolveSharedBrush("#0F172A")
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        Foreground = MarkdownTextBlock.ResolveSharedBrush("#64748B")
                    },
                    (FrameworkElement)body
                }
            }
        };
    }

    private FrameworkElement CreateFormulaBlock(string body, string title, string accent, string background)
    {
        return new Border
        {
            Background = MarkdownTextBlock.ResolveSharedBrush(background),
            BorderBrush = MarkdownTextBlock.ResolveSharedBrush("#CBD5E1"),
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
                        Foreground = MarkdownTextBlock.ResolveSharedBrush(accent)
                    },
                    string.IsNullOrWhiteSpace(body)
                        ? new TextBlock
                        {
                            Text = string.Empty
                        }
                        : new Border
                        {
                            Background = MarkdownTextBlock.ResolveSharedBrush("#FFFFFF"),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(10),
                            Child = new TextBlock
                            {
                                Text = body,
                                FontFamily = new FontFamily("Cascadia Mono"),
                                TextWrapping = WinTextWrapping.NoWrap,
                                Foreground = Foreground as Brush ?? MarkdownTextBlock.ResolveSharedBrush("#0F172A"),
                                IsTextSelectionEnabled = true
                            }
                        }
                }
            }
        };
    }

    private bool TryResolveAvailableWidth(out double availableWidth)
    {
        return TryResolveAvailableWidth(ActualWidth, out availableWidth);
    }

    private bool TryResolveAvailableWidth(double width, out double availableWidth)
    {
        if (double.IsNaN(width) || width <= 0)
        {
            width = Width;
        }

        if (double.IsNaN(width) || width <= 0)
        {
            availableWidth = 0;
            return false;
        }

        availableWidth = Math.Max(1, width);
        return true;
    }

    private async void OnLinkPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string linkTarget })
        {
            return;
        }

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

    private static bool CanMergeInlinePieces(InlinePiece left, InlinePiece right)
    {
        return left.Font == right.Font &&
               left.ClassName == right.ClassName &&
               left.Href == right.Href &&
               left.BreakMode == right.BreakMode &&
               left.ExtraWidth.Equals(right.ExtraWidth);
    }

    private static string BuildFont(string family, double size, bool bold, bool italic, bool mediumWeight)
    {
        var stylePrefix = italic ? "italic " : string.Empty;
        var weight = bold ? 700 : (mediumWeight ? 500 : 400);
        return $"{stylePrefix}{weight} {Math.Max(10, Math.Round(size))}px {family}";
    }

    private enum BlockVariant
    {
        Body,
        Heading1,
        Heading2
    }

    private readonly record struct RenderContext(int ListDepth, int QuoteDepth);

    private readonly record struct InlinePiece(
        string Text,
        string Font,
        string ClassName,
        string? Href,
        RichInlineBreakMode BreakMode,
        double ExtraWidth);

    private readonly record struct BlockDecoration(
        double ContentLeft,
        double MarginTop,
        double? MarkerLeft,
        string? MarkerText,
        double[] QuoteRailLefts);

    private abstract record PreparedBlock(BlockDecoration Decoration)
    {
        public double ContentLeft { get; init; } = Decoration.ContentLeft;
        public double MarginTop { get; init; } = Decoration.MarginTop;
        public double? MarkerLeft { get; init; } = Decoration.MarkerLeft;
        public string? MarkerText { get; init; } = Decoration.MarkerText;
        public double[] QuoteRailLefts { get; init; } = Decoration.QuoteRailLefts;
    }

    private sealed record PreparedInlineBlock(
        BlockDecoration Decoration,
        PreparedRichInline Flow,
        string[] ClassNames,
        string?[] Hrefs,
        double LineHeight)
        : PreparedBlock(Decoration);

    private sealed record PreparedCodeBlock(
        BlockDecoration Decoration,
        MarkdownStyledTextRun[][] Lines,
        double FontSize,
        double LineHeight,
        string? LanguageHint)
        : PreparedBlock(Decoration);

    private sealed record PreparedRuleBlock(
        BlockDecoration Decoration,
        double Height)
        : PreparedBlock(Decoration);

    private sealed record PreparedNodeBlock(
        BlockDecoration Decoration,
        MarkdownBlockNode Node)
        : PreparedBlock(Decoration);
}
