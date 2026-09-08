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
}
