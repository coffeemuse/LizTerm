// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class MvsmfViewerWindowTests
{
    private static (MvsmfViewerWindow Window, MvsmfViewerViewModel Vm) Show(params string[] lines)
    {
        var vm = new MvsmfViewerViewModel("MVSCE02.CNTL(HELLO)", lines.Length > 0 ? lines : ["//HELLO JOB"],
            trimTrailingBlanks: true);
        var window = new MvsmfViewerWindow { DataContext = vm };
        window.Show();
        window.Activate();
        window.UpdateLayout();
        return (window, vm);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    [AvaloniaFact]
    public void It_shows_the_text_the_numbers_and_the_footer()
    {
        var (window, vm) = Show("//HELLO JOB", "//STEP EXEC PGM=IEFBR14");

        Assert.Equal("MVSCE02.CNTL(HELLO) — mvsMF Access", window.Title);
        Assert.Equal(vm.Text, Named<TextBox>(window, "TextArea").Text);
        Assert.True(Named<TextBox>(window, "TextArea").IsReadOnly);
        Assert.Equal("1\n2", Named<TextBlock>(window, "Gutter").Text);
        Assert.Equal("2 lines · trailing blanks trimmed", Named<TextBlock>(window, "FooterLine").Text);
    }

    [AvaloniaFact]
    public void The_toggle_hides_the_gutter()
    {
        var (window, vm) = Show();
        Assert.True(Named<ScrollViewer>(window, "GutterScroller").IsVisible);

        vm.ShowLineNumbers = false;
        window.UpdateLayout();

        Assert.False(Named<ScrollViewer>(window, "GutterScroller").IsVisible);
    }

    [AvaloniaFact]
    public void A_match_is_selected_in_the_text()
    {
        var (window, vm) = Show("//HELLO JOB", "//STEP EXEC PGM=IEFBR14", "//SYSIN DD *", "hello again");

        vm.Term = "hello";
        window.UpdateLayout();

        var text = Named<TextBox>(window, "TextArea");
        Assert.Equal(2, text.SelectionStart);
        Assert.Equal(7, text.SelectionEnd);

        vm.FindNextCommand.Execute(null);
        window.UpdateLayout();
        Assert.Equal(vm.MatchStart, text.SelectionStart);
        Assert.Equal(vm.MatchStart + 5, text.SelectionEnd);
    }

    [AvaloniaFact]
    public void Enter_and_shift_enter_step_the_matches()
    {
        var (window, vm) = Show("//HELLO JOB", "hello again");
        vm.Term = "hello";

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("2 of 2", vm.CountText);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Assert.Equal("1 of 2", vm.CountText);
    }

    /// <summary>With no matches, Enter has nothing to step to and must not be marked handled — harmless today, but
    /// it would otherwise quietly swallow a future default button's Enter. The bubble-phase handler below, added
    /// with handledEventsToo, sees the flag as OnKeyDownTunnel's own Tunnel-phase handler left it.</summary>
    [AvaloniaFact]
    public void Enter_is_left_unhandled_when_there_are_no_matches()
    {
        var (window, _) = Show("//HELLO JOB");
        bool? handled = null;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) handled = e.Handled;
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(handled);
    }

    [AvaloniaFact]
    public void Escape_closes_it()
    {
        var (window, _) = Show();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void A_new_view_model_replaces_the_text_and_the_title()
    {
        var (window, _) = Show("//HELLO JOB");

        window.DataContext = new MvsmfViewerViewModel("MVSCE02.CNTL(ALLOC)", ["//ALLOC JOB"], trimTrailingBlanks: true);
        window.UpdateLayout();

        Assert.Equal("MVSCE02.CNTL(ALLOC) — mvsMF Access", window.Title);
        Assert.Equal("//ALLOC JOB", Named<TextBox>(window, "TextArea").Text);
    }

    [AvaloniaFact]
    public void The_line_number_choice_survives_a_second_view()
    {
        var (window, vm) = Show("//HELLO JOB");
        vm.ShowLineNumbers = false;

        var second = new MvsmfViewerViewModel("MVSCE02.CNTL(ALLOC)", ["//ALLOC JOB"], trimTrailingBlanks: true);
        window.DataContext = second;
        window.UpdateLayout();

        Assert.False(second.ShowLineNumbers);
        Assert.False(Named<ScrollViewer>(window, "GutterScroller").IsVisible);
    }
}
