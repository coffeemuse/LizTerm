using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Controls;

/// <summary>Draws a ScreenSnapshot scaled to fit, in the IBM 3270 font. Rows are drawn as runs of equal style.</summary>
public sealed class TerminalScreen : Control
{
    public static readonly StyledProperty<ScreenSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenSnapshot?>(nameof(Snapshot));

    public static readonly FontFamily TerminalFont = FontFamily.Parse("avares://LizTerm.App/Assets/Fonts#IBM 3270");

    private readonly Typeface _typeface = new(TerminalFont);
    private double _advancePerEm;
    private double _lineHeightPerEm;

    static TerminalScreen()
    {
        AffectsRender<TerminalScreen>(SnapshotProperty);
        AffectsArrange<TerminalScreen>(SnapshotProperty);
        FocusableProperty.OverrideDefaultValue<TerminalScreen>(true);
    }

    public ScreenSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    internal CellGeometry LastGeometry { get; private set; }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateGeometry(finalSize);
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Palette.Background, bounds);
        var snapshot = Snapshot;
        if (snapshot is null) return;

        UpdateGeometry(bounds.Size);
        var g = LastGeometry;
        if (g.FontSize <= 0) return;

        for (var row = 0; row < snapshot.Rows; row++)
        {
            var cells = snapshot.Row(row);
            var col = 0;
            while (col < cells.Length)
            {
                var start = col;
                var style = cells[col];
                while (col < cells.Length && SameStyle(cells[col], style)) col++;
                DrawRun(context, snapshot, row, start, col - start, style, g);
            }
        }
        DrawCursor(context, snapshot, g);
    }

    private void UpdateGeometry(Size size)
    {
        var snapshot = Snapshot;
        if (snapshot is null) { LastGeometry = default; return; }
        EnsureMetrics();
        LastGeometry = CellGeometry.Fit(size.Width, size.Height, snapshot.Rows, snapshot.Columns, _advancePerEm, _lineHeightPerEm);
    }

    private void EnsureMetrics()
    {
        if (_advancePerEm > 0) return;
        const double probeSize = 100;
        var probe = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, probeSize, Brushes.White);
        _advancePerEm = probe.Width > 0 ? probe.Width / probeSize : 0.6;
        _lineHeightPerEm = probe.Height > 0 ? probe.Height / probeSize : 1.2;
    }

    private static bool SameStyle(in Cell a, in Cell b) =>
        a.Foreground == b.Foreground && a.Background == b.Background && a.Rendition == b.Rendition;

    private void DrawRun(DrawingContext context, ScreenSnapshot snapshot, int row, int start, int length, Cell style, CellGeometry g)
    {
        var rect = new Rect(g.OriginX + start * g.CellWidth, g.OriginY + row * g.CellHeight, length * g.CellWidth, g.CellHeight);
        var reverse = style.Rendition.HasFlag(CellRendition.Reverse);
        var fg = ResolveForeground(reverse ? style.Background : style.Foreground);
        var bg = ResolveBackground(reverse ? style.Foreground : style.Background);

        if (bg != HostColor.NeutralBlack)
            context.FillRectangle(Palette.Brush(bg, false), rect);

        var text = snapshot.GetText(row, start, length);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var bright = style.Rendition.HasFlag(CellRendition.Highlight);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, g.FontSize, Palette.Brush(fg, bright));
            context.DrawText(formatted, rect.TopLeft);
        }

        if (style.Rendition.HasFlag(CellRendition.Underline))
        {
            var y = Math.Round(rect.Bottom) - 1.5;
            context.DrawLine(new Pen(Palette.Brush(fg, false)), new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }

    private void DrawCursor(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        var cursor = snapshot.Cursor;
        if (!cursor.Visible || cursor.Row >= snapshot.Rows || cursor.Column >= snapshot.Columns) return;
        var cell = snapshot[cursor.Row, cursor.Column];
        var rect = g.CellRect(cursor.Row, cursor.Column);
        var fg = ResolveForeground(cell.Foreground);
        context.FillRectangle(Palette.Brush(fg, false), rect);
        var ch = cell.Character.ToString();
        if (!string.IsNullOrWhiteSpace(ch))
        {
            var formatted = new FormattedText(ch, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, g.FontSize, Palette.Brush(HostColor.NeutralBlack, false));
            context.DrawText(formatted, rect.TopLeft);
        }
    }

    private static HostColor ResolveForeground(HostColor color) => color == HostColor.Default ? HostColor.NeutralWhite : color;
    private static HostColor ResolveBackground(HostColor color) => color == HostColor.Default ? HostColor.NeutralBlack : color;
}
