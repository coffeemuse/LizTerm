using Avalonia.Input;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Keyboard;

public class ModifierTapDetectorTests
{
    [Fact]
    public void A_ctrl_key_pressed_and_released_alone_is_a_tap()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.RightCtrl);
        Assert.Equal(Key.RightCtrl, taps.KeyUp(Key.RightCtrl));
        taps.KeyDown(Key.LeftCtrl);
        Assert.Equal(Key.LeftCtrl, taps.KeyUp(Key.LeftCtrl));
    }

    [Fact]
    public void Any_key_between_the_press_and_the_release_cancels_the_tap()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.LeftCtrl);
        taps.KeyDown(Key.C);
        Assert.Null(taps.KeyUp(Key.C));
        Assert.Null(taps.KeyUp(Key.LeftCtrl));
    }

    [Fact]
    public void Auto_repeat_of_the_same_ctrl_key_keeps_the_tap()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.LeftCtrl);
        taps.KeyDown(Key.LeftCtrl);
        Assert.Equal(Key.LeftCtrl, taps.KeyUp(Key.LeftCtrl));
    }

    [Fact]
    public void Other_keys_are_never_taps_and_reset_clears_a_pending_one()
    {
        var taps = new ModifierTapDetector();
        taps.KeyDown(Key.LeftShift);
        Assert.Null(taps.KeyUp(Key.LeftShift));
        taps.KeyDown(Key.RightCtrl);
        taps.Reset();
        Assert.Null(taps.KeyUp(Key.RightCtrl));
    }
}
