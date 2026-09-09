// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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
        foreach (var cell in cells)
        {
            if (!cell.Rendition.HasFlag(CellRendition.Blink)) continue;
            HasBlink = true;
            break;
        }
    }

    /// <summary>Whether any cell carries <see cref="CellRendition.Blink"/>. Answered once here, where the cells
    /// are already in hand, so the UI does not rescan the grid on every published screen to decide whether a
    /// blink timer should run.</summary>
    public bool HasBlink { get; }

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

    /// <summary>Text of a rectangular region: one line per row, trailing spaces trimmed, rows joined by '\n'
    /// with no trailing newline. The region is clamped to the screen; nothing left means "".</summary>
    public string GetText(ScreenRegion region)
    {
        if (region.Clamp(Rows, Columns) is not { } r) return "";
        var lines = new string[r.Rows];
        for (var row = r.Top; row <= r.Bottom; row++)
            lines[row - r.Top] = GetText(row, r.Left, r.Columns).TrimEnd(' ');
        return string.Join('\n', lines);
    }

    /// <summary>The maximal run of non-space cells on the row containing the cell, or null when the cell is a
    /// space or off the screen. Non-space is the whole rule, so SYS1.PROCLIB(IEFBR14) is one word.</summary>
    public ScreenRegion? WordAt(int row, int column)
    {
        if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns) return null;
        if (this[row, column].Character == Cell.Space) return null;
        var left = column;
        while (left > 0 && this[row, left - 1].Character != Cell.Space) left--;
        var right = column;
        while (right < Columns - 1 && this[row, right + 1].Character != Cell.Space) right++;
        return ScreenRegion.FromCorners(row, left, row, right);
    }

    public static ScreenSnapshot Empty(int rows, int columns)
    {
        var cells = new Cell[rows * columns];
        Array.Fill(cells, Cell.Blank(HostColor.NeutralWhite, HostColor.NeutralBlack));
        return new ScreenSnapshot(rows, columns, cells, CursorPosition.Hidden);
    }
}
