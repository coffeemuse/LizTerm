using LizTerm.Core.Screen;

namespace LizTerm.Backend.B3270.Protocol;

public static class ColorNames
{
    private static readonly Dictionary<string, HostColor> Colors = new(StringComparer.Ordinal)
    {
        ["neutralBlack"] = HostColor.NeutralBlack,
        ["blue"] = HostColor.Blue,
        ["red"] = HostColor.Red,
        ["pink"] = HostColor.Pink,
        ["green"] = HostColor.Green,
        ["turquoise"] = HostColor.Turquoise,
        ["yellow"] = HostColor.Yellow,
        ["neutralWhite"] = HostColor.NeutralWhite,
        ["black"] = HostColor.Black,
        ["deepBlue"] = HostColor.DeepBlue,
        ["orange"] = HostColor.Orange,
        ["purple"] = HostColor.Purple,
        ["paleGreen"] = HostColor.PaleGreen,
        ["paleTurquoise"] = HostColor.PaleTurquoise,
        ["grey"] = HostColor.Grey,
        ["white"] = HostColor.White,
    };

    private static readonly Dictionary<string, CellRendition> Renditions = new(StringComparer.Ordinal)
    {
        ["underline"] = CellRendition.Underline,
        ["blink"] = CellRendition.Blink,
        ["highlight"] = CellRendition.Highlight,
        ["selectable"] = CellRendition.Selectable,
        ["reverse"] = CellRendition.Reverse,
        ["wide"] = CellRendition.Wide,
        ["order"] = CellRendition.Order,
        ["private-use"] = CellRendition.PrivateUse,
        ["no-copy"] = CellRendition.NoCopy,
        ["wrap"] = CellRendition.Wrap,
        ["left-half"] = CellRendition.LeftHalf,
        ["right-half"] = CellRendition.RightHalf,
    };

    public static HostColor ParseColor(string? name) =>
        name is not null && Colors.TryGetValue(name, out var color) ? color : HostColor.Default;

    public static CellRendition ParseRendition(string? gr)
    {
        if (string.IsNullOrEmpty(gr) || gr == "default") return CellRendition.None;
        var result = CellRendition.None;
        foreach (var part in gr.Split(','))
            if (Renditions.TryGetValue(part.Trim(), out var flag)) result |= flag;
        return result;
    }
}
