namespace LizTerm.Core.Screen;

/// <summary>An inclusive, zero-based rectangle of cells. Always normalized: Top &lt;= Bottom and Left &lt;= Right.
/// Coordinates, not content: after a host update the same region may cover different text.</summary>
public readonly record struct ScreenRegion
{
    public int Top { get; }
    public int Left { get; }
    public int Bottom { get; }
    public int Right { get; }

    private ScreenRegion(int top, int left, int bottom, int right)
    {
        Top = top;
        Left = left;
        Bottom = bottom;
        Right = right;
    }

    /// <summary>Builds the rectangle spanning two corners given in any order.</summary>
    public static ScreenRegion FromCorners(int row1, int column1, int row2, int column2) =>
        new(Math.Min(row1, row2), Math.Min(column1, column2), Math.Max(row1, row2), Math.Max(column1, column2));

    /// <summary>The whole rows x columns grid.</summary>
    public static ScreenRegion Full(int rows, int columns)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        return new ScreenRegion(0, 0, rows - 1, columns - 1);
    }

    public int Rows => Bottom - Top + 1;
    public int Columns => Right - Left + 1;

    public bool Contains(int row, int column) =>
        row >= Top && row <= Bottom && column >= Left && column <= Right;

    /// <summary>Trims the region to a rows x columns grid; null when nothing remains.</summary>
    public ScreenRegion? Clamp(int rows, int columns)
    {
        var top = Math.Max(Top, 0);
        var left = Math.Max(Left, 0);
        var bottom = Math.Min(Bottom, rows - 1);
        var right = Math.Min(Right, columns - 1);
        if (top > bottom || left > right) return null;
        return new ScreenRegion(top, left, bottom, right);
    }
}
