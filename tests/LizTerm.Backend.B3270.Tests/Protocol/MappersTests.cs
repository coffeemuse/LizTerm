using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests.Protocol;

public class MappersTests
{
    [Theory]
    [InlineData("neutralBlack", HostColor.NeutralBlack)]
    [InlineData("blue", HostColor.Blue)]
    [InlineData("neutralWhite", HostColor.NeutralWhite)]
    [InlineData("paleTurquoise", HostColor.PaleTurquoise)]
    [InlineData("default", HostColor.Default)]
    [InlineData(null, HostColor.Default)]
    [InlineData("nonsense", HostColor.Default)]
    public void ParseColor_maps_b3270_names(string? name, HostColor expected) =>
        Assert.Equal(expected, ColorNames.ParseColor(name));

    [Theory]
    [InlineData(null, CellRendition.None)]
    [InlineData("default", CellRendition.None)]
    [InlineData("underline", CellRendition.Underline)]
    [InlineData("highlight,selectable", CellRendition.Highlight | CellRendition.Selectable)]
    [InlineData("reverse,blink,order,private-use,no-copy,wrap,left-half,right-half,wide", CellRendition.Reverse | CellRendition.Blink | CellRendition.Order | CellRendition.PrivateUse | CellRendition.NoCopy | CellRendition.Wrap | CellRendition.LeftHalf | CellRendition.RightHalf | CellRendition.Wide)]
    public void ParseRendition_maps_comma_lists(string? gr, CellRendition expected) =>
        Assert.Equal(expected, ColorNames.ParseRendition(gr));

    [Theory]
    [InlineData(TerminalKey.Enter, "Enter", new string[0])]
    [InlineData(TerminalKey.PF1, "PF", new[] { "1" })]
    [InlineData(TerminalKey.PF24, "PF", new[] { "24" })]
    [InlineData(TerminalKey.PA3, "PA", new[] { "3" })]
    [InlineData(TerminalKey.Backspace, "BackSpace", new string[0])]
    [InlineData(TerminalKey.EraseEof, "EraseEOF", new string[0])]
    [InlineData(TerminalKey.Insert, "ToggleInsert", new string[0])]
    [InlineData(TerminalKey.BackTab, "BackTab", new string[0])]
    [InlineData(TerminalKey.Erase, "Erase", new string[0])]
    [InlineData(TerminalKey.SysReq, "SysReq", new string[0])]
    public void ActionMap_names_match_x3270_actions(TerminalKey key, string name, string[] args)
    {
        var action = ActionMap.ForKey(key);
        Assert.Equal(name, action.Name);
        Assert.Equal(args, action.Args);
    }

    [Fact]
    public void Every_TerminalKey_has_an_action()
    {
        foreach (var key in Enum.GetValues<TerminalKey>())
            Assert.NotEmpty(ActionMap.ForKey(key).Name);
    }

    [Fact]
    public void RunOperation_serializes_tag_and_actions()
    {
        var json = RunOperation.Serialize("7", [new B3270Action("PF", "3"), new B3270Action("Enter")]);
        Assert.Equal("""{"run":{"r-tag":"7","actions":[{"action":"PF","args":["3"]},{"action":"Enter"}]}}""", json);
        Assert.DoesNotContain('\n', json);
    }

    [Fact]
    public void RunOperation_escapes_text()
    {
        var json = RunOperation.Serialize("1", [new B3270Action("String", "say \"hi\"\\")]);
        Assert.Equal("""{"run":{"r-tag":"1","actions":[{"action":"String","args":["say \"hi\"\\"]}]}}""", json);
    }

    [Fact]
    public void HostString_plain()
    {
        var p = new SessionProfile { Name = "a", Host = "mvs.example", Port = 3270 };
        Assert.Equal("mvs.example:3270", HostStringBuilder.Build(p));
    }

    [Fact]
    public void HostString_tls_and_lu()
    {
        var p = new SessionProfile { Name = "a", Host = "mvs.example", Port = 992, UseTls = true, LuName = "LU01" };
        Assert.Equal("L:LU01@mvs.example:992", HostStringBuilder.Build(p));
    }

    [Fact]
    public void HostString_brackets_ipv6()
    {
        var p = new SessionProfile { Name = "a", Host = "::1", Port = 23 };
        Assert.Equal("[::1]:23", HostStringBuilder.Build(p));
    }

    [Theory]
    [InlineData(2, true, "3279-2-E")]
    [InlineData(4, false, "3279-4")]
    [InlineData(5, true, "3279-5-E")]
    public void ModelArgument_formats_model(int model, bool extended, string expected)
    {
        var p = new SessionProfile { Name = "a", Host = "h", Model = model, Extended = extended };
        Assert.Equal(expected, HostStringBuilder.ModelArgument(p));
    }
}
