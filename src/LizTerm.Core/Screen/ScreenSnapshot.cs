using System.Text;

namespace LizTerm.Core.Screen;

public sealed class ScreenSnapshot
{
    private readonly Cell[] _cells;

    public ScreenSnapshot(int rows, int columns, Cell[] cells, CursorPosition cursor)
    {
        if (cells.Length != rows * columns)
            throw new ArgumentException($"Expected {rows * columns} cells, got {cells.Length}", nameof(cells));
        Rows = rows;
        Columns = columns;
        _cells = cells;
        Cursor = cursor;
    }

    public int Rows { get; }
    public int Columns { get; }
    public CursorPosition Cursor { get; }

    public Cell this[int row, int column] => _cells[row * Columns + column];

    public ReadOnlySpan<Cell> Row(int row) => _cells.AsSpan(row * Columns, Columns);

    public string GetText(int row, int column, int length)
    {
        var sb = new StringBuilder(length);
        var end = Math.Min(column + length, Columns);
        for (var c = column; c < end; c++)
            sb.Append(this[row, c].Character.ToString());
        return sb.ToString();
    }

    public string RowText(int row) => GetText(row, 0, Columns);

    public string ToText()
    {
        var lines = new string[Rows];
        for (var r = 0; r < Rows; r++) lines[r] = RowText(r);
        return string.Join('\n', lines);
    }

    public static ScreenSnapshot Empty(int rows, int columns)
    {
        var cells = new Cell[rows * columns];
        Array.Fill(cells, Cell.Blank(HostColor.NeutralWhite, HostColor.NeutralBlack));
        return new ScreenSnapshot(rows, columns, cells, CursorPosition.Hidden);
    }
}
