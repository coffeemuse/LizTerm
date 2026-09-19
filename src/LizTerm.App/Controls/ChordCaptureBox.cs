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
/// <see cref="CaptureHandler"/>. Accepted disarms; refused stays armed with the reason as its text. Focus loss, a
/// second click or the row's Cancel button (<see cref="Cancel"/>) disarms. Escape and Tab are chords like the rest, since both are bindable.
/// The control knows nothing about keymaps: the handler is the row's.
/// The release of a key the slot captured is swallowed too (<c>_consumed</c>): <see cref="Button"/> activates on the
/// Space *release*, whatever happened to the press, so binding Space disarmed the slot and its own release armed it
/// straight back again.
/// The key that armed the slot is remembered (<c>_armedBy</c>) and ignored until it is released, because
/// <see cref="Button"/> activates from the Enter *press*: the slot is armed while Enter is still down, and the OS
/// auto-repeat's next press would otherwise be captured as the chord Enter — binding the 3270 Enter key to whatever
/// row the user was only opening. A fresh press of the same key after its release is a chord like any other. Neither
/// the click-armed nor the Space-armed slot needs the rule: a click is no key at all, and Space arms on its release,
/// so in both cases nothing is being held down when the slot becomes armed.</summary>
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
    private Key? _armedBy;

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

    public static readonly DirectProperty<ChordCaptureBox, bool> IsArmedProperty =
        AvaloniaProperty.RegisterDirect<ChordCaptureBox, bool>(nameof(IsArmed), box => box.IsArmed);

    private bool _isArmed;

    /// <summary>Read-only, and a property a binding can follow: the row's Cancel button shows while it is true.</summary>
    public bool IsArmed
    {
        get => _isArmed;
        private set => SetAndRaise(IsArmedProperty, ref _isArmed, value);
    }

    /// <summary>The way out that binds nothing, for the row's Cancel button. The slot keeps its focus, so a keyboard
    /// user who reached for the mouse can Tab on from it.</summary>
    public void Cancel() => Disarm();

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
            if (_consumed == e.Key)
            {
                // The OS auto-repeat of a key the slot captured and the user still holds: an accepted Enter would
                // otherwise activate Button here and arm the slot again, wiping the note it had just shown.
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
            // Button activates from the Enter press, so a slot armed by this very event is armed with its key still
            // down. Remember it, and the auto-repeat below is ignored rather than captured.
            if (IsArmed) _armedBy = e.Key;
            return;
        }
        if (_armedBy == e.Key)
        {
            // The OS auto-repeat of the key that armed the slot: handled, and nothing offered, until it is released.
            e.Handled = true;
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
        // The release of the key that armed the slot belongs to that activation, so it is swallowed and clears the
        // rule above: the next press of the same key is a chord like any other.
        if (_armedBy == e.Key)
        {
            _armedBy = null;
            e.Handled = true;
            return;
        }
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

    /// <summary>Unconditional: every transient record goes with the focus. A key consumed by a silent accept (Space,
    /// whose release now reaches whatever took the focus) would otherwise stay owed a swallow, and the next Space
    /// that tried to arm this slot would be eaten.</summary>
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        Disarm();
        base.OnLostFocus(e);
    }

    /// <summary>A DispatcherTimer runs whether or not its control is on screen, so a note still showing when the tab
    /// is torn down would leave one behind, holding this control alive until it fires. Losing focus gets there first
    /// in every path the tests can drive — a note only ever shows on a focused slot, and losing focus clears it — so
    /// no test fails without these two lines; they are the belt to that braces.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _expiry?.Stop();
        _expiry = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Arm()
    {
        IsArmed = true;
        _taps.Reset();
        _consumed = null;
        _armedBy = null;
        Show(null, expires: false);
        Focus();
    }

    private void Disarm()
    {
        IsArmed = false;
        _taps.Reset();
        // A refusal leaves the refused key consumed; disarming before its release (a click elsewhere) would otherwise
        // leave the slot owing a swallow, and the next Space activation on it would be eaten.
        _consumed = null;
        _armedBy = null;
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
