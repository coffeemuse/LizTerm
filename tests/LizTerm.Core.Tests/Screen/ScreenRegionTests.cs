using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenRegionTests
{
    [Fact]
    public void FromCorners_normalizes_any_corner_order()
    {
        var expected = ScreenRegion.FromCorners(2, 3, 5, 9);
        Assert.Equal(expected, ScreenRegion.FromCorners(5, 9, 2, 3));
        Assert.Equal(expected, ScreenRegion.FromCorners(2, 9, 5, 3));
        Assert.Equal(expected, ScreenRegion.FromCorners(5, 3, 2, 9));
        Assert.Equal((2, 3, 5, 9), (expected.Top, expected.Left, expected.Bottom, expected.Right));
        Assert.Equal(4, expected.Rows);
        Assert.Equal(7, expected.Columns);
    }

    [Fact]
    public void Contains_is_inclusive_on_all_edges()
    {
        var region = ScreenRegion.FromCorners(2, 3, 5, 9);
        Assert.True(region.Contains(2, 3));
        Assert.True(region.Contains(5, 9));
        Assert.True(region.Contains(3, 6));
        Assert.False(region.Contains(1, 3));
        Assert.False(region.Contains(6, 9));
        Assert.False(region.Contains(2, 2));
        Assert.False(region.Contains(5, 10));
    }

    [Fact]
    public void Clamp_trims_an_overhanging_region()
    {
        Assert.Equal(ScreenRegion.Full(24, 80), ScreenRegion.FromCorners(-2, -1, 30, 100).Clamp(24, 80));
        Assert.Equal(ScreenRegion.FromCorners(20, 70, 23, 79), ScreenRegion.FromCorners(20, 70, 40, 90).Clamp(24, 80));
        Assert.Equal(ScreenRegion.FromCorners(1, 1, 2, 2), ScreenRegion.FromCorners(1, 1, 2, 2).Clamp(24, 80));
    }

    [Fact]
    public void Clamp_returns_null_when_nothing_remains()
    {
        Assert.Null(ScreenRegion.FromCorners(24, 0, 30, 10).Clamp(24, 80));
        Assert.Null(ScreenRegion.FromCorners(0, 80, 5, 90).Clamp(24, 80));
    }

    [Fact]
    public void Full_covers_the_grid()
    {
        var full = ScreenRegion.Full(24, 80);
        Assert.Equal((0, 0, 23, 79), (full.Top, full.Left, full.Bottom, full.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenRegion.Full(0, 80));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenRegion.Full(24, 0));
    }
}
