using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace LizTerm.App.Tests.Views;

/// <summary>The native menu definition, reached through NativeMenu.GetMenu rather than the visual tree:
/// NativeMenuItem is not a Control, so FindControl cannot see it. Bindings on it do resolve under the
/// headless platform, two-way write-back included, which is what makes these assertions possible.</summary>
public class NativeMenuTests
{
    [AvaloniaFact]
    public void The_application_menu_carries_about_and_quit()
    {
        var menu = NativeMenu.GetMenu(Application.Current!);

        Assert.NotNull(menu);
        var headers = menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray();
        Assert.Equal(["About LizTerm", "Quit LizTerm"], headers);
    }

    /// <summary>The one gesture outside the Edit menu in this whole feature. The application menu is macOS-only
    /// by construction and Keymap claims no Meta chord, so Cmd+Q cannot swallow a key the host needed; omitting
    /// it is the riskier choice, since a replaced application menu may not inherit AppKit's own Quit item.</summary>
    [AvaloniaFact]
    public void Quit_carries_cmd_q_and_about_carries_no_gesture()
    {
        var menu = NativeMenu.GetMenu(Application.Current!)!;
        var items = menu.Items.OfType<NativeMenuItem>().ToArray();

        Assert.Null(items.Single(i => i.Header == "About LizTerm").Gesture);
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Meta), items.Single(i => i.Header == "Quit LizTerm").Gesture);
    }
}
