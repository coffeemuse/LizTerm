// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LizTerm.App.Keyboard;
using LizTerm.App.Mouse;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Controls;

/// <summary>Draws a ScreenSnapshot scaled to fit, in the IBM 3270 font. Rows are drawn as runs of equal style.</summary>
public sealed class TerminalScreen : Control
{
    public static readonly StyledProperty<ScreenSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenSnapshot?>(nameof(Snapshot));

    /// <summary>The table in force. The window composes it from the profile's Backspace choice and the user's
    /// keymap.json (#18) and sets it here, on open and on every change; a screen shown on its own (the tests) has
    /// the erasing default, which is what it always had.</summary>
    public static readonly StyledProperty<Keymap> KeymapProperty =
        AvaloniaProperty.Register<TerminalScreen, Keymap>(nameof(Keymap), DefaultKeymap.Create(destructiveBackspace: true));

    /// <summary>The mouse selection, or null. Two-way by default so the window can bind it to the view model,
    /// which clears it whenever input is sent to the host.</summary>
    public static readonly StyledProperty<ScreenRegion?> SelectionProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenRegion?>(nameof(Selection), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Which crosshair lines follow the cursor, per window. b3270's own CROSSHAIR toggle is
    /// deliberately unused: the engine has no display, so routing a display preference through a child process
    /// to have it handed back would only make the crosshair unavailable while disconnected (spec 4.1).</summary>
    public static readonly StyledProperty<CrosshairMode> CrosshairProperty =
        AvaloniaProperty.Register<TerminalScreen, CrosshairMode>(nameof(Crosshair));

    /// <summary>Every find match on the current screen, or null. Recomputed by FindViewModel against each new
    /// snapshot and pushed in; the control only paints them.</summary>
    public static readonly StyledProperty<IReadOnlyList<ScreenRegion>?> FindMatchesProperty =
        AvaloniaProperty.Register<TerminalScreen, IReadOnlyList<ScreenRegion>?>(nameof(FindMatches));

    /// <summary>The match the user is on, painted distinctly from the others.</summary>
    public static readonly StyledProperty<ScreenRegion?> CurrentMatchProperty =
        AvaloniaProperty.Register<TerminalScreen, ScreenRegion?>(nameof(CurrentMatch));

    /// <summary>Whether text the host marks as blinking actually blinks. Off draws it steady: the timer never
    /// runs, so the hidden phase never comes. A preference (SettingsViewModel.Blink), not a snapshot property —
    /// the snapshot says what the host asked for, this says whether the user wants to see it.</summary>
    public static readonly StyledProperty<bool> BlinkEnabledProperty =
        AvaloniaProperty.Register<TerminalScreen, bool>(nameof(BlinkEnabled), defaultValue: true);

    /// <summary>Whether this is a mono (3278) session, drawn in one phosphor with only intensity varying. The
    /// window binds it from the profile; the snapshot is not consulted, since b3270 sends no colour for a 3278
    /// and the fill a cell carries is not the host's (#123).</summary>
    public static readonly StyledProperty<bool> MonochromeProperty =
        AvaloniaProperty.Register<TerminalScreen, bool>(nameof(Monochrome));

    public static readonly FontFamily TerminalFont = FontFamily.Parse("avares://LizTerm.App/Assets/Fonts#IBM 3270");

    private readonly Typeface _typeface = new(TerminalFont);
    private readonly SelectionGesture _gesture = new();
    private readonly ModifierTapDetector _taps = new();
    private WindowBase? _window;
    private double _advancePerEm;
    private double _lineHeightPerEm;

    static TerminalScreen()
    {
        AffectsRender<TerminalScreen>(SnapshotProperty, SelectionProperty, CrosshairProperty,
            FindMatchesProperty, CurrentMatchProperty, BlinkEnabledProperty);
        AffectsArrange<TerminalScreen>(SnapshotProperty);
        FocusableProperty.OverrideDefaultValue<TerminalScreen>(true);
    }

    /// <summary>Half a blink cycle. 750 ms is two flashes every three seconds, well under WCAG 2.3.1's limit of
    /// three per second; never take it below 500 ms without revisiting that (spec section 8).</summary>
    public static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(750);

    /// <summary>How long the visual bell lights the screen. Brief enough to read as a flash, long enough to be
    /// seen; SessionViewModel.BellInterval keeps successive flashes at most two per second.</summary>
    public static readonly TimeSpan BellFlashDuration = TimeSpan.FromMilliseconds(120);

    private readonly DispatcherTimer _blinkTimer = new() { Interval = BlinkInterval };
    private readonly DispatcherTimer _flashTimer = new() { Interval = BellFlashDuration };
    private bool _attached;

    internal bool BlinkTimerRunning => _blinkTimer.IsEnabled;
    /// <summary>How many times the per-run draw list has been prepared. Test seam, like BlinkTimerRunning.</summary>
    internal int RunPlanBuilds { get; private set; }
    /// <summary>True during the phase in which blinking text is not drawn.</summary>
    internal bool BlinkHidden { get; private set; }
    /// <summary>True while the bell overlay is painted: exactly while the one-shot flash timer runs, so there is
    /// one state rather than two to keep in step. Test seam, like BlinkTimerRunning.</summary>
    internal bool BellFlashing => _flashTimer.IsEnabled;

    private readonly StatusBarFont _oiaFont = new();

    public TerminalScreen()
    {
        _blinkTimer.Tick += (_, _) =>
        {
            BlinkHidden = !BlinkHidden;
            InvalidateVisual();
        };
        _flashTimer.Tick += (_, _) => EndFlash();
        // The end of a layout pass is what tells a bounce from the bar's own resize apart from a real change
        // (StatusBarFont).
        LayoutUpdated += (_, _) => _oiaFont.LayoutSettled();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _window = TopLevel.GetTopLevel(this) as WindowBase;
        if (_window is not null) _window.Deactivated += OnWindowDeactivated;
        UpdateBlinkTimer(Snapshot);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        if (_window is not null) _window.Deactivated -= OnWindowDeactivated;
        _window = null;
        UpdateBlinkTimer(null);
        EndFlash();
    }

    private void UpdateBlinkTimer(ScreenSnapshot? snapshot)
    {
        var wanted = _attached && BlinkEnabled && snapshot is { HasBlink: true };
        if (wanted == _blinkTimer.IsEnabled) return;
        if (wanted)
        {
            _blinkTimer.Start();
        }
        else
        {
            _blinkTimer.Stop();
            BlinkHidden = false;
        }
    }

    /// <summary>The visual bell: lights the screen for BellFlashDuration, then clears it. A second call during a
    /// flash restarts the timer; the view model's throttle makes that unreachable from the bell, but the method must
    /// not misbehave if something else calls it.</summary>
    public void Flash()
    {
        _flashTimer.Stop();
        _flashTimer.Start();
        InvalidateVisual();
    }

    private void EndFlash()
    {
        if (!_flashTimer.IsEnabled) return;
        _flashTimer.Stop();
        InvalidateVisual();
    }

    public ScreenSnapshot? Snapshot
    {
        get => GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public Keymap Keymap
    {
        get => GetValue(KeymapProperty);
        set => SetValue(KeymapProperty, value);
    }

    public ScreenRegion? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public CrosshairMode Crosshair
    {
        get => GetValue(CrosshairProperty);
        set => SetValue(CrosshairProperty, value);
    }

    public bool BlinkEnabled
    {
        get => GetValue(BlinkEnabledProperty);
        set => SetValue(BlinkEnabledProperty, value);
    }

    public bool Monochrome
    {
        get => GetValue(MonochromeProperty);
        set => SetValue(MonochromeProperty, value);
    }

    public IReadOnlyList<ScreenRegion>? FindMatches
    {
        get => GetValue(FindMatchesProperty);
        set => SetValue(FindMatchesProperty, value);
    }

    public ScreenRegion? CurrentMatch
    {
        get => GetValue(CurrentMatchProperty);
        set => SetValue(CurrentMatchProperty, value);
    }

    internal CellGeometry LastGeometry { get; private set; }

    /// <summary>The status bar's font size: the cell font size, followed from ArrangeOverride through
    /// <see cref="StatusBarFont"/>, which holds only a bounce the bar's own resize causes. Read-only; the window's OIA
    /// text binds to it.</summary>
    public static readonly DirectProperty<TerminalScreen, double> OiaFontSizeProperty =
        AvaloniaProperty.RegisterDirect<TerminalScreen, double>(nameof(OiaFontSize), o => o.OiaFontSize);

    private double _oiaFontSize = StatusBarFont.Default;

    public double OiaFontSize
    {
        get => _oiaFontSize;
        private set => SetAndRaise(OiaFontSizeProperty, ref _oiaFontSize, value);
    }

    public event EventHandler<TerminalKey>? KeyRequested;
    public event EventHandler<string>? TextEntered;
    public event EventHandler<(int Row, int Column)>? CellClicked;

    /// <summary>Raised for the platform's Copy, Paste, and Select All hotkeys. The control never touches the clipboard.</summary>
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? SelectAllRequested;

    /// <summary>Raised for the platform's Find gesture. Checked here, ahead of Keymap.TryMap, for the same
    /// reason copy and paste are: the window owns what happens, the control owns only the keystroke.</summary>
    public event EventHandler? FindRequested;

    /// <summary>The session switcher's chord, Cmd+K on macOS and Ctrl+K elsewhere (#46). Like FindRequested it is
    /// a platform gesture, checked ahead of the keymap, so a future user keymap (#18) cannot take Ctrl+K.</summary>
    public event EventHandler? SwitcherRequested;

    /// <summary>Spec 6.2 ordering: the platform's copy, paste, and select-all hotkeys first (they are not in the
    /// table), then the key table, then the text table, then Avalonia's text input for everything else so dead
    /// keys and IMEs keep working.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        _taps.KeyDown(e.Key);
        if (TryHandlePlatformGesture(e))
        {
            e.Handled = true;
            return;
        }
        var chord = new KeyChord(e.Key, e.KeyModifiers);
        if (Keymap.TryMap(chord, out var key))
        {
            KeyRequested?.Invoke(this, key);
            e.Handled = true;
            return;
        }
        if (Keymap.TryText(chord, out var text))
        {
            TextEntered?.Invoke(this, text);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>A Ctrl key released alone is a tap chord (Right Ctrl is Enter, Left Ctrl is Reset by default).</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_taps.KeyUp(e.Key) is { } tapped)
        {
            var chord = KeyChord.TapOf(tapped);
            if (Keymap.TryMap(chord, out var key))
            {
                KeyRequested?.Invoke(this, key);
                e.Handled = true;
                return;
            }
            if (Keymap.TryText(chord, out var text))
            {
                TextEntered?.Invoke(this, text);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        _taps.Reset();
        base.OnLostFocus(e);
    }

    /// <summary>A wheel turn between a Ctrl press and its release makes the release the end of a chord, not a tap
    /// (a Ctrl+wheel zoom habit must not submit the screen), and so does the window losing activation while Ctrl is
    /// down, which moves no keyboard focus.</summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _taps.Reset();
        base.OnPointerWheelChanged(e);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => _taps.Reset();

    /// <summary>Ends a modifier tap in progress, for a pointer press this control does not see: a press anywhere
    /// else in the window, where Right Ctrl held across it must not become Enter on its release (keypad spec §6.2).
    /// The same rule OnPointerPressed applies to presses on the screen itself; SessionWindow calls this from one
    /// tunnelled PointerPressed handler, so no surface has to remember the rule for itself.</summary>
    public void CancelTap() => _taps.Reset();

    private bool TryHandlePlatformGesture(KeyEventArgs e)
    {
        // The same classification the keymap policy refuses chords by: the platform's gestures when available
        // (Cmd on macOS, Ctrl elsewhere), Ctrl+key as the fallback, and Find and Switch Session built from
        // CommandModifiers because PlatformHotkeyConfiguration carries neither.
        var hotkeys = PlatformHotkeys.From(this.GetPlatformSettings()?.HotkeyConfiguration);
        var (raise, handled) = hotkeys.Classify(e.Key, e.KeyModifiers) switch
        {
            ReservedGesture.Copy => (CopyRequested, true),
            ReservedGesture.Paste => (PasteRequested, true),
            ReservedGesture.SelectAll => (SelectAllRequested, true),
            ReservedGesture.Find => (FindRequested, true),
            ReservedGesture.SwitchSession => (SwitcherRequested, true),
            _ => ((EventHandler?)null, false),
        };
        raise?.Invoke(this, EventArgs.Empty);
        return handled;
    }

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
        // A Ctrl+click is a click, not a Ctrl tap.
        _taps.Reset();
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
        SetCurrentValue(SelectionProperty, _gesture.Region);
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
        SetCurrentValue(SelectionProperty, _gesture.Region);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        // Read before uncapturing: releasing capture on a control that owns it raises PointerCaptureLost
        // synchronously, and that handler ends the gesture too, so uncapturing first would make every plain
        // click look like the no-op release of an already-ended gesture.
        var result = _gesture.Release();
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        if (result != ReleaseResult.Click) return;
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var position = e.GetPosition(this);
        if (LastGeometry.NearestCell(position.X, position.Y, snapshot.Rows, snapshot.Columns) is { } cell)
        {
            CellClicked?.Invoke(this, cell);
            e.Handled = true;
        }
    }

    /// <summary>A capture lost mid-drag (a modal opened, the window deactivated) ends the gesture where it was:
    /// the next move must not extend it and the next release must not click.</summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _gesture.Release();
    }

    /// <summary>A screen of a different size makes the old coordinates meaningless; same size keeps them. A
    /// BlinkEnabled change re-evaluates the blink timer against the current snapshot.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BlinkEnabledProperty)
        {
            // AffectsRender repaints; this decides whether the timer runs against the screen already showing.
            UpdateBlinkTimer(Snapshot);
            return;
        }
        if (change.Property == MonochromeProperty)
        {
            // The run plan bakes brushes in and is keyed only by snapshot and geometry, so it has to go.
            _runPlan = null;
            InvalidateVisual();
            return;
        }
        if (change.Property != SnapshotProperty) return;
        var (oldValue, newValue) = change.GetOldAndNewValue<ScreenSnapshot?>();
        UpdateBlinkTimer(newValue);
        if (Selection is null) return;
        if (oldValue is null || newValue is null || oldValue.Rows != newValue.Rows || oldValue.Columns != newValue.Columns)
            SetCurrentValue(SelectionProperty, null);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateGeometry(finalSize);
        // Here and never from Render: the bar's size is layout, and StatusBarFont tells a bounce from its own resize
        // by its arriving within the same layout pass.
        OiaFontSize = _oiaFont.Follow(LastGeometry.FontSize);
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

        foreach (var run in EnsureRunPlan(snapshot, g))
        {
            // The background is painted through a hidden blink phase; only the glyphs and the underline go away.
            if (run.Background is { } background) context.FillRectangle(background, run.Rect);
            if (BlinkHidden && run.Blink) continue;
            if (run.Text is { } text) context.DrawText(text, run.Rect.TopLeft);
            if (run.Underline is { } pen)
            {
                var y = Math.Round(run.Rect.Bottom) - 1.5;
                context.DrawLine(pen, new Point(run.Rect.Left, y), new Point(run.Rect.Right, y));
            }
        }
        DrawSelection(context, snapshot, g);
        DrawFindMatches(context, snapshot, g);
        DrawCursor(context, snapshot, g);
        // Last of the overlays, over the cursor block and not under it: the ruler's lines sit on the cursor
        // cell's own bottom and left edges, and the opaque block DrawCursor paints over that whole cell used to
        // erase most of them — the crossing point, which is the one place the crosshair has to be readable.
        DrawCrosshair(context, snapshot, g);
        if (BellFlashing) context.FillRectangle(Palette.BellFlash, bounds);
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

    /// <summary>One run of identically styled cells, with its text already shaped. Held for as long as the
    /// snapshot and the cell geometry are unchanged, so a blink phase flip — a full repaint twice a second, for
    /// as long as anything on screen blinks — redraws these instead of re-segmenting and re-shaping every cell.
    /// </summary>
    private readonly record struct RunVisual(Rect Rect, IBrush? Background, FormattedText? Text, IPen? Underline, bool Blink);

    private List<RunVisual>? _runPlan;
    private ScreenSnapshot? _runPlanSnapshot;
    private CellGeometry _runPlanGeometry;

    private List<RunVisual> EnsureRunPlan(ScreenSnapshot snapshot, CellGeometry g)
    {
        if (_runPlan is not null && ReferenceEquals(_runPlanSnapshot, snapshot) && _runPlanGeometry == g) return _runPlan;

        var plan = new List<RunVisual>();
        var monochrome = Monochrome;
        for (var row = 0; row < snapshot.Rows; row++)
        {
            var cells = snapshot.Row(row);
            var col = 0;
            while (col < cells.Length)
            {
                var start = col;
                var style = cells[col];
                while (col < cells.Length && cells[col].SameStyleAs(style)) col++;
                plan.Add(BuildRun(snapshot, row, start, col - start, style, g, monochrome));
            }
        }

        _runPlan = plan;
        _runPlanSnapshot = snapshot;
        _runPlanGeometry = g;
        RunPlanBuilds++;
        return plan;
    }

    private RunVisual BuildRun(ScreenSnapshot snapshot, int row, int start, int length, Cell style, CellGeometry g, bool monochrome)
    {
        var rect = new Rect(g.OriginX + start * g.CellWidth, g.OriginY + row * g.CellHeight, length * g.CellWidth, g.CellHeight);
        var background = CellColors.Background(style, monochrome);

        var text = snapshot.GetText(row, start, length);
        FormattedText? formatted = null;
        if (!string.IsNullOrWhiteSpace(text))
            formatted = new FormattedText(CellGlyphs.ForDrawing(text), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, g.FontSize, CellColors.Foreground(style, monochrome));

        var underline = style.Rendition.HasFlag(CellRendition.Underline) ? new Pen(CellColors.Underline(style, monochrome)) : null;
        return new RunVisual(rect, background, formatted, underline, style.Rendition.HasFlag(CellRendition.Blink));
    }

    private void DrawCrosshair(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        var (horizontal, vertical) = CrosshairGeometry.Rects(
            Crosshair, snapshot.Cursor, g, snapshot.Rows, snapshot.Columns, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        var brush = Palette.CrosshairBrush(Monochrome);
        if (horizontal is { } h) context.FillRectangle(brush, h);
        if (vertical is { } v) context.FillRectangle(brush, v);
    }

    private void DrawSelection(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        if (Selection?.Clamp(snapshot.Rows, snapshot.Columns) is not { } region) return;
        var topLeft = g.CellRect(region.Top, region.Left);
        var bottomRight = g.CellRect(region.Bottom, region.Right);
        context.FillRectangle(Palette.Selection, new Rect(topLeft.TopLeft, bottomRight.BottomRight));
    }

    /// <summary>An overlay, exactly as the selection is — never folded into the run plan. Matches arrive
    /// already recomputed for this snapshot, so a stale region here means only that the host has just resized
    /// the screen; FindMatchGeometry answers that rather than throwing (see its own comment for why a match
    /// list can outlive the screen it was computed against).</summary>
    private void DrawFindMatches(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        if (FindMatches is not { Count: > 0 } matches) return;
        var current = CurrentMatch;

        foreach (var match in matches)
        {
            if (FindMatchGeometry.Rect(match, g, snapshot.Rows, snapshot.Columns) is not { } rect) continue;
            var brush = Palette.FindMatchBrush(match == current);
            context.FillRectangle(brush, rect);
        }
    }

    private void DrawCursor(DrawingContext context, ScreenSnapshot snapshot, CellGeometry g)
    {
        var cursor = snapshot.Cursor;
        if (!cursor.Visible || cursor.Row >= snapshot.Rows || cursor.Column >= snapshot.Columns) return;
        var cell = snapshot[cursor.Row, cursor.Column];
        var rect = g.CellRect(cursor.Row, cursor.Column);
        var monochrome = Monochrome;
        context.FillRectangle(CellColors.CursorBlock(cell, monochrome), rect);
        var ch = cell.Character.ToString();
        if (!string.IsNullOrWhiteSpace(ch))
        {
            var formatted = new FormattedText(CellGlyphs.ForDrawing(ch), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, g.FontSize, CellColors.CursorGlyph(cell, monochrome));
            context.DrawText(formatted, rect.TopLeft);
        }
    }
}
