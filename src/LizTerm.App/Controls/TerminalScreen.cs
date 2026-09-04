using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using LizTerm.App.Keyboard;
using LizTerm.App.Mouse;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Controls;

/// <summary>Draws a ScreenSnapshot scaled to fit, in the IBM 3270 font. Rows are drawn as runs of equal style.</summary>
public sealed class TerminalScreen : Control
{
    public static readonly StyledProperty<ScreenSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenSnapshot?>(nameof(Snapshot));

    /// <summary>Mirrors the profile's DestructiveBackspace choice; see <see cref="DefaultKeymap.TryMap"/>.</summary>
    public static readonly StyledProperty<bool> DestructiveBackspaceProperty =
        AvaloniaProperty.Register<TerminalScreen, bool>(nameof(DestructiveBackspace));

    /// <summary>The mouse selection, or null. Two-way by default so the window can bind it to the view model,
    /// which clears it whenever input is sent to the host.</summary>
    public static readonly StyledProperty<ScreenRegion?> SelectionProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenRegion?>(nameof(Selection), defaultBindingMode: BindingMode.TwoWay);

    public static readonly FontFamily TerminalFont = FontFamily.Parse("avares://LizTerm.App/Assets/Fonts#IBM 3270");

    private readonly Typeface _typeface = new(TerminalFont);
    private readonly SelectionGesture _gesture = new();
    private double _advancePerEm;
    private double _lineHeightPerEm;

    static TerminalScreen()
    {
        AffectsRender<TerminalScreen>(SnapshotProperty, SelectionProperty);
        AffectsArrange<TerminalScreen>(SnapshotProperty);
        FocusableProperty.OverrideDefaultValue<TerminalScreen>(true);
    }

    public ScreenSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public bool DestructiveBackspace
    {
        get => GetValue(DestructiveBackspaceProperty);
        set => SetValue(DestructiveBackspaceProperty, value);
    }

    public ScreenRegion? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    internal CellGeometry LastGeometry { get; private set; }

    public event EventHandler<TerminalKey>? KeyRequested;
    public event EventHandler<string>? TextEntered;
    public event EventHandler<(int Row, int Column)>? CellClicked;

    /// <summary>Raised for the platform's Copy, Paste, and Select All hotkeys. The control never touches the clipboard.</summary>
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? SelectAllRequested;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (TryHandleClipboardKey(e))
        {
            e.Handled = true;
            return;
        }
        if (DefaultKeymap.TryMap(e.Key, e.KeyModifiers, DestructiveBackspace, out var key))
        {
            KeyRequested?.Invoke(this, key);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private bool TryHandleClipboardKey(KeyEventArgs e)
    {
        var app = Application.Current;
        var hotkeys = app?.PlatformSettings?.HotkeyConfiguration;
        if (Matches(hotkeys?.Copy, e, Key.C)) { CopyRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (Matches(hotkeys?.Paste, e, Key.V)) { PasteRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (Matches(hotkeys?.SelectAll, e, Key.A)) { SelectAllRequested?.Invoke(this, EventArgs.Empty); return true; }
        return false;
    }

    /// <summary>The platform's gestures when available (Cmd on macOS, Ctrl elsewhere); Ctrl+key as the fallback.</summary>
    private static bool Matches(List<KeyGesture>? gestures, KeyEventArgs e, Key fallbackKey) =>
        gestures is { Count: > 0 }
            ? gestures.Any(gesture => gesture.Matches(e))
            : e.Key == fallbackKey && e.KeyModifiers == KeyModifiers.Control;

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text))
        {
            TextEntered?.Invoke(this, e.Text);
            e.Handled = true;
            return;
        }
        base.OnTextInput(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.HitTest(position.X, position.Y, snapshot.Rows, snapshot.Columns) is not { } cell) return;
        PressAt(cell, e.ClickCount);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>The cell-level half of a press, split out so tests can drive a double-click directly if the
    /// headless platform does not report click counts.</summary>
    internal void PressAt((int Row, int Column) cell, int clickCount)
    {
        var snapshot = Snapshot;
        if (snapshot is null) return;
        if (clickCount == 2)
            _gesture.DoubleClick(cell.Row, cell.Column, snapshot);
        else
            _gesture.Press(cell.Row, cell.Column);
        Selection = _gesture.Region;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer.Captured, this)) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.NearestCell(position.X, position.Y, snapshot.Rows, snapshot.Columns) is not { } cell) return;
        _gesture.Move(cell.Row, cell.Column);
        Selection = _gesture.Region;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        if (_gesture.Release() != ReleaseResult.Click) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.NearestCell(position.X, position.Y, snapshot.Rows, snapshot.Columns) is { } cell)
        {
            CellClicked?.Invoke(this, cell);
            e.Handled = true;
        }
    }

    /// <summary>A screen of a different size makes the old coordinates meaningless; same size keeps them.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SnapshotProperty || Selection is null) return;
        var (oldValue, newValue) = change.GetOldAndNewValue<ScreenSnapshot?>();
        if (oldValue is null || newValue is null || oldValue.Rows != newValue.Rows || oldValue.Columns != newValue.Columns)
            Selection = null;
    }

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
        DrawSelection(context, snapshot, g);
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

    private void DrawSelection(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        if (Selection?.Clamp(snapshot.Rows, snapshot.Columns) is not { } region) return;
        var topLeft = g.CellRect(region.Top, region.Left);
        var bottomRight = g.CellRect(region.Bottom, region.Right);
        context.FillRectangle(Palette.Selection, new Rect(topLeft.TopLeft, bottomRight.BottomRight));
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
