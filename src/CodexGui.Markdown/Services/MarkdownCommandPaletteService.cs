using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace CodexGui.Markdown.Services;

internal enum MarkdownCommandPaletteCommitMode
{
    InsertAfter,
    InsertBefore,
    Replace
}

internal sealed record MarkdownCommandPaletteSelection(
    MarkdownBlockTemplate Template,
    MarkdownCommandPaletteCommitMode CommitMode,
    string Query);

internal sealed class MarkdownCommandPaletteRequest
{
    public required Control PlacementTarget { get; init; }

    public required Rect PlacementRect { get; init; }

    public required IReadOnlyList<MarkdownBlockTemplate> Templates { get; init; }

    public required Action<MarkdownCommandPaletteSelection> Commit { get; init; }

    public string Title { get; init; } = "Insert block";

    public string Subtitle { get; init; } = "Type to filter block templates.";

    public string? InitialQuery { get; init; }

    public Control? ReturnFocusTarget { get; init; }

    public Action? Dismissed { get; init; }
}

internal sealed class MarkdownCommandPaletteService : IDisposable
{
    private static readonly IBrush LightSurface = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush DarkSurface = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush LightChrome = new SolidColorBrush(Color.Parse("#CBD5E1"));
    private static readonly IBrush DarkChrome = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush LightSecondary = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush DarkSecondary = new SolidColorBrush(Color.Parse("#CBD5E1"));

    private readonly Popup _popup;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _subtitleBlock;
    private readonly TextBox _searchBox;
    private readonly ListBox _listBox;
    private readonly TextBlock _hintBlock;
    private readonly Border _root;
    private IReadOnlyList<MarkdownBlockTemplate> _templates = Array.Empty<MarkdownBlockTemplate>();
    private readonly List<PaletteItem> _items = [];
    private Action<MarkdownCommandPaletteSelection>? _commit;
    private Action? _dismissed;
    private Control? _returnFocusTarget;
    private bool _suppressDismiss;

    public MarkdownCommandPaletteService()
    {
        _titleBlock = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = 15
        };
        _subtitleBlock = new TextBlock
        {
            Foreground = SecondaryBrush
        };
        _searchBox = new TextBox
        {
            Watermark = "Search templates",
            UseFloatingWatermark = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _listBox = new ListBox
        {
            MinWidth = 320,
            MaxHeight = 320
        };
        _hintBlock = new TextBlock
        {
            Text = "Enter insert after • Shift+Enter insert before • Alt+Enter replace • Esc close",
            Foreground = SecondaryBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };

        _root = new Border
        {
            Background = SurfaceBrush,
            BorderBrush = ChromeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            _titleBlock,
                            _subtitleBlock
                        }
                    },
                    _searchBox,
                    _listBox,
                    _hintBlock
                }
            }
        };

        _popup = new Popup
        {
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.BottomLeft,
            PlacementGravity = PopupGravity.BottomLeft,
            PlacementConstraintAdjustment =
                PopupPositionerConstraintAdjustment.SlideX |
                PopupPositionerConstraintAdjustment.SlideY |
                PopupPositionerConstraintAdjustment.FlipY,
            ShouldUseOverlayLayer = true,
            OverlayDismissEventPassThrough = true,
            IsLightDismissEnabled = true,
            Topmost = true,
            Child = _root
        };

        _popup.Closed += OnPopupClosed;
        _searchBox.TextChanged += OnSearchTextChanged;
        _searchBox.AddHandler(InputElement.KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _listBox.AddHandler(InputElement.KeyDownEvent, OnPaletteKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _listBox.DoubleTapped += (_, _) => CommitSelection(MarkdownCommandPaletteCommitMode.InsertAfter);
    }

    public bool IsOpen => _popup.IsOpen;

    private static IBrush SurfaceBrush =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? DarkSurface : LightSurface;

    private static IBrush ChromeBrush =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? DarkChrome : LightChrome;

    private static IBrush SecondaryBrush =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? DarkSecondary : LightSecondary;

    public void Show(MarkdownCommandPaletteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _templates = request.Templates;
        _commit = request.Commit;
        _dismissed = request.Dismissed;
        _returnFocusTarget = request.ReturnFocusTarget;
        _titleBlock.Text = request.Title;
        _subtitleBlock.Text = request.Subtitle;
        _searchBox.Text = request.InitialQuery ?? string.Empty;

        _popup.PlacementTarget = request.PlacementTarget;
        _popup.PlacementRect = request.PlacementRect;
        _popup.IsOpen = true;

        RefreshItems();
        Dispatcher.UIThread.Post(() =>
        {
            _searchBox.Focus();
            _searchBox.CaretIndex = _searchBox.Text?.Length ?? 0;
        });
    }

    public void Hide(bool suppressDismiss = false)
    {
        if (!_popup.IsOpen)
        {
            _commit = null;
            _dismissed = null;
            return;
        }

        _suppressDismiss = suppressDismiss;
        _popup.IsOpen = false;
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshItems();
    }

    private void RefreshItems()
    {
        var query = _searchBox.Text?.Trim() ?? string.Empty;
        _items.Clear();

        foreach (var template in _templates)
        {
            var label = template.Label;
            var description = template.Description;
            var haystack = string.Concat(label, " ", description ?? string.Empty);
            if (query.Length > 0 &&
                haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            _items.Add(new PaletteItem(template));
        }

        _listBox.ItemsSource = null;
        _listBox.ItemsSource = _items;
        _listBox.SelectedIndex = _items.Count > 0 ? 0 : -1;
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var mode = e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                ? MarkdownCommandPaletteCommitMode.Replace
                : e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    ? MarkdownCommandPaletteCommitMode.InsertBefore
                    : MarkdownCommandPaletteCommitMode.InsertAfter;
            CommitSelection(mode);
            e.Handled = true;
        }
    }

    private void CommitSelection(MarkdownCommandPaletteCommitMode mode)
    {
        if (_listBox.SelectedItem is not PaletteItem { Template: { } template })
        {
            return;
        }

        var query = _searchBox.Text ?? string.Empty;
        var commit = _commit;
        Hide(suppressDismiss: true);
        commit?.Invoke(new MarkdownCommandPaletteSelection(template, mode, query));
        Dispatcher.UIThread.Post(() => _returnFocusTarget?.Focus());
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        var dismissed = _dismissed;
        _commit = null;
        _dismissed = null;

        if (_suppressDismiss)
        {
            _suppressDismiss = false;
            return;
        }

        Dispatcher.UIThread.Post(() => _returnFocusTarget?.Focus());
        dismissed?.Invoke();
    }

    public void Dispose()
    {
        _popup.Closed -= OnPopupClosed;
        _searchBox.TextChanged -= OnSearchTextChanged;
        Hide(suppressDismiss: true);
    }

    private sealed record PaletteItem(MarkdownBlockTemplate Template)
    {
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Template.Description)
                ? Template.Label
                : $"{Template.Label} — {Template.Description}";
        }
    }
}
