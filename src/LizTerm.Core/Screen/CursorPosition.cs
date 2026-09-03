namespace LizTerm.Core.Screen;

/// <summary>Zero-based cursor position.</summary>
public readonly record struct CursorPosition(int Row, int Column, bool Visible)
{
    public static readonly CursorPosition Hidden = new(0, 0, false);
}
