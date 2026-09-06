using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The built-in defaults: Vista TN3270's table, cross-checked against wc3270 (spec 6.2). Clear has a
/// second home on Ctrl+Escape because Mac keyboards have no Pause key, PA1 to PA3 a second home on Alt+1 to Alt+3
/// because Mac laptops have no Insert key, and Reset a second home on Ctrl+R (wc3270's) in case a platform never
/// reports the Left Ctrl tap. Copy, paste, and select-all are platform hotkeys checked before this table and are
/// deliberately absent from it. Not user-editable yet; see <see cref="Keymap.With"/>.</summary>
public static class DefaultKeymap
{
    private static readonly Keymap Erasing = Build(destructiveBackspace: true);
    private static readonly Keymap CursorLeft = Build(destructiveBackspace: false);

    /// <param name="destructiveBackspace">The profile's choice: Backspace as <see cref="TerminalKey.Erase"/> (true,
    /// the default) or as the cursor-left <see cref="TerminalKey.Backspace"/>.</param>
    public static Keymap Create(bool destructiveBackspace) => destructiveBackspace ? Erasing : CursorLeft;

    /// <summary>Shim for the pre-table callers; removed in the task that teaches the screen control taps.</summary>
    public static bool TryMap(Key key, KeyModifiers modifiers, bool destructiveBackspace, out TerminalKey terminalKey) =>
        Create(destructiveBackspace).TryMap(new KeyChord(key, modifiers), out terminalKey);

    private static Keymap Build(bool destructiveBackspace)
    {
        var keys = new List<KeyValuePair<KeyChord, TerminalKey>>();
        void Add(Key key, TerminalKey terminal, KeyModifiers modifiers = KeyModifiers.None) =>
            keys.Add(KeyValuePair.Create(new KeyChord(key, modifiers), terminal));
        void Tap(Key key, TerminalKey terminal) => keys.Add(KeyValuePair.Create(KeyChord.TapOf(key), terminal));

        Add(Key.Enter, TerminalKey.Enter);
        Add(Key.Enter, TerminalKey.Enter, KeyModifiers.Control);
        Tap(Key.RightCtrl, TerminalKey.Enter);
        Add(Key.Enter, TerminalKey.Newline, KeyModifiers.Shift);
        Add(Key.Escape, TerminalKey.Attn);
        Add(Key.Escape, TerminalKey.SysReq, KeyModifiers.Shift);
        Tap(Key.LeftCtrl, TerminalKey.Reset);
        Add(Key.R, TerminalKey.Reset, KeyModifiers.Control);
        Add(Key.Pause, TerminalKey.Clear);
        Add(Key.Escape, TerminalKey.Clear, KeyModifiers.Control);
        for (var i = 0; i < 12; i++)
        {
            Add(Key.F1 + i, TerminalKey.PF1 + i);
            Add(Key.F1 + i, TerminalKey.PF13 + i, KeyModifiers.Shift);
            Add(Key.F1 + i, TerminalKey.PF13 + i, KeyModifiers.Control);
        }
        Add(Key.PageUp, TerminalKey.PF7);
        Add(Key.PageDown, TerminalKey.PF8);
        Add(Key.Insert, TerminalKey.PA1, KeyModifiers.Control);
        Add(Key.Home, TerminalKey.PA2, KeyModifiers.Control);
        Add(Key.PageUp, TerminalKey.PA3, KeyModifiers.Control);
        Add(Key.D1, TerminalKey.PA1, KeyModifiers.Alt);
        Add(Key.D2, TerminalKey.PA2, KeyModifiers.Alt);
        Add(Key.D3, TerminalKey.PA3, KeyModifiers.Alt);
        Add(Key.Tab, TerminalKey.Tab);
        Add(Key.Tab, TerminalKey.BackTab, KeyModifiers.Shift);
        Add(Key.Insert, TerminalKey.Insert);
        Add(Key.Home, TerminalKey.Home);
        Add(Key.End, TerminalKey.EraseEof);
        Add(Key.Delete, TerminalKey.Delete);
        Add(Key.Back, destructiveBackspace ? TerminalKey.Erase : TerminalKey.Backspace);
        Add(Key.Up, TerminalKey.Up);
        Add(Key.Down, TerminalKey.Down);
        Add(Key.Left, TerminalKey.Left);
        Add(Key.Right, TerminalKey.Right);

        var text = new List<KeyValuePair<KeyChord, string>>
        {
            KeyValuePair.Create(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control), "¬"),   // Vista: Ctrl+[ is the NOT sign
            KeyValuePair.Create(new KeyChord(Key.D6, KeyModifiers.Control), "¢"),                // Vista: Ctrl+6 is the cent sign
        };
        return new Keymap(keys, text);
    }
}
