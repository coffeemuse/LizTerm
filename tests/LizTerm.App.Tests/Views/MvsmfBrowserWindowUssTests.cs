// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Tests.ViewModels;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class MvsmfBrowserWindowUssTests
{
    /// <summary>The window over a host seeded with the standard datasets and the standard UNIX tree, the USS tab in
    /// front and its start path (/u/mvsce02, the profile's userid) listed.</summary>
    private static async Task<(MvsmfBrowserWindow Window, BrowserTestHost T)> UssAsync()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        window.Activate();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first dataset listing");
        Named<TabControl>(window, "Tabs").SelectedIndex = 1;
        window.UpdateLayout();
        await Wait.UntilAsync(() => t.Vm.Uss.Current is not null && !t.Vm.IsBusy, "the USS start listing");
        return (window, t);
    }

    private static async Task ListAsync(BrowserTestHost t, string path)
    {
        t.Vm.Uss.Path = path;
        await t.Vm.Uss.GoCommand.ExecuteAsync(null);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    private static RawInputModifiers Command(MvsmfBrowserWindow window) =>
        window.RefreshGesture.KeyModifiers == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public async Task The_uss_tab_lists_the_start_path_when_first_shown_and_only_then()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first dataset listing");
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("listdir:", StringComparison.Ordinal));
        Assert.False(window.IsUssTab);

        Named<TabControl>(window, "Tabs").SelectedIndex = 1;
        window.UpdateLayout();
        await Wait.UntilAsync(() => t.Vm.Uss.Current is not null, "the USS start listing");

        Assert.True(window.IsUssTab);
        Assert.Equal("/u/mvsce02", Named<TextBox>(window, "PathBox").Text);
        Assert.Equal("/u/mvsce02", Named<BrowserPane>(window, "FilesPane").Title);
        Assert.Equal("Directories", Named<BrowserPane>(window, "DirectoriesPane").Title);
        Assert.Equal("No directories", Named<BrowserPane>(window, "DirectoriesPane").FooterText);
        Assert.Equal("✓ Listed /u/mvsce02 · 0 directories, 0 files.", Named<TextBlock>(window, "StatusLine").Text);
    }

    [AvaloniaFact]
    public async Task Enter_in_the_path_box_lists_and_the_lists_fill()
    {
        var (window, t) = await UssAsync();
        var box = Named<TextBox>(window, "PathBox");
        box.Text = "/u/ibmuser/notes";
        box.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.UntilAsync(() => t.Vm.Uss.Current?.UnixPath == "/u/ibmuser/notes" && !t.Vm.IsBusy, "the listing");
        window.UpdateLayout();

        Assert.Equal(1, Named<ListBox>(window, "DirectoryList").ItemCount);
        Assert.Equal(3, Named<ListBox>(window, "FileList").ItemCount);
        Assert.Equal("/u/ibmuser/notes", Named<BrowserPane>(window, "FilesPane").Title);
        Assert.Equal("3 files · none selected", Named<BrowserPane>(window, "FilesPane").FooterText);
    }

    [AvaloniaFact]
    public async Task Enter_on_a_directory_row_descends_and_up_climbs()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser");
        window.UpdateLayout();
        var list = Named<ListBox>(window, "DirectoryList");
        list.SelectedIndex = 0;
        list.ContainerFromIndex(0)!.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.UntilAsync(() => t.Vm.Uss.Current?.UnixPath == "/u/ibmuser/notes" && !t.Vm.IsBusy, "the descent");

        Named<Button>(window, "UpButton").Command!.Execute(null);
        await Wait.UntilAsync(() => t.Vm.Uss.Current?.UnixPath == "/u/ibmuser" && !t.Vm.IsBusy, "the climb");
        Assert.Equal("/u/ibmuser", Named<TextBox>(window, "PathBox").Text);
    }

    [AvaloniaFact]
    public async Task The_command_r_gesture_refreshes_the_uss_listing_not_the_datasets()
    {
        var (window, t) = await UssAsync();
        var lists = t.Host.CallsSnapshot().Count(c => c.StartsWith("list:", StringComparison.Ordinal));
        var listdirs = t.Host.CallsSnapshot().Count(c => c.StartsWith("listdir:", StringComparison.Ordinal));

        window.KeyPress(Key.R, Command(window), PhysicalKey.R, null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("listdir:", StringComparison.Ordinal)) == listdirs + 1 && !t.Vm.IsBusy, "the refresh");

        Assert.Equal(lists, t.Host.CallsSnapshot().Count(c => c.StartsWith("list:", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task Delete_on_the_file_list_asks_in_the_strip_and_escape_cancels()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser/notes");
        window.UpdateLayout();
        var list = Named<ListBox>(window, "FileList");
        list.SelectedIndex = 0;
        list.ContainerFromIndex(0)!.Focus();
        await Wait.UntilAsync(() => t.Vm.Uss.SelectedFiles.Count == 1, "the pushed selection");

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");

        Assert.True(Named<Border>(window, "ConfirmationStrip").IsVisible);
        Assert.Equal("Delete README.txt from /u/ibmuser/notes? This cannot be undone.", t.Vm.Confirmation!.Message);
        Assert.Equal("Delete 1 file", Named<Button>(window, "ConfirmPrimaryButton").Content);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the cancel");
        Assert.Equal("– Delete cancelled.", t.Vm.StatusText);
        Assert.Equal(3, t.Vm.Uss.Files.Count);
    }

    [AvaloniaFact]
    public async Task A_uss_result_reaches_the_status_line_while_the_datasets_tab_is_in_front()
    {
        var (window, t) = await UssAsync();
        Named<TabControl>(window, "Tabs").SelectedIndex = 0;
        window.UpdateLayout();
        Assert.False(window.IsUssTab);

        await ListAsync(t, "/u/ibmuser");

        Assert.Equal("✓ Listed /u/ibmuser · 2 directories, 0 files.", Named<TextBlock>(window, "StatusLine").Text);
        Assert.True(Named<Button>(window, "ListButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task The_view_gesture_on_a_file_opens_the_shared_viewer()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser/notes");
        window.UpdateLayout();
        var list = Named<ListBox>(window, "FileList");
        list.SelectedIndex = 0;
        list.ContainerFromIndex(0)!.Focus();
        await Wait.UntilAsync(() => t.Vm.Uss.SelectedFiles.Count == 1, "the pushed selection");
        var button = Named<Button>(window, "UssViewButton");
        Assert.True(ToolTip.GetShowOnDisabled(button));
        Assert.Contains(window.ViewGesture.ToString("p", null), (string)ToolTip.GetTip(button)!);

        window.KeyPress(Key.Enter, Command(window), PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        Assert.Equal("/u/ibmuser/notes/README.txt — mvsMF Access", window.ViewerWindow!.Title);
    }

    // NOTE: unlike the brief's literal snippet, this opens the drop-down's flyout before reading or setting a
    // MenuItem's IsChecked. A MenuFlyout's content is not part of the window's logical tree until it is shown
    // (confirmed by instrumenting this test: DataContext, Parent and LogicalParent were all null before ShowAt),
    // so a binding on an unopened item never resolves and IsChecked stays its unbound default of false — the same
    // reason MvsmfBrowserWindowTests.The_transfer_menu_binds_the_mode_and_the_upload_options opens the Datasets
    // tab's TransferButton flyout before asserting on its items rather than reading them by name.
    [AvaloniaFact]
    public async Task The_transfer_drop_down_holds_text_binary_and_verify_only()
    {
        var (window, t) = await UssAsync();
        var transfer = Named<DropDownButton>(window, "UssTransferButton");
        var flyout = (MenuFlyout)transfer.Flyout!;
        flyout.ShowAt(transfer);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items.OfType<MenuItem>().ToDictionary(i => i.Name!);

        Assert.True(items["UssTextModeItem"].IsChecked);
        Assert.True(items["UssVerifyItem"].IsChecked);
        Assert.False(items.ContainsKey("UssTrimItem"));
        Assert.Null(window.FindControl<CheckBox>("UssExpandTabsBox"));

        items["UssBinaryModeItem"].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.HostFiles.HostTransferMode.Binary, t.Vm.Uss.Mode);
        Assert.Equal("Transfer: Binary", (string)transfer.Content!);
        items["UssVerifyItem"].IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(t.Vm.Uss.VerifyUploads);
    }

    [AvaloniaFact]
    public async Task The_strip_input_takes_a_directory_name_and_creates_it()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser");
        window.UpdateLayout();

        window.KeyPress(Key.N, Command(window), PhysicalKey.N, null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the name question");
        Assert.Equal(Core.HostFiles.HostPath.MaxUnixPathLength, Named<TextBox>(window, "ConfirmInputBox").MaxLength);
        Named<TextBox>(window, "ConfirmInputBox").Text = "drafts2";
        Named<TextBox>(window, "ConfirmInputBox").Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.UntilAsync(() => !t.Vm.IsBusy && !t.Vm.HasConfirmation, "the create");

        Assert.Contains("mkdir:/u/ibmuser/drafts2", t.Host.CallsSnapshot());
        Assert.Equal("drafts2", t.Vm.Uss.SelectedDirectory!.Name);
        Assert.Equal("✓ Created /u/ibmuser/drafts2.", t.Vm.StatusText);
    }
}
