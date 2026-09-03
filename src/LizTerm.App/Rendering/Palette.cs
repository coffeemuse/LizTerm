using Avalonia.Media;
using Avalonia.Media.Immutable;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Fixed v1 color scheme following IBM host color names. Theming is deferred.</summary>
public static class Palette
{
    public static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromRgb(0, 0, 0));

    private static readonly Dictionary<HostColor, Color> Colors = new()
    {
        [HostColor.Default] = Color.FromRgb(0xF0, 0xF0, 0xF0),
        [HostColor.NeutralBlack] = Color.FromRgb(0x00, 0x00, 0x00),
        [HostColor.Blue] = Color.FromRgb(0x5C, 0x8A, 0xFF),
        [HostColor.Red] = Color.FromRgb(0xFF, 0x50, 0x50),
        [HostColor.Pink] = Color.FromRgb(0xFF, 0x70, 0xFF),
        [HostColor.Green] = Color.FromRgb(0x50, 0xFF, 0x50),
        [HostColor.Turquoise] = Color.FromRgb(0x50, 0xFF, 0xFF),
        [HostColor.Yellow] = Color.FromRgb(0xFF, 0xFF, 0x50),
        [HostColor.NeutralWhite] = Color.FromRgb(0xF0, 0xF0, 0xF0),
        [HostColor.Black] = Color.FromRgb(0x00, 0x00, 0x00),
        [HostColor.DeepBlue] = Color.FromRgb(0x20, 0x20, 0xB0),
        [HostColor.Orange] = Color.FromRgb(0xFF, 0xA0, 0x40),
        [HostColor.Purple] = Color.FromRgb(0xB0, 0x60, 0xFF),
        [HostColor.PaleGreen] = Color.FromRgb(0xA0, 0xFF, 0xA0),
        [HostColor.PaleTurquoise] = Color.FromRgb(0xA0, 0xFF, 0xFF),
        [HostColor.Grey] = Color.FromRgb(0xA0, 0xA0, 0xA0),
        [HostColor.White] = Color.FromRgb(0xFF, 0xFF, 0xFF),
    };

    private static readonly Dictionary<(HostColor, bool), IBrush> Cache = new();

    public static Color ColorOf(HostColor color) => Colors[color];

    /// <param name="bright">Intensified (highlight) rendition: blend 35% toward white.</param>
    public static IBrush Brush(HostColor color, bool bright)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((color, bright), out var brush)) return brush;
            var c = ColorOf(color);
            if (bright)
                c = Color.FromRgb(Blend(c.R), Blend(c.G), Blend(c.B));
            brush = new ImmutableSolidColorBrush(c);
            Cache[(color, bright)] = brush;
            return brush;
        }
    }

    private static byte Blend(byte channel) => (byte)(channel + (255 - channel) * 0.35);
}
