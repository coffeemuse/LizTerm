// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Controls;

/// <summary>The Keyboard tab's Add slot (editable keymap spec §5.3). Idle it is an ordinary button, so Tab passes
/// through it (an armed-on-focus slot would swallow Tab and trap a keyboard user); a click, or Enter or Space,
/// arms it. Armed, it captures the next chord: a key with its modifiers, or a Ctrl key pressed and released alone
/// as a tap (its own <see cref="ModifierTapDetector"/>, the screen's rule), and offers it to
/// <see cref="CaptureHandler"/>. Accepted disarms; refused stays armed with the reason as its text. Focus loss or a
/// second click disarms. Escape and Tab are chords like the rest, since both are bindable.
/// The control knows nothing about keymaps: the handler is the row's.
/// The release of a key the slot captured is swallowed too (<c>_consumed</c>): <see cref="Button"/> activates on the
/// Space *release*, whatever happened to the press, so binding Space disarmed the slot and its own release armed it
/// straight back again.</summary>
public sealed class ChordCaptureBox : Button
{
    public const string IdleText = "Add";
    public const string ArmedText = "Press a key";

    public static readonly StyledProperty<Func<KeyChord, CaptureResult>?> CaptureHandlerProperty =
        AvaloniaProperty.Register<ChordCaptureBox, Func<KeyChord, CaptureResult>?>(nameof(CaptureHandler));

    private readonly ModifierTapDetector _taps = new();
    private readonly TextBlock _label = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private DispatcherTimer? _expiry;
    private string? _message;
    private Key? _consumed;

    public ChordCaptureBox()
    {
        Classes.Add("chord-slot");
        Content = _label;
        Refresh();
    }

    /// <summary>A Button subclass is a different style key and would render with no template at all. It also means a
    /// style selector cannot name this type, so the tab styles it by the class and pseudo-class set here.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    public Func<KeyChord, CaptureResult>? CaptureHandler
    {
        get => GetValue(CaptureHandlerProperty);
        set => SetValue(CaptureHandlerProperty, value);
    }

    public bool IsArmed { get; private set; }

    /// <summary>How long an accepted note ("Moved from PA2") shows before the slot reads Add again.</summary>
    public TimeSpan MessageDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>What the slot reads now; the words carry the state, colour and border only echo it.</summary>
    public string Text => _label.Text ?? "";

    protected override void OnClick()
    {
        if (IsArmed) Disarm();
        else Arm();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsArmed)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        _taps.KeyDown(e.Key);
        if (e.Key == Key.None || ChordSyntax.IsModifierKey(e.Key)) return;
        _consumed = e.Key;
        Offer(new KeyChord(e.Key, e.KeyModifiers));
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        // The release of a captured key belongs to that capture, armed or not: it is never a tap (the press cleared
        // the tap candidate) and it must not reach Button, which activates on the Space release.
        if (_consumed == e.Key)
        {
            _consumed = null;
            e.Handled = true;
            return;
        }
        if (!IsArmed)
        {
            base.OnKeyUp(e);
            return;
        }
        e.Handled = true;
        if (_taps.KeyUp(e.Key) is { } tapped) Offer(KeyChord.TapOf(tapped));
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        if (IsArmed || _message is not null) Disarm();
        base.OnLostFocus(e);
    }

    private void Arm()
    {
        IsArmed = true;
        _taps.Reset();
        _consumed = null;
        Show(null, expires: false);
        Focus();
    }

    private void Disarm()
    {
        IsArmed = false;
        _taps.Reset();
        Show(null, expires: false);
    }

    private void Offer(KeyChord chord)
    {
        var result = CaptureHandler?.Invoke(chord) ?? new CaptureResult(false, null);
        if (result.Accepted)
        {
            IsArmed = false;
            _taps.Reset();
            Show(result.Message, expires: true);
        }
        else
        {
            Show(result.Message, expires: false);
        }
    }

    private void Show(string? message, bool expires)
    {
        _message = message;
        _expiry?.Stop();
        _expiry = null;
        if (expires && message is not null)
        {
            _expiry = new DispatcherTimer(MessageDuration, DispatcherPriority.Normal, (_, _) =>
            {
                _expiry?.Stop();
                _expiry = null;
                _message = null;
                Refresh();
            });
            _expiry.Start();
        }
        Refresh();
    }

    private void Refresh()
    {
        _label.Text = _message ?? (IsArmed ? ArmedText : IdleText);
        PseudoClasses.Set(":armed", IsArmed);
    }
}
