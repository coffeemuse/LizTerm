using System.Text.RegularExpressions;

namespace LizTerm.App.Tests;

public class AppVersionTests
{
    [Fact]
    public void Version_is_a_plain_three_part_number()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppVersion.Current);
        Assert.DoesNotContain("+", AppVersion.Current);
    }
}
