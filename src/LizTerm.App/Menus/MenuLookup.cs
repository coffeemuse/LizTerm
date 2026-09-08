using Avalonia.Controls;

namespace LizTerm.App.Menus;

/// <summary>Finding a NativeMenuItem by its header path. NativeMenuItem is not a Control, so FindControl
/// cannot reach it and the visual tree does not contain it; this is the one way the window's code-behind and
/// the tests both look one up, so they cannot disagree about what "the Wire Log item" means.</summary>
internal static class MenuLookup
{
    public static NativeMenuItem? Item(NativeMenu? menu, string top, string child) =>
        Item(Item(menu, top)?.Menu, child);

    public static NativeMenuItem? Item(NativeMenu? menu, string header) =>
        menu?.Items.OfType<NativeMenuItem>().FirstOrDefault(i => i.Header == header);

    /// <summary>The item, or null when there is no menu to look in — which under the classic strategy is the
    /// deliberate state, since ApplyMenuStrategy detaches the window's NativeMenu.
    ///
    /// A menu that IS there and does not hold the item is a header typo, not a strategy, and throws rather than
    /// answering null. Both callers name items by string and act on what comes back, so a silent null would mean
    /// About shown twice on macOS or the Edit key equivalents quietly gone — and renaming a header in both menus
    /// at once keeps the parity guard green, so nothing else would notice.</summary>
    public static NativeMenuItem? Required(NativeMenu? menu, string top, string child) =>
        menu is null
            ? null
            : Item(menu, top, child)
              ?? throw new InvalidOperationException($"The native menu declares no item {top} > {child}.");

    /// <summary>The separator immediately above an item, if it has one. An item hidden at the end of a menu
    /// leaves the menu ending in a divider otherwise: neither NSMenu nor the classic Menu collapses a trailing
    /// separator, and the exporter honours IsVisible on a separator because NativeMenuItemSeparator derives
    /// from NativeMenuItem.</summary>
    public static NativeMenuItemSeparator? SeparatorAbove(NativeMenuItem item)
    {
        var siblings = item.Parent?.Items;
        var index = siblings?.IndexOf(item) ?? -1;
        return index > 0 ? siblings![index - 1] as NativeMenuItemSeparator : null;
    }
}
