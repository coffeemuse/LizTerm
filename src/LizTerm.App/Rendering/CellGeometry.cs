using Avalonia;

namespace LizTerm.App.Rendering;

/// <summary>Scale-to-fit layout of a rows x columns character grid inside an area. Pure math; no Avalonia rendering.</summary>
public readonly record struct CellGeometry(double CellWidth, double CellHeight, double FontSize, double OriginX, double OriginY)
{
    /// <param name="advancePerEm">Glyph advance width divided by font size, measured once from the font.</param>
    /// <param name="lineHeightPerEm">Line height divided by font size.</param>
    public static CellGeometry Fit(double availableWidth, double availableHeight, int rows, int columns, double advancePerEm, double lineHeightPerEm)
    {
        if (rows <= 0 || columns <= 0 || advancePerEm <= 0 || lineHeightPerEm <= 0) return default;
        var byWidth = availableWidth / (columns * advancePerEm);
        var byHeight = availableHeight / (rows * lineHeightPerEm);
        var fontSize = Math.Max(1, Math.Floor(Math.Min(byWidth, byHeight)));
        var cellWidth = fontSize * advancePerEm;
        var cellHeight = fontSize * lineHeightPerEm;
        var originX = Math.Max(0, (availableWidth - cellWidth * columns) / 2);
        var originY = Math.Max(0, (availableHeight - cellHeight * rows) / 2);
        return new CellGeometry(cellWidth, cellHeight, fontSize, originX, originY);
    }

    public (int Row, int Column)? HitTest(double x, double y, int rows, int columns)
    {
        if (CellWidth <= 0 || CellHeight <= 0) return null;
        var column = (int)Math.Floor((x - OriginX) / CellWidth);
        var row = (int)Math.Floor((y - OriginY) / CellHeight);
        if (row < 0 || row >= rows || column < 0 || column >= columns) return null;
        return (row, column);
    }

    public Rect CellRect(int row, int column) =>
        new(OriginX + column * CellWidth, OriginY + row * CellHeight, CellWidth, CellHeight);
}
