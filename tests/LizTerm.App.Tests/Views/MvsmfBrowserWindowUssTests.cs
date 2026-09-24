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
using LizTerm.Core.HostFiles;

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

    /// <summary>Selecting the USS tab while the window's own opening dataset listing is still running is a no-op
    /// in UssBrowserViewModel.EnsureListedAsync (it defers to the busy runner), so the window must list the USS
    /// tab's start path itself once that listing ends, without the user pressing Go or switching tabs away and
    /// back (fix for the gap the task-6 review found).</summary>
    [AvaloniaFact]
    public async Task The_uss_tab_lists_its_start_path_once_the_busy_dataset_listing_ends_even_if_selected_first()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        t.Host.Gate = new TaskCompletionSource();
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        await Wait.UntilAsync(() => t.Vm.IsBusy, "the dataset listing to start");
        Assert.Empty(t.Vm.Datasets);

        Named<TabControl>(window, "Tabs").SelectedIndex = 1;
        window.UpdateLayout();
        Assert.True(window.IsUssTab);
        // The tab's own EnsureListedAsync is a no-op while the runner is still busy with the dataset listing.
        Assert.Null(t.Vm.Uss.Current);

        t.Host.Gate.SetResult();
        await Wait.UntilAsync(() => t.Vm.Uss.Current is not null && !t.Vm.IsBusy, "the USS start listing");

        Assert.Equal("/u/mvsce02", t.Vm.Uss.Current!.UnixPath);
        Assert.Empty(t.Vm.Uss.Directories);
        Assert.Empty(t.Vm.Uss.Files);
        Assert.Equal("✓ Listed /u/mvsce02 · 0 directories, 0 files.", t.Vm.StatusText);
    }

    private static int StartListings(BrowserTestHost t) => t.Host.CallsSnapshot().Count(c => c == "listdir:/u/mvsce02");

    /// <summary>The start listing waits for the opening listing, but not past the user's Cancel of it: the tab stays
    /// unlisted with the Cancel's line until it is shown again.</summary>
    [AvaloniaFact]
    public async Task A_cancelled_opening_listing_does_not_start_the_uss_start_listing()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        t.Host.Gate = new TaskCompletionSource();
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        try
        {
            await Wait.UntilAsync(() => t.Vm.IsBusy, "the dataset listing to start");
            Named<TabControl>(window, "Tabs").SelectedIndex = 1;
            window.UpdateLayout();

            t.Vm.CancelCommand.Execute(null);
            await Wait.UntilAsync(() => !t.Vm.IsBusy, "the cancel");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, StartListings(t));
            Assert.Null(t.Vm.Uss.Current);
            Assert.Equal("– Cancelled.", t.Vm.StatusText);

            t.Host.Gate.SetResult();
            Named<TabControl>(window, "Tabs").SelectedIndex = 0;
            Named<TabControl>(window, "Tabs").SelectedIndex = 1;
            await Wait.UntilAsync(() => t.Vm.Uss.Current is not null && !t.Vm.IsBusy, "the USS start listing");
            Assert.Equal(1, StartListings(t));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>An opening listing that fails into the banner keeps its Retry: the start listing does not run over
    /// it. Once the Retry lands cleanly, the tab lists.</summary>
    [AvaloniaFact]
    public async Task A_failed_opening_listing_keeps_its_banner_and_the_uss_tab_lists_after_its_retry()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        t.Host.Gate = new TaskCompletionSource();
        t.Host.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host.");
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        try
        {
            await Wait.UntilAsync(() => t.Vm.IsBusy, "the dataset listing to start");
            Named<TabControl>(window, "Tabs").SelectedIndex = 1;
            window.UpdateLayout();

            t.Host.Gate.SetResult();
            await Wait.UntilAsync(() => !t.Vm.IsBusy, "the failure");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, StartListings(t));
            Assert.True(t.Vm.CanRetry);
            Assert.Equal("cannot reach the host.", t.Vm.ErrorText);

            t.Host.Failures.Clear();
            await t.Vm.RetryCommand.ExecuteAsync(null);
            await Wait.UntilAsync(() => t.Vm.Uss.Current is not null && !t.Vm.IsBusy, "the USS start listing");
            Assert.Equal(4, t.Vm.Datasets.Count);
            Assert.Equal(1, StartListings(t));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Escape's ladder skips an upload review on the hidden Datasets tab: from the USS tab it closes the
    /// window, rather than a review the user cannot see.</summary>
    [AvaloniaFact]
    public async Task Escape_on_the_uss_tab_closes_the_window_not_a_hidden_upload_review()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            UssTestHost.Standard(host);
        });
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first dataset listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var folder = Directory.CreateTempSubdirectory("lizterm-window-upload-").FullName;
        try
        {
            var file = Path.Combine(folder, "newmem.jcl");
            await File.WriteAllTextAsync(file, "//NEWMEM JOB\n", TestContext.Current.CancellationToken);
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);
            Assert.True(t.Vm.IsReviewingUpload);
            Named<TabControl>(window, "Tabs").SelectedIndex = 1;
            window.UpdateLayout();
            await Wait.UntilAsync(() => t.Vm.Uss.Current is not null && !t.Vm.IsBusy, "the USS start listing");

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.False(window.IsVisible);
            Assert.True(t.Host.Disposed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>A host without /u/&lt;userid&gt; (USS spec §3) fails the start listing. First show means attempted,
    /// not succeeded: the failure must not start the next attempt when the busy flag falls. The gate is released
    /// one listing at a time, so each failure lands asynchronously, as it does over a network.</summary>
    [AvaloniaFact]
    public async Task A_missing_start_path_is_listed_once_and_its_reason_stays()
    {
        var t = BrowserTestHost.Create(seed: BrowserTestHost.Standard);
        t.Host.Gate = new TaskCompletionSource();
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        try
        {
            await Wait.UntilAsync(() => t.Vm.IsBusy, "the dataset listing to start");
            Named<TabControl>(window, "Tabs").SelectedIndex = 1;
            window.UpdateLayout();

            for (var round = 0; round < 4; round++)
            {
                var gate = t.Host.Gate!;
                t.Host.Gate = new TaskCompletionSource();
                var before = StartListings(t);
                gate.SetResult();
                await Wait.UntilAsync(() => StartListings(t) > before || (!t.Vm.IsBusy && t.Vm.StatusText.StartsWith('✗')),
                    "the next listing or the failure");
                await Task.Delay(50, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(1, StartListings(t));
            Assert.False(t.Vm.IsBusy);
            Assert.Equal("✗ File not found: /u/mvsce02", Named<TextBlock>(window, "StatusLine").Text);
            Assert.True(Named<Button>(window, "GoButton").IsEffectivelyEnabled);
            Assert.True(Named<TextBox>(window, "PathBox").IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The same, with the failure synchronous: it must not recurse inside the runner's own finally.</summary>
    [AvaloniaFact]
    public async Task A_missing_start_path_that_fails_at_once_is_listed_once()
    {
        var t = BrowserTestHost.Create(seed: BrowserTestHost.Standard);
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4 && !t.Vm.IsBusy, "the first dataset listing");

        Named<TabControl>(window, "Tabs").SelectedIndex = 1;
        window.UpdateLayout();
        await Wait.UntilAsync(() => StartListings(t) > 0 && !t.Vm.IsBusy, "the start listing");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, StartListings(t));
        Assert.Equal("✗ File not found: /u/mvsce02", t.Vm.StatusText);
        Assert.True(Named<Button>(window, "GoButton").IsEffectivelyEnabled);
    }

    /// <summary>While the tab is hidden its panel loses its context and the file list its selection; that must not
    /// reach the view model, or a Retry or a verb after the round trip acts on nothing.</summary>
    [AvaloniaFact]
    public async Task The_file_selection_survives_a_round_trip_to_the_datasets_tab()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser/notes");
        window.UpdateLayout();
        var list = Named<ListBox>(window, "FileList");
        list.SelectedIndex = 0;
        await Wait.UntilAsync(() => t.Vm.Uss.SelectedFiles.Count == 1, "the pushed selection");
        var selected = t.Vm.Uss.SelectedFiles[0];

        var tabs = Named<TabControl>(window, "Tabs");
        tabs.SelectedIndex = 0;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(selected, Assert.Single(t.Vm.Uss.SelectedFiles));

        tabs.SelectedIndex = 1;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(selected, Assert.Single(t.Vm.Uss.SelectedFiles));
        Assert.Same(selected, Assert.Single(list.SelectedItems!.OfType<FileRow>()));
        Assert.Equal("3 files · 1 selected", Named<BrowserPane>(window, "FilesPane").FooterText);
        Assert.True(Named<Button>(window, "UssDownloadButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task The_directory_and_file_headings_sit_over_their_columns()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser/notes");

        MvsmfBrowserWindowTests.AssertHeaderSpansRows(window, "DirectoriesHeader", "DirectoryList");
        MvsmfBrowserWindowTests.AssertHeaderSpansRows(window, "FilesHeader", "FileList");
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

    /// <summary>The strip is the window's, not the tab's: a USS question stays up over the Datasets tab and is
    /// answered there.</summary>
    [AvaloniaFact]
    public async Task A_uss_question_is_answered_in_the_shared_strip_from_the_datasets_tab()
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

        Named<TabControl>(window, "Tabs").SelectedIndex = 0;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsUssTab);
        Assert.True(Named<Border>(window, "ConfirmationStrip").IsEffectivelyVisible);
        Assert.Equal("Delete README.txt from /u/ibmuser/notes? This cannot be undone.", t.Vm.Confirmation!.Message);

        var primary = Named<Button>(window, "ConfirmPrimaryButton");
        primary.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Wait.UntilAsync(() => !t.Vm.IsBusy && !t.Vm.HasConfirmation, "the delete");

        Assert.Contains("delete:/u/ibmuser/notes/README.txt", t.Host.CallsSnapshot());
        Assert.DoesNotContain(t.Vm.Uss.Files, f => f.Name == "README.txt");
        Assert.Equal("✓ Deleted 1 of 1 file.", Named<TextBlock>(window, "StatusLine").Text);
    }

    [AvaloniaFact]
    public async Task The_uss_context_menus_bind_the_same_commands_and_gestures_as_the_toolbars()
    {
        var (window, t) = await UssAsync();
        await ListAsync(t, "/u/ibmuser/notes");
        window.UpdateLayout();
        var uss = t.Vm.Uss;

        var directoryList = Named<ListBox>(window, "DirectoryList");
        var directoryMenu = directoryList.ContextMenu!;
        directoryMenu.Open(directoryList);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var items = directoryMenu.Items.OfType<MenuItem>().ToDictionary(i => i.Name!);
            Assert.Same(uss.OpenDirectoryCommand, items["DirectoryMenuOpen"].Command);
            Assert.Same(uss.NewDirectoryCommand, items["DirectoryMenuNew"].Command);
            Assert.Same(uss.DeleteDirectoryCommand, items["DirectoryMenuDelete"].Command);
            Assert.Same(uss.RefreshCommand, items["DirectoryMenuRefresh"].Command);
            Assert.Same(uss.NewDirectoryCommand, Named<Button>(window, "NewDirectoryButton").Command);
            Assert.Same(uss.DeleteDirectoryCommand, Named<Button>(window, "DeleteDirectoryButton").Command);
            Assert.Same(uss.RefreshCommand, Named<Button>(window, "UssRefreshButton").Command);
            Assert.Equal(new KeyGesture(Key.Enter), items["DirectoryMenuOpen"].InputGesture);
            Assert.Equal(window.NewDatasetGesture, items["DirectoryMenuNew"].InputGesture);
            Assert.Equal(new KeyGesture(Key.Delete), items["DirectoryMenuDelete"].InputGesture);
            Assert.Equal(window.RefreshGesture, items["DirectoryMenuRefresh"].InputGesture);
            // The toolbar names the same gestures in its tooltips.
            Assert.Contains(window.NewDatasetGesture.ToString("p", null), (string)ToolTip.GetTip(Named<Button>(window, "NewDirectoryButton"))!);
            Assert.Contains("(Delete)", (string)ToolTip.GetTip(Named<Button>(window, "DeleteDirectoryButton"))!);
            Assert.Contains(window.RefreshGesture.ToString("p", null), (string)ToolTip.GetTip(Named<Button>(window, "UssRefreshButton"))!);
        }
        finally
        {
            directoryMenu.Close();
        }

        var fileList = Named<ListBox>(window, "FileList");
        var fileMenu = fileList.ContextMenu!;
        fileMenu.Open(fileList);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var items = fileMenu.Items.OfType<MenuItem>().ToDictionary(i => i.Name!);
            Assert.Same(uss.ViewCommand, items["FileMenuView"].Command);
            Assert.Same(uss.DownloadCommand, items["FileMenuDownload"].Command);
            Assert.Same(uss.UploadCommand, items["FileMenuUpload"].Command);
            Assert.Same(uss.DeleteFilesCommand, items["FileMenuDelete"].Command);
            Assert.Same(uss.ViewCommand, Named<Button>(window, "UssViewButton").Command);
            Assert.Same(uss.DownloadCommand, Named<Button>(window, "UssDownloadButton").Command);
            Assert.Same(uss.UploadCommand, Named<Button>(window, "UssUploadButton").Command);
            Assert.Same(uss.DeleteFilesCommand, Named<Button>(window, "DeleteFilesButton").Command);
            Assert.Equal(window.ViewGesture, items["FileMenuView"].InputGesture);
            Assert.Equal(new KeyGesture(Key.Enter), items["FileMenuDownload"].InputGesture);
            Assert.Equal(new KeyGesture(Key.Delete), items["FileMenuDelete"].InputGesture);
            Assert.Contains(window.ViewGesture.ToString("p", null), (string)ToolTip.GetTip(Named<Button>(window, "UssViewButton"))!);
            Assert.Contains("(Delete)", (string)ToolTip.GetTip(Named<Button>(window, "DeleteFilesButton"))!);
        }
        finally
        {
            fileMenu.Close();
        }
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
