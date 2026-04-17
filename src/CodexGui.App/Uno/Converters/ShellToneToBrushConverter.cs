using CodexGui.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace CodexGui.App.Uno.Converters;

public sealed class ShellToneToBrushConverter : IValueConverter
{
    private static readonly Dictionary<ShellTone, Brush> Brushes = new()
    {
        [ShellTone.TextPrimary] = Brush("#171717"),
        [ShellTone.TextSecondary] = Brush("#454545"),
        [ShellTone.TextMuted] = Brush("#707070"),
        [ShellTone.Green] = Brush("#1F7A3D"),
        [ShellTone.GreenSoft] = Brush("#EFFAF2"),
        [ShellTone.Blue] = Brush("#0A56C2"),
        [ShellTone.BlueSoft] = Brush("#EDF3FF"),
        [ShellTone.Amber] = Brush("#B06A10"),
        [ShellTone.AmberSoft] = Brush("#FFF6EA"),
        [ShellTone.Red] = Brush("#B42318"),
        [ShellTone.RedSoft] = Brush("#FFF0EE"),
        [ShellTone.Neutral] = Brush("#636363"),
        [ShellTone.NeutralSoft] = Brush("#F2F2F2"),
        [ShellTone.Paper] = Brush("#FFFFFF"),
        [ShellTone.PaperMuted] = Brush("#F8F8F8"),
        [ShellTone.EditorSurface] = Brush("#111827"),
        [ShellTone.EditorHeaderSurface] = Brush("#1F2937"),
        [ShellTone.LineNumber] = Brush("#707070"),
        [ShellTone.DiffAddSurface] = Brush("#EFFAF2"),
        [ShellTone.DiffRemoveSurface] = Brush("#FFF0EE"),
        [ShellTone.DiffMetaSurface] = Brush("#F8F8F8"),
        [ShellTone.CommandSurface] = Brush("#F3F6FA"),
        [ShellTone.ToolSurface] = Brush("#F8F8F8")
    };

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is ShellTone tone && Brushes.TryGetValue(tone, out var brush)
            ? brush
            : Brush("#0F172A");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static Brush Brush(string hex)
    {
        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length == 6)
        {
            normalized = $"FF{normalized}";
        }

        return new SolidColorBrush(ColorHelper.FromArgb(
            System.Convert.ToByte(normalized[..2], 16),
            System.Convert.ToByte(normalized.Substring(2, 2), 16),
            System.Convert.ToByte(normalized.Substring(4, 2), 16),
            System.Convert.ToByte(normalized.Substring(6, 2), 16)));
    }
}
