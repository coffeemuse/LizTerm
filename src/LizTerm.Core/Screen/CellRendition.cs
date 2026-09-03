namespace LizTerm.Core.Screen;

[Flags]
public enum CellRendition
{
    None = 0,
    Underline = 1,
    Blink = 2,
    Highlight = 4,
    Selectable = 8,
    Reverse = 16,
    Wide = 32,
    Order = 64,
    PrivateUse = 128,
    NoCopy = 256,
    Wrap = 512,
    LeftHalf = 1024,
    RightHalf = 2048,
}
