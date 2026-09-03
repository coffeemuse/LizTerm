using System.Text;
using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenBufferTests
{
    [Fact]
    public void New_buffer_is_blank_with_default_colors()
    {
        var buffer = new ScreenBuffer(24, 80);
        var snap = buffer.Snapshot();
        Assert.Equal(24, snap.Rows);
        Assert.Equal(80, snap.Columns);
        Assert.Equal(new Rune(' '), snap[0, 0].Character);
        Assert.Equal(HostColor.NeutralWhite, snap[0, 0].Foreground);
        Assert.Equal(HostColor.NeutralBlack, snap[0, 0].Background);
        Assert.False(snap.Cursor.Visible);
    }

    [Fact]
    public void SetText_writes_characters_and_given_attributes()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(1, 1, "Field:", HostColor.Red, null, CellRendition.Underline);
        var snap = buffer.Snapshot();
        Assert.Equal("Field:", snap.GetText(1, 1, 6));
        Assert.Equal(HostColor.Red, snap[1, 3].Foreground);
        Assert.Equal(HostColor.NeutralBlack, snap[1, 3].Background);
        Assert.Equal(CellRendition.Underline, snap[1, 3].Rendition);
        Assert.Equal(new Rune(' '), snap[1, 7].Character);
    }

    [Fact]
    public void SetText_leaves_absent_attributes_unchanged()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetAttributes(0, 0, 80, HostColor.Blue, HostColor.Red, CellRendition.Reverse);
        buffer.SetText(0, 5, "abc", null, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("abc", snap.GetText(0, 5, 3));
        Assert.Equal(HostColor.Blue, snap[0, 6].Foreground);
        Assert.Equal(HostColor.Red, snap[0, 6].Background);
        Assert.Equal(CellRendition.Reverse, snap[0, 6].Rendition);
    }

    [Fact]
    public void SetAttributes_keeps_characters()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 0, "hello", null, null, null);
        buffer.SetAttributes(2, 0, 5, HostColor.Green, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("hello", snap.GetText(2, 0, 5));
        Assert.Equal(HostColor.Green, snap[2, 4].Foreground);
        Assert.Equal(HostColor.NeutralWhite, snap[2, 5].Foreground);
    }

    [Fact]
    public void SetText_truncates_at_end_of_row()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(0, 78, "abcd", null, null, null);
        var snap = buffer.Snapshot();
        Assert.Equal("ab", snap.GetText(0, 78, 2));
        Assert.Equal(new Rune(' '), snap[1, 0].Character);
    }

    [Fact]
    public void Erase_blanks_everything_with_given_colors_and_keeps_size()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(3, 3, "xyz", HostColor.Red, null, null);
        buffer.Erase(HostColor.Blue, HostColor.NeutralBlack);
        var snap = buffer.Snapshot();
        Assert.Equal(new Rune(' '), snap[3, 3].Character);
        Assert.Equal(HostColor.Blue, snap[3, 3].Foreground);
        Assert.Equal(CellRendition.None, snap[3, 3].Rendition);
        Assert.Equal(24, snap.Rows);
    }

    [Fact]
    public void Resize_changes_dimensions_and_blanks()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.Resize(43, 80, HostColor.Blue, HostColor.NeutralBlack);
        var snap = buffer.Snapshot();
        Assert.Equal(43, snap.Rows);
        Assert.Equal(HostColor.Blue, snap[42, 79].Foreground);
    }

    [Fact]
    public void Snapshot_is_independent_of_later_mutation()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(0, 0, "one", null, null, null);
        var first = buffer.Snapshot();
        buffer.SetText(0, 0, "two", null, null, null);
        Assert.Equal("one", first.GetText(0, 0, 3));
        Assert.Equal("two", buffer.Snapshot().GetText(0, 0, 3));
    }

    [Fact]
    public void SetCursor_is_reflected_in_snapshot()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetCursor(new CursorPosition(1, 8, true));
        Assert.Equal(new CursorPosition(1, 8, true), buffer.Snapshot().Cursor);
    }
}
