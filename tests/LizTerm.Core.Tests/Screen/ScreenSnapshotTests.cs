using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenSnapshotTests
{
    [Fact]
    public void RowText_and_ToText_join_characters()
    {
        var buffer = new ScreenBuffer(2, 4);
        buffer.SetText(0, 0, "ab", null, null, null);
        buffer.SetText(1, 2, "cd", null, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("ab  ", snap.RowText(0));
        Assert.Equal("ab  \n  cd", snap.ToText());
    }

    [Fact]
    public void Empty_has_requested_size()
    {
        var snap = ScreenSnapshot.Empty(32, 80);
        Assert.Equal(32, snap.Rows);
        Assert.Equal(80, snap.Row(31).Length);
    }

    /// <summary>A 32-column screen with the given rows written from column 0.</summary>
    private static ScreenSnapshot Screen(params string[] rows)
    {
        var buffer = new ScreenBuffer(rows.Length, 32);
        for (var r = 0; r < rows.Length; r++) buffer.SetText(r, 0, rows[r], null, null, null);
        return buffer.Snapshot();
    }

    [Fact]
    public void GetText_region_trims_trailing_spaces_and_joins_rows_with_newline()
    {
        var snap = Screen("ab  cd    ", "  ef      ", "          ");
        Assert.Equal("ab  cd\n  ef\n", snap.GetText(ScreenRegion.FromCorners(0, 0, 2, 9)));
    }

    [Fact]
    public void GetText_region_keeps_interior_spaces_and_a_single_row_has_no_newline()
    {
        var snap = Screen("SYS1.PROCLIB  JOB01234 ");
        Assert.Equal("1.PROCLIB  JOB", snap.GetText(ScreenRegion.FromCorners(0, 3, 0, 16)));
    }

    [Fact]
    public void GetText_region_is_clamped_to_the_snapshot()
    {
        var snap = Screen("abc", "def");
        Assert.Equal("bc\nef", snap.GetText(ScreenRegion.FromCorners(0, 1, 5, 40)));
        Assert.Equal("", snap.GetText(ScreenRegion.FromCorners(7, 0, 9, 3)));
    }

    [Fact]
    public void WordAt_returns_the_run_of_non_space_cells()
    {
        var snap = Screen("  SYS1.PROCLIB(IEFBR14) JOB01234");
        var word = ScreenRegion.FromCorners(0, 2, 0, 22);
        Assert.Equal(word, snap.WordAt(0, 7));    // middle
        Assert.Equal(word, snap.WordAt(0, 2));    // first cell
        Assert.Equal(word, snap.WordAt(0, 22));   // last cell
        Assert.Null(snap.WordAt(0, 23));          // the space between the words
        Assert.Null(snap.WordAt(0, 0));
    }

    [Fact]
    public void WordAt_stops_at_the_row_edges_and_rejects_cells_off_the_screen()
    {
        var snap = Screen("  SYS1.PROCLIB(IEFBR14) JOB01234");   // JOB01234 ends in the last column (31)
        Assert.Equal(ScreenRegion.FromCorners(0, 24, 0, 31), snap.WordAt(0, 26));
        var left = Screen("abc def");
        Assert.Equal(ScreenRegion.FromCorners(0, 0, 0, 2), left.WordAt(0, 1));
        Assert.Null(snap.WordAt(5, 0));
        Assert.Null(snap.WordAt(0, 32));
        Assert.Null(snap.WordAt(-1, 0));
    }

    /// <summary>The snapshot answers this once, where the cells are already being walked, so the UI does not
    /// rescan the whole grid on every published screen just to decide whether to run a blink timer.</summary>
    [Fact]
    public void Snapshot_knows_whether_any_cell_blinks()
    {
        var plain = new ScreenBuffer(4, 8);
        plain.SetText(0, 0, "hello", null, null, null);
        Assert.False(plain.Snapshot().HasBlink);

        var blinking = new ScreenBuffer(4, 8);
        blinking.SetText(1, 2, "X", null, null, CellRendition.Blink);
        Assert.True(blinking.Snapshot().HasBlink);
    }
}
