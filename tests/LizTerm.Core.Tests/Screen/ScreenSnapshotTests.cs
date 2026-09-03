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
}
