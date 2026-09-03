using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The built-in physical-key to 3270-key mapping. Not user-editable in v1.</summary>
public static class DefaultKeymap
{
    public static bool TryMap(Key key, KeyModifiers modifiers, out TerminalKey terminalKey)
    {
        terminalKey = default;
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) return false;
        var shift = (modifiers & KeyModifiers.Shift) != 0;

        if (key >= Key.F1 && key <= Key.F12)
        {
            terminalKey = TerminalKey.PF1 + (key - Key.F1) + (shift ? 12 : 0);
            return true;
        }

        TerminalKey? mapped = key switch
        {
            Key.Enter => TerminalKey.Enter,
            Key.Escape => TerminalKey.Reset,
            Key.Tab => shift ? TerminalKey.BackTab : TerminalKey.Tab,
            Key.Insert => TerminalKey.Insert,
            Key.Home => TerminalKey.Home,
            Key.End => TerminalKey.EraseEof,
            Key.Delete => TerminalKey.Delete,
            Key.Back => TerminalKey.Backspace,
            Key.Up => TerminalKey.Up,
            Key.Down => TerminalKey.Down,
            Key.Left => TerminalKey.Left,
            Key.Right => TerminalKey.Right,
            Key.PageUp => TerminalKey.PA1,
            Key.PageDown => TerminalKey.PA2,
            _ => null,
        };
        if (mapped is null) return false;
        terminalKey = mapped.Value;
        return true;
    }
}
