using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Media;

namespace CodexGui.Markdown.Controls;

internal sealed class MarkdownSelectionOverlayHost : IDisposable
{
    private static readonly IBrush HighlightBackground = new SolidColorBrush(Color.FromArgb(48, 59, 130, 246));
    private static readonly IBrush HighlightBorder = new SolidColorBrush(Color.FromArgb(160, 37, 99, 235));

    private readonly Border _highlightBorder;
    private readonly Popup _popup;

    public MarkdownSelectionOverlayHost()
    {
        _highlightBorder = new Border
        {
            Background = HighlightBackground,
            BorderBrush = HighlightBorder,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false
        };

        _popup = new Popup
        {
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            PlacementConstraintAdjustment =
                PopupPositionerConstraintAdjustment.SlideX |
                PopupPositionerConstraintAdjustment.SlideY,
            ShouldUseOverlayLayer = true,
            OverlayDismissEventPassThrough = true,
            IsLightDismissEnabled = false,
            Topmost = false,
            IsHitTestVisible = false,
            Child = _highlightBorder
        };
    }

    public bool IsOpen => _popup.IsOpen;

    public void Show(Control placementTarget, Rect placementRect)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);

        _highlightBorder.Width = Math.Max(placementRect.Width, 1);
        _highlightBorder.Height = Math.Max(placementRect.Height, 1);
        _popup.PlacementTarget = placementTarget;
        _popup.PlacementRect = placementRect;
        _popup.IsOpen = true;
    }

    public void Update(Control placementTarget, Rect placementRect)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);

        if (!_popup.IsOpen)
        {
            Show(placementTarget, placementRect);
            return;
        }

        _highlightBorder.Width = Math.Max(placementRect.Width, 1);
        _highlightBorder.Height = Math.Max(placementRect.Height, 1);
        _popup.PlacementTarget = placementTarget;
        _popup.PlacementRect = placementRect;
    }

    public void Hide()
    {
        _popup.IsOpen = false;
    }

    public void Dispose()
    {
        Hide();
    }
}
