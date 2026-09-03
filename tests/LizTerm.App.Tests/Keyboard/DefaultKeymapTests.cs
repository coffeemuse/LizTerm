using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Keyboard;

public class DefaultKeymapTests
{
    [Theory]
    [InlineData(Key.Enter, KeyModifiers.None, TerminalKey.Enter)]
    [InlineData(Key.F1, KeyModifiers.None, TerminalKey.PF1)]
    [InlineData(Key.F12, KeyModifiers.None, TerminalKey.PF12)]
    [InlineData(Key.F1, KeyModifiers.Shift, TerminalKey.PF13)]
    [InlineData(Key.F12, KeyModifiers.Shift, TerminalKey.PF24)]
    [InlineData(Key.Escape, KeyModifiers.None, TerminalKey.Reset)]
    [InlineData(Key.Tab, KeyModifiers.None, TerminalKey.Tab)]
    [InlineData(Key.Tab, KeyModifiers.Shift, TerminalKey.BackTab)]
    [InlineData(Key.Insert, KeyModifiers.None, TerminalKey.Insert)]
    [InlineData(Key.Home, KeyModifiers.None, TerminalKey.Home)]
    [InlineData(Key.End, KeyModifiers.None, TerminalKey.EraseEof)]
    [InlineData(Key.Delete, KeyModifiers.None, TerminalKey.Delete)]
    [InlineData(Key.Back, KeyModifiers.None, TerminalKey.Backspace)]
    [InlineData(Key.Up, KeyModifiers.None, TerminalKey.Up)]
    [InlineData(Key.Down, KeyModifiers.None, TerminalKey.Down)]
    [InlineData(Key.Left, KeyModifiers.None, TerminalKey.Left)]
    [InlineData(Key.Right, KeyModifiers.None, TerminalKey.Right)]
    [InlineData(Key.PageUp, KeyModifiers.None, TerminalKey.PA1)]
    [InlineData(Key.PageDown, KeyModifiers.None, TerminalKey.PA2)]
    public void Maps_default_keys(Key key, KeyModifiers modifiers, TerminalKey expected)
    {
        Assert.True(DefaultKeymap.TryMap(key, modifiers, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(Key.A, KeyModifiers.None)]
    [InlineData(Key.C, KeyModifiers.Control)]
    [InlineData(Key.F1, KeyModifiers.Control)]
    [InlineData(Key.Enter, KeyModifiers.Meta)]
    [InlineData(Key.LeftShift, KeyModifiers.Shift)]
    public void Leaves_text_and_shortcut_keys_unmapped(Key key, KeyModifiers modifiers) =>
        Assert.False(DefaultKeymap.TryMap(key, modifiers, out _));
}
