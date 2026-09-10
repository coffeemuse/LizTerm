// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;

namespace LizTerm.Core.Session;

/// <summary>An oversize screen geometry, <b>columns by rows</b>, as b3270's <c>-oversize</c> takes it. Note that
/// <see cref="TerminalModel.ToString"/> renders the opposite order (<c>2 — 24x80</c> is rows by columns), which is
/// conventional for naming a 3270 model and is deliberately left disagreeing with this one: the resolution is
/// labelling, not reordering, so every message below names which number is which (spec 4.3).
///
/// The rules are b3270's, from <c>Common/ctlr.c</c>, and it refuses a bad one with a popup — which reaches LizTerm
/// as an unexplained HostMessage and no connection, with nothing tying it to the field the user typed. That is why
/// this validates in the editor instead (spec 4.2).</summary>
public sealed record OversizeGeometry(int Columns, int Rows)
{
    /// <summary>b3270's <c>MAX_ROWS_COLS</c>. It is both a per-dimension ceiling and, compared against
    /// <c>columns * rows</c>, an <b>area</b> limit — which is what caps a usable oversize far below Vista's
    /// advertised 200x200: at 160 columns it allows 102 rows.</summary>
    public const int MaxCells = 0x3fff;

    /// <summary>What <c>BuildArguments</c> passes to <c>-oversize</c>.</summary>
    public override string ToString() => $"{Columns}x{Rows}";

    /// <param name="model">The model the profile has chosen, whose own geometry is the floor. A model outside
    /// the catalogue arrives with zero rows and columns and skips that one rule.</param>
    /// <param name="geometry">Null when the text names no oversize, which is valid.</param>
    public static bool TryParse(string? text, TerminalModel model, out OversizeGeometry? geometry, out string? error)
    {
        geometry = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var parts = text.Trim().Split('x', 'X');
        if (parts.Length != 2 || !TryParseDimension(parts[0], out var columns) || !TryParseDimension(parts[1], out var rows))
        {
            error = "Oversize must be columns by rows, for example 132x43.";
            return false;
        }

        // b3270's own spelling of "none", which a hand-edited profile can carry.
        if (columns == 0 && rows == 0) return true;
        if (columns == 0 || rows == 0)
        {
            error = "Oversize needs both a column count and a row count, for example 132x43.";
            return false;
        }
        if (columns > MaxCells) { error = $"Oversize columns must be at most {MaxCells}."; return false; }
        if (rows > MaxCells) { error = $"Oversize rows must be at most {MaxCells}."; return false; }
        // Both dimensions are now within the ceiling, so the product cannot overflow an int and the division
        // in the message cannot divide by zero.
        if ((long)columns * rows > MaxCells)
        {
            error = $"{columns} columns by {rows} rows is {(columns * rows).ToString("N0", CultureInfo.InvariantCulture)} cells; "
                + $"b3270 allows {MaxCells.ToString("N0", CultureInfo.InvariantCulture)}. "
                + $"At {columns} columns the most is {MaxCells / columns} rows.";
            return false;
        }
        if (model.Columns > 0 && model.Rows > 0 && (columns < model.Columns || rows < model.Rows))
        {
            error = $"Oversize must be at least {model.Columns} columns and {model.Rows} rows for model {model.Number}.";
            return false;
        }

        geometry = new OversizeGeometry(columns, rows);
        return true;
    }

    /// <summary>Plain digits only, which is <see cref="PlainNumber"/>'s whole job: "-5", "+5" and " 5" are format
    /// errors rather than reaching the engine, and no trimming happens here, so "13 2x43" stays one. The ceiling
    /// is left to the rules above, which name columns and rows separately.</summary>
    private static bool TryParseDimension(string text, out int value) =>
        PlainNumber.TryParse(text, 0, int.MaxValue, out value);
}
