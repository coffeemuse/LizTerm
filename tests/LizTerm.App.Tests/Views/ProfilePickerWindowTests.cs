// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ProfilePickerWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-picker-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>The picker's Connect button is IsDefault, so without the box consuming Enter, typing a host and
    /// pressing Enter would connect the SELECTED PROFILE instead — a different host entirely. Handled at the box
    /// means the window-level default button never sees the key, the same shape SessionWindow.OnFindBoxKeyDown
    /// uses to keep the find bar's typing off the wire (spec 7.3).</summary>
    [AvaloniaFact]
    public void Enter_in_the_quick_connect_box_connects_the_typed_host_not_the_selected_profile()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "saved", Host = "saved.host", Port = 23 });
        SessionProfile? opened = null;
        var window = new ProfilePickerWindow(store, (p, _) => opened = p, () => { });
        window.Show();

        var vm = (ProfilePickerViewModel)window.DataContext!;
        vm.SelectedProfile = vm.Profiles.Single();
        Assert.True(vm.ConnectCommand.CanExecute(null));

        var box = window.FindControl<TextBox>("QuickConnectBox")!;
        vm.QuickConnectText = "other.example:3270";
        box.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.NotNull(opened);
        Assert.Equal("other.example", opened.Host);
        Assert.Equal(3270, opened.Port);
    }

    /// <summary>The row used to be a horizontal StackPanel holding a Width="300" box, which overflowed below about
    /// 460px — and the window's own MinWidth is 400, so a user could reach it just by dragging the corner in.
    /// Measured at MinWidth, where the row is 368px wide.</summary>
    [AvaloniaFact]
    public void The_quick_connect_row_fits_at_the_windows_minimum_width()
    {
        var store = new ProfileStore(_dir);
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Width = window.MinWidth;
        window.Show();

        var box = window.FindControl<TextBox>("QuickConnectBox")!;
        var button = window.FindControl<Button>("QuickConnectButton")!;
        var row = (Panel)box.Parent!;

        // Measured against the row's own width, so the assertion is about the layout rather than about which
        // panel type holds it.
        Assert.True(box.Bounds.Width > 0, "the quick connect box collapsed to nothing");
        Assert.True(button.Bounds.Right <= row.Bounds.Width + 0.5,
            $"the Quick Connect button ends at {button.Bounds.Right} in a {row.Bounds.Width} row");
        Assert.True(box.Bounds.Right <= button.Bounds.Left + 0.5,
            $"the box ends at {box.Bounds.Right} and the button starts at {button.Bounds.Left}");
    }
}
