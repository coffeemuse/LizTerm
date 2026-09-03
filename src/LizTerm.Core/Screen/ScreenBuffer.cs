namespace LizTerm.Core.Screen;

/// <summary>Mutable screen the backend owns. Not thread-safe; one writer.</summary>
public sealed class ScreenBuffer
{
    private Cell[] _cells;

    public ScreenBuffer(int rows, int columns)
    {
        _cells = [];
        Resize(rows, columns, HostColor.NeutralWhite, HostColor.NeutralBlack);
    }

    public int Rows { get; private set; }
    public int Columns { get; private set; }
    public CursorPosition Cursor { get; private set; } = CursorPosition.Hidden;

    public void Resize(int rows, int columns, HostColor foreground, HostColor background)
    {
        if (rows <= 0 || columns <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        Rows = rows;
        Columns = columns;
        _cells = new Cell[rows * columns];
        Erase(foreground, background);
    }

    public void Erase(HostColor foreground, HostColor background) =>
        Array.Fill(_cells, Cell.Blank(foreground, background));

    public void SetText(int row, int column, string text, HostColor? foreground, HostColor? background, CellRendition? rendition)
    {
        var col = column;
        foreach (var rune in text.EnumerateRunes())
        {
            if (col >= Columns) break;
            var index = Index(row, col);
            var cell = _cells[index];
            _cells[index] = new Cell(rune, foreground ?? cell.Foreground, background ?? cell.Background, rendition ?? cell.Rendition);
            col++;
        }
    }

    public void SetAttributes(int row, int column, int count, HostColor? foreground, HostColor? background, CellRendition? rendition)
    {
        var end = Math.Min(column + count, Columns);
        for (var col = column; col < end; col++)
        {
            var index = Index(row, col);
            var cell = _cells[index];
            _cells[index] = cell with
            {
                Foreground = foreground ?? cell.Foreground,
                Background = background ?? cell.Background,
                Rendition = rendition ?? cell.Rendition,
            };
        }
    }

    public void SetCursor(CursorPosition cursor) => Cursor = cursor;

    public ScreenSnapshot Snapshot() => new(Rows, Columns, (Cell[])_cells.Clone(), Cursor);

    private int Index(int row, int column)
    {
        if ((uint)row >= (uint)Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if ((uint)column >= (uint)Columns) throw new ArgumentOutOfRangeException(nameof(column));
        return row * Columns + column;
    }
}
