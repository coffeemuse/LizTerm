// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Tests.ViewModels;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Views;

public class MvsmfBrowserWindowTests
{
    private static (MvsmfBrowserWindow Window, BrowserTestHost T) Show(string? userid = "MVSCE02",
        Action<FakeHostFileService>? seed = null, int pageSize = 500)
    {
        var t = BrowserTestHost.Create(userid, seed, pageSize);
        var window = new MvsmfBrowserWindow { DataContext = t.Vm };
        window.Show();
        window.Activate();
        return (window, t);
    }

    private static T Named<T>(Window window, string name) where T : Control => window.FindControl<T>(name)!;

    private static async Task<(MvsmfBrowserWindow Window, BrowserTestHost T)> ViewableAsync()
    {
        var (window, t) = Show(seed: host =>
        {
            BrowserTestHost.Standard(host);
            host.AddDataset("MVSCE02.NOTES", dsorg: "PS", recfm: "FB", lrecl: 80, blksize: 3120);
        });
        t.Host.Text["MVSCE02.CNTL(HELLO)"] = ["//HELLO JOB"];
        t.Host.Text["MVSCE02.CNTL(ALLOC)"] = ["//ALLOC JOB"];
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 5, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        return (window, t);
    }

    [AvaloniaFact]
    public async Task The_view_button_explains_itself_even_when_it_is_off()
    {
        var (window, t) = await ViewableAsync();
        var button = Named<Button>(window, "ViewButton");

        Assert.Equal(t.Vm.ViewHint, ToolTip.GetTip(button));
        Assert.True(ToolTip.GetShowOnDisabled(button));
        Assert.Contains(window.ViewGesture.ToString("p", null), t.Vm.ViewHint);

        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.False(button.IsEffectivelyEnabled);
        Assert.Equal("MVSCE02.LOAD holds undefined-length records, which are not text.", t.Vm.ViewHint);
    }

    [AvaloniaFact]
    public async Task The_command_gesture_views_and_plain_enter_still_downloads()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        window.UpdateLayout();
        Named<ListBox>(window, "MemberList").ContainerFromIndex(0)!.Focus();
        var modifiers = window.ViewGesture.KeyModifiers == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

        window.KeyPress(Key.Enter, modifiers, PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        Assert.Equal("MVSCE02.CNTL(HELLO) — mvsMF Access", window.ViewerWindow!.Title);
        Assert.DoesNotContain(t.Picker.Calls, c => c.StartsWith("save:"));
    }

    /// <summary>A sequential dataset has no Members pane, so MemberList cannot hold the focus and the gesture has
    /// to be taken from the dataset list instead — the View button's own tooltip offers it there.</summary>
    [AvaloniaFact]
    public async Task The_command_gesture_views_a_sequential_dataset_from_the_dataset_list()
    {
        var (window, t) = await ViewableAsync();
        t.Host.Text["MVSCE02.NOTES"] = ["Notes on the batch run."];
        await t.ChooseAsync("MVSCE02.NOTES");
        window.UpdateLayout();
        Assert.False(Named<DockPanel>(window, "MemberPane").IsVisible);
        var datasets = Named<ListBox>(window, "DatasetList");
        datasets.ContainerFromItem(t.Vm.SelectedDataset!)!.Focus();
        var modifiers = window.ViewGesture.KeyModifiers == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

        window.KeyPress(Key.Enter, modifiers, PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        Assert.Equal("MVSCE02.NOTES — mvsMF Access", window.ViewerWindow!.Title);
    }

    [AvaloniaFact]
    public async Task Plain_enter_in_the_member_list_still_downloads()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        window.UpdateLayout();
        Named<ListBox>(window, "MemberList").ContainerFromIndex(0)!.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => t.Picker.Calls.Contains("save:HELLO.txt"), "the save dialog");
        Assert.Null(window.ViewerWindow);
    }

    [AvaloniaFact]
    public async Task A_second_view_reuses_the_one_window()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        await t.Vm.ViewCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        var first = window.ViewerWindow!;

        t.Select("ALLOC");
        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Same(first, window.ViewerWindow);
        Assert.Equal("MVSCE02.CNTL(ALLOC) — mvsMF Access", window.ViewerWindow!.Title);
    }

    [AvaloniaFact]
    public async Task The_viewer_closes_with_the_browser_window()
    {
        var (window, t) = await ViewableAsync();
        t.Select("HELLO");
        await t.Vm.ViewCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => window.ViewerWindow is { IsVisible: true }, "the viewer window");
        var viewer = window.ViewerWindow!;

        window.Close();

        Assert.False(viewer.IsVisible);
    }

    [AvaloniaFact]
    public async Task The_sequential_note_names_every_verb_that_acts_on_the_dataset()
    {
        var (window, t) = await ViewableAsync();

        await t.ChooseAsync("MVSCE02.NOTES");

        Assert.Equal("Sequential dataset: View, Download and Upload act on the dataset itself.",
            Named<TextBlock>(window, "SequentialNote").Text);
    }

    [AvaloniaFact]
    public async Task Opens_with_the_preview_strip_and_lists_the_users_datasets()
    {
        var (window, t) = Show();

        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        Assert.Equal("mvsMF Access — MVS/CE (Preview)", window.Title);
        Assert.True(Named<Border>(window, "PreviewStrip").IsVisible);
        Assert.StartsWith("⚠ Feature preview.", Named<Border>(window, "PreviewStrip").GetLogicalDescendants().OfType<TextBlock>().First().Text);
        Assert.Equal("MVSCE02.**", Named<TextBox>(window, "FilterBox").Text);
        Assert.Equal(4, Named<ListBox>(window, "DatasetList").ItemCount);
        Assert.True(Named<TextBlock>(window, "ChooseHint").IsVisible);
        Assert.True(window.CanResize);
    }

    [AvaloniaFact]
    public async Task Load_more_buttons_show_only_while_the_host_has_more()
    {
        var (window, t) = Show(seed: BrowserTestHost.Large, pageSize: 2);
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 2, "the first page");
        var moreDatasets = Named<Button>(window, "LoadMoreDatasetsButton");
        var moreMembers = Named<Button>(window, "LoadMoreMembersButton");
        Assert.True(moreDatasets.IsVisible);
        Assert.Equal("Load more", moreDatasets.Content);

        await t.ChooseAsync("MVSCE02.BIG");
        Assert.True(moreMembers.IsVisible);
        Assert.Equal("Load more", moreMembers.Content);

        moreMembers.Command!.Execute(null);
        await Wait.UntilAsync(() => t.Vm.Members.Count == 4, "the second page of members");
        await t.LoadMoreMembersAsync();
        Assert.False(moreMembers.IsVisible);

        while (t.Vm.HasMoreDatasets) await t.LoadMoreDatasetsAsync();
        Assert.False(moreDatasets.IsVisible);
        Assert.Equal(9, Named<ListBox>(window, "DatasetList").ItemCount);
    }

    /// <summary>The strip reads as a caution banner: dark text, the guide link included, on yellow.</summary>
    [AvaloniaFact]
    public void The_preview_strip_is_dark_on_yellow()
    {
        var (window, _) = Show(userid: null);
        var strip = Named<Border>(window, "PreviewStrip");

        Assert.Equal(Color.Parse("#F5C542"), ((ISolidColorBrush)strip.Background!).Color);
        Assert.All(strip.GetLogicalDescendants().OfType<TextBlock>(),
            text => Assert.Equal(Color.Parse("#1A1A1A"), ((ISolidColorBrush)text.Foreground!).Color));
        Assert.Equal(Color.Parse("#1A1A1A"), ((ISolidColorBrush)Named<Button>(window, "GuideLink").Foreground!).Color);
    }

    [AvaloniaFact]
    public void Without_a_userid_nothing_is_listed_on_open()
    {
        var (_, t) = Show(userid: null);
        Assert.Empty(t.Host.CallsSnapshot());
    }

    [AvaloniaFact]
    public void The_column_headers_are_ispf_style_capitals()
    {
        var (window, _) = Show(userid: null);
        var headers = window.GetLogicalDescendants().OfType<TextBlock>().Select(b => b.Text).ToHashSet();
        foreach (var header in new[] { "NAME", "DSORG", "RECFM", "LRECL", "MEMBER", "STATUS" })
            Assert.Contains(header, headers);
    }

    [AvaloniaFact]
    public async Task Choosing_a_pds_shows_its_members_and_selection_reaches_the_view_model()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        Named<ListBox>(window, "DatasetList").SelectedIndex = 0;
        await Wait.UntilAsync(() => t.Vm.Members.Count == 3, "the members");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[0]);
        members.SelectedItems.Add(t.Vm.VisibleMembers[2]);

        Assert.True(Named<Control>(window, "MemberPane").IsVisible);
        Assert.Equal("MVSCE02.CNTL", Named<BrowserPane>(window, "MembersPane").Title);
        Assert.Equal("3 members · 2 selected", Named<BrowserPane>(window, "MembersPane").FooterText);
        Assert.Equal(new[] { "ALLOC", "HELLO" }, t.Vm.SelectedMembers.Select(m => m.Name));
        Assert.True(Named<Button>(window, "DownloadButton").IsEffectivelyEnabled);
        Assert.True(Named<Button>(window, "DeleteButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task A_question_shows_in_the_strip_with_its_own_labels()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        t.Select("HELLO");

        _ = t.Vm.DeleteCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");

        Assert.True(Named<Border>(window, "ConfirmationStrip").IsVisible);
        Assert.Equal("Delete 1 member", Named<Button>(window, "ConfirmPrimaryButton").Content);
        Assert.False(Named<Button>(window, "ConfirmSecondaryButton").IsVisible);
        Assert.False(Named<CheckBox>(window, "ApplyToAllBox").IsVisible);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Wait.UntilAsync(() => !t.Vm.HasConfirmation, "the question to be cancelled");
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task The_error_banner_shows_with_a_mark_and_retry()
    {
        var (window, t) = Show(userid: null);
        t.Vm.Filter = "MVSCE02.**";
        t.Host.Failures["list:MVSCE02.**"] = new LizTerm.Core.HostFiles.HostFileException(LizTerm.Core.HostFiles.HostFileErrorKind.Unreachable, "Dataset list: cannot reach the host (refused).");

        await t.Vm.ListCommand.ExecuteAsync(null);

        Assert.True(Named<Border>(window, "ErrorBanner").IsVisible);
        Assert.Contains(window.FindControl<Border>("ErrorBanner")!.GetLogicalDescendants().OfType<TextBlock>(),
            b => b.Text == "✗ Dataset list: cannot reach the host (refused).");
        Assert.True(Named<Button>(window, "RetryButton").IsVisible);
    }

    [AvaloniaFact]
    public async Task The_upload_review_replaces_the_member_pane()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var file = Path.Combine(Path.GetTempPath(), $"lizterm-{Guid.NewGuid():N}.jcl");
        await File.WriteAllTextAsync(file, "x\n");
        try
        {
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);

            Assert.True(Named<Control>(window, "ReviewPane").IsVisible);
            Assert.False(Named<Control>(window, "MemberPane").IsVisible);
            Assert.Equal(1, Named<ItemsControl>(window, "UploadList").ItemCount);
            Assert.False(Named<ListBox>(window, "DatasetList").IsEffectivelyEnabled);
            Assert.Equal("Upload to MVSCE02.CNTL", Named<BrowserPane>(window, "MembersPane").Title);
            Assert.Equal("1 file", Named<BrowserPane>(window, "MembersPane").FooterText);
            Assert.True(Named<WrapPanel>(window, "ReviewToolbar").IsVisible);
            Assert.False(Named<WrapPanel>(window, "MemberToolbar").IsVisible);
            Assert.True(Named<DropDownButton>(window, "TransferButton").IsVisible);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public async Task Choosing_binary_puts_the_padding_note_on_the_status_line_and_the_label_on_the_button()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        var transfer = Named<DropDownButton>(window, "TransferButton");
        Assert.Equal("Transfer: Text", transfer.Content);

        t.Vm.IsBinaryMode = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Transfer: Binary", transfer.Content);
        Assert.Equal(MvsmfBrowserViewModel.PaddingNote, Named<TextBlock>(window, "StatusLine").Text);
    }

    [AvaloniaFact]
    public async Task The_transfer_menu_binds_the_mode_and_the_upload_options()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        var transfer = Named<DropDownButton>(window, "TransferButton");
        var flyout = (MenuFlyout)transfer.Flyout!;
        flyout.ShowAt(transfer);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items.OfType<MenuItem>().ToDictionary(i => i.Name!);
        try
        {
            Assert.True(items["TextModeItem"].IsChecked);
            Assert.False(items["BinaryModeItem"].IsChecked);
            Assert.True(items["TrimItem"].IsChecked);
            Assert.True(items["VerifyItem"].IsChecked);
            Assert.True(items["TrimItem"].IsEffectivelyEnabled);

            items["BinaryModeItem"].IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(t.Vm.IsBinaryMode);
            Assert.False(items["TrimItem"].IsEffectivelyEnabled);

            items["TextModeItem"].IsChecked = true;
            items["VerifyItem"].IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(t.Vm.IsTextMode);
            Assert.False(t.Vm.VerifyUploads);
        }
        finally
        {
            flyout.Hide();
        }
    }

    [AvaloniaFact]
    public async Task Each_pane_has_its_toolbar_and_the_bottom_bar_is_only_status()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var datasets = Named<BrowserPane>(window, "DatasetsPane");
        var members = Named<BrowserPane>(window, "MembersPane");
        Assert.Equal("Datasets", datasets.Title);
        Assert.Equal("4 datasets · none selected", datasets.FooterText);
        Assert.Equal("Members", members.Title);

        var datasetVerbs = Named<WrapPanel>(window, "DatasetToolbar").Children.OfType<Button>().Select(b => b.Content).ToList();
        Assert.Equal(new object?[] { "New…", "Rename…", "Delete…", "↻ Refresh" }, datasetVerbs);
        Assert.Same(t.Vm.RefreshCommand, Named<Button>(window, "RefreshButton").Command);

        var memberVerbs = Named<WrapPanel>(window, "MemberToolbar").Children.OfType<Button>()
            .Select(b => b.Content).ToList();
        Assert.Equal(new object?[] { "View", "⇣ Download…", "⇡ Upload…", "Rename…", "Delete…" }, memberVerbs);

        Assert.Null(window.FindControl<RadioButton>("TextModeButton"));
        Assert.Null(window.FindControl<TextBlock>("PaddingNote"));
        Assert.False(Named<Button>(window, "CancelButton").IsVisible);
        Assert.True(Named<TextBlock>(window, "StatusLine").IsVisible);
    }

    [AvaloniaFact]
    public async Task The_members_pane_stays_for_a_sequential_dataset_and_carries_the_note()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.UFSHOME");
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<BrowserPane>(window, "MembersPane").IsVisible);
        Assert.Equal("MVSCE02.UFSHOME", Named<BrowserPane>(window, "MembersPane").Title);
        Assert.True(Named<TextBlock>(window, "SequentialNote").IsVisible);
        Assert.False(Named<DockPanel>(window, "MemberPane").IsVisible);
        Assert.True(Named<Button>(window, "DownloadButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task Closing_cancels_and_releases_the_connection()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        window.Close();

        Assert.True(t.Host.Disposed);
    }

    [AvaloniaFact]
    public async Task Enter_in_the_filter_box_lists_again()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var box = Named<TextBox>(window, "FilterBox");
        box.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("list:")) == 2, "a second listing");
    }

    [AvaloniaFact]
    public async Task A_member_filter_that_hides_selected_rows_leaves_only_the_visible_ones_selected()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[0]);
        members.SelectedItems.Add(t.Vm.VisibleMembers[2]);
        Assert.Equal(new[] { "ALLOC", "HELLO" }, t.Vm.SelectedMembers.Select(m => m.Name));

        Named<TextBox>(window, "MemberFilterBox").Text = "HEL";

        Assert.Equal(new[] { "HELLO" }, t.Vm.VisibleMembers.Select(m => m.Name));
        var visibleSelection = members.SelectedItems.OfType<MemberRow>().Where(t.Vm.VisibleMembers.Contains);
        Assert.Equal(visibleSelection.Select(m => m.Name), t.Vm.SelectedMembers.Select(m => m.Name));
        Assert.DoesNotContain(t.Vm.SelectedMembers, m => m.Name == "ALLOC");
    }

    [AvaloniaFact]
    public async Task Delete_in_the_member_list_asks_about_the_selection()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[2]);
        window.UpdateLayout();
        Assert.True(members.ContainerFromIndex(2)!.Focus());

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");
        Assert.Equal("Delete 1 member", t.Vm.Confirmation!.PrimaryLabel);
    }

    [AvaloniaFact]
    public async Task Backspace_in_the_member_list_asks_about_the_selection()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[2]);
        window.UpdateLayout();
        Assert.True(members.ContainerFromIndex(2)!.Focus());

        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);

        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");
        Assert.Equal("Delete 1 member", t.Vm.Confirmation!.PrimaryLabel);
    }

    [AvaloniaFact]
    public void Escape_with_nothing_pending_closes_the_window()
    {
        var (window, t) = Show(userid: null);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(window.IsVisible);
        Assert.True(t.Host.Disposed);
    }

    [AvaloniaFact]
    public async Task Escape_in_an_open_upload_review_closes_the_review_not_the_window()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var folder = Directory.CreateTempSubdirectory("lizterm-window-upload-").FullName;
        try
        {
            var file = Path.Combine(folder, "newmem.jcl");
            await File.WriteAllTextAsync(file, "//NEWMEM JOB\n");
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);
            Assert.True(t.Vm.IsReviewingUpload);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.False(t.Vm.IsReviewingUpload);
            Assert.True(window.IsVisible);
            Assert.False(t.Host.Disposed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task A_sent_upload_row_keeps_its_member_name()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var folder = Directory.CreateTempSubdirectory("lizterm-window-upload-").FullName;
        try
        {
            var file = Path.Combine(folder, "newmem.jcl");
            await File.WriteAllTextAsync(file, "//NEWMEM JOB\n");
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);
            window.UpdateLayout();
            var nameBox = Named<ItemsControl>(window, "UploadList").GetVisualDescendants().OfType<TextBox>().Single();
            Assert.False(nameBox.IsReadOnly);

            await t.Vm.StartUploadCommand.ExecuteAsync(null);

            Assert.True(t.Vm.Uploads[0].Sent);
            Assert.True(nameBox.IsReadOnly);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Listing_is_off_while_an_upload_review_is_open()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var folder = Directory.CreateTempSubdirectory("lizterm-window-upload-").FullName;
        try
        {
            var file = Path.Combine(folder, "newmem.jcl");
            await File.WriteAllTextAsync(file, "//NEWMEM JOB\n");
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);

            Assert.False(Named<Button>(window, "ListButton").IsEffectivelyEnabled);
            var box = Named<TextBox>(window, "FilterBox");
            Assert.False(box.IsEffectivelyEnabled);
            box.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c.StartsWith("list:")));
            Assert.True(t.Vm.IsReviewingUpload);
            Assert.Same(t.Vm.Datasets[0], t.Vm.SelectedDataset);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Choosing_a_pds_with_the_keyboard_keeps_focus_in_the_dataset_list()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var list = Named<ListBox>(window, "DatasetList");
        window.UpdateLayout();
        Assert.True(list.ContainerFromIndex(0)!.Focus());
        var gate = new TaskCompletionSource();
        t.Host.Gate = gate;

        list.SelectedIndex = 0;
        await Wait.UntilAsync(() => t.Vm.IsBusy, "the member load");
        Dispatcher.UIThread.RunJobs();
        Assert.False(list.IsKeyboardFocusWithin);
        gate.SetResult();
        await Wait.UntilAsync(() => !t.Vm.IsBusy && t.Vm.Members.Count == 3, "the members");

        await Wait.UntilAsync(() => list.IsKeyboardFocusWithin, "the focus back in the dataset list");
    }

    [AvaloniaFact]
    public async Task Focus_returns_to_the_member_list_after_a_delete()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[2]);
        window.UpdateLayout();
        Assert.True(members.ContainerFromIndex(2)!.Focus());

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the delete");

        Assert.Equal(2, t.Vm.Members.Count);
        await Wait.UntilAsync(() => members.IsKeyboardFocusWithin, "the focus back in the member list");
    }

    [AvaloniaFact]
    public async Task A_question_takes_the_focus_to_its_cancel_button()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");
        t.Select("HELLO");

        _ = t.Vm.DeleteCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");

        await Wait.UntilAsync(() => ReferenceEquals(window.FocusManager?.GetFocusedElement(), Named<Button>(window, "ConfirmCancelButton")),
            "the focus on Cancel");
    }

    [AvaloniaFact]
    public async Task An_input_question_shows_a_text_box_focused_and_selected_and_enter_answers_it()
    {
        var (window, t) = Show(userid: null);
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        t.Vm.Confirmation = question;

        var box = Named<TextBox>(window, "ConfirmInputBox");
        await Wait.UntilAsync(() => box.IsFocused, "the focus in the text box");
        Assert.True(box.IsVisible);
        Assert.Equal("HELLO", box.Text);
        Assert.Equal("HELLO", box.SelectedText);
        Assert.False(Named<TextBlock>(window, "ConfirmInputProblem").IsVisible);
        Assert.False(Named<Button>(window, "ConfirmPrimaryButton").IsEffectivelyEnabled);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.False(question.Answer.IsCompleted);

        box.Text = "BAD-NAME";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<TextBlock>(window, "ConfirmInputProblem").IsVisible);
        Assert.Equal("✗ A member name cannot contain '-'.", Named<TextBlock>(window, "ConfirmInputProblem").Text);

        box.Text = "HELLO2";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<Button>(window, "ConfirmPrimaryButton").IsEffectivelyEnabled);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.Equal(ConfirmChoice.Primary, (await question.Answer).Choice);
        Assert.Equal("HELLO2", question.Input);
        t.Vm.Confirmation = null;
    }

    [AvaloniaFact]
    public async Task A_plain_question_hides_the_text_box()
    {
        var (window, t) = Show(userid: null);

        t.Vm.Confirmation = new ConfirmationRequest("Delete HELLO from MVSCE02.CNTL? This cannot be undone.", "Delete 1 member");

        await Wait.UntilAsync(() => Named<Button>(window, "ConfirmCancelButton").IsFocused, "the focus on Cancel");
        Assert.False(Named<TextBox>(window, "ConfirmInputBox").IsVisible);
        t.Vm.Confirmation = null;
    }

    [AvaloniaFact]
    public async Task The_manage_buttons_follow_the_selection()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var rename = Named<Button>(window, "RenameDatasetButton");
        var delete = Named<Button>(window, "DeleteDatasetButton");
        var renameMember = Named<Button>(window, "RenameMemberButton");
        Assert.Equal("Rename…", rename.Content);
        Assert.Equal("Delete…", delete.Content);
        Assert.Equal("Rename…", renameMember.Content);
        Assert.False(rename.IsEffectivelyEnabled);
        Assert.False(delete.IsEffectivelyEnabled);
        Assert.False(renameMember.IsEffectivelyEnabled);

        await t.ChooseAsync("MVSCE02.DB");
        Dispatcher.UIThread.RunJobs();
        Assert.True(rename.IsEffectivelyEnabled);
        Assert.True(delete.IsEffectivelyEnabled);
        Assert.False(renameMember.IsEffectivelyEnabled);

        await t.ChooseAsync("MVSCE02.CNTL");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(renameMember.IsEffectivelyEnabled);
        members.SelectedItems!.Add(t.Vm.VisibleMembers[1]);
        Dispatcher.UIThread.RunJobs();
        Assert.False(renameMember.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task A_renamed_member_is_selected_in_the_list()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        var members = Named<ListBox>(window, "MemberList");
        members.SelectedItems!.Add(t.Vm.VisibleMembers[2]);
        Dispatcher.UIThread.RunJobs();

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");
        t.Vm.Confirmation!.Input = "HELLO2";
        t.Vm.Confirmation.PrimaryCommand.Execute(null);
        await renaming;
        window.UpdateLayout();

        Assert.Equal(new[] { "HELLO2" }, members.SelectedItems!.OfType<MemberRow>().Select(m => m.Name));
        Assert.Equal(new[] { "HELLO2" }, t.Vm.SelectedMembers.Select(m => m.Name));
    }

    private static async Task<NewDatasetWindow> OpenNewDatasetAsync(MvsmfBrowserWindow window, BrowserTestHost t)
    {
        t.Vm.NewDatasetCommand.Execute(null);
        await Wait.UntilAsync(() => window.NewDatasetDialog is { IsVisible: true }, "the New dataset dialog");
        return window.NewDatasetDialog!;
    }

    [AvaloniaFact]
    public async Task New_opens_a_dialog_over_the_window_with_the_focus_in_the_name_box()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        var newButton = Named<Button>(window, "NewDatasetButton");
        Assert.Equal("New…", newButton.Content);
        Assert.True(newButton.IsEffectivelyEnabled);

        newButton.Command!.Execute(null);
        await Wait.UntilAsync(() => window.NewDatasetDialog is { IsVisible: true }, "the New dataset dialog");
        var dialog = window.NewDatasetDialog!;

        Assert.Same(dialog, Assert.Single(window.OwnedWindows));
        Assert.Equal("New dataset", dialog.Title);
        Assert.True(Named<DockPanel>(window, "MemberPane").IsVisible);
        var name = dialog.FindControl<TextBox>("NewNameBox")!;
        await Wait.UntilAsync(() => name.IsFocused, "the focus in the name box");
        Assert.Equal("MVSCE02.", name.Text);
        Assert.False(dialog.FindControl<Button>("CreateButton")!.IsEffectivelyEnabled);

        name.Text = "MVSCE02.NEW";
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.FindControl<Button>("CreateButton")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task Escape_in_the_dialog_closes_it_and_leaves_the_window()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var dialog = await OpenNewDatasetAsync(window, t);

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Wait.UntilAsync(() => window.NewDatasetDialog is null, "the dialog to close");

        Assert.False(t.Vm.IsCreating);
        Assert.True(window.IsVisible);
        Assert.Empty(window.OwnedWindows);
        await Wait.UntilAsync(() => Named<TextBox>(window, "FilterBox").IsFocused, "the focus back in the filter box");
    }

    [AvaloniaFact]
    public async Task Escape_in_the_dialog_cancels_a_create_in_flight_and_keeps_the_dialog()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var dialog = await OpenNewDatasetAsync(window, t);
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Gate = new TaskCompletionSource();
        try
        {
            var create = t.Vm.CreateCommand.ExecuteAsync(null);
            await Wait.UntilAsync(() => t.Vm.IsBusy, "the create to start");

            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await create;

            Assert.Equal("– Cancelled.", t.Vm.StatusText);
            Assert.True(t.Vm.IsCreating);
            Assert.Same(dialog, window.NewDatasetDialog);
            Assert.True(dialog.IsVisible);
            Assert.DoesNotContain(t.Vm.Datasets, d => d.Name == "MVSCE02.NEW");
        }
        finally
        {
            t.Host.Gate.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task The_dialogs_close_box_cancels_a_create_in_flight()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var dialog = await OpenNewDatasetAsync(window, t);
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Gate = new TaskCompletionSource();
        try
        {
            var create = t.Vm.CreateCommand.ExecuteAsync(null);
            await Wait.UntilAsync(() => t.Vm.IsBusy, "the create to start");

            dialog.Close();
            await create;

            Assert.Equal("– Cancelled.", t.Vm.StatusText);
            Assert.True(t.Vm.IsCreating);
            Assert.Same(dialog, window.NewDatasetDialog);
        }
        finally
        {
            t.Host.Gate.TrySetResult();
        }
    }

    [AvaloniaFact]
    public async Task A_form_closed_and_opened_again_in_one_turn_gets_a_new_dialog()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var first = await OpenNewDatasetAsync(window, t);

        t.Vm.CloseFormCommand.Execute(null);
        t.Vm.NewDatasetCommand.Execute(null);
        await Wait.UntilAsync(() => window.NewDatasetDialog is { IsVisible: true }, "the second dialog");

        Assert.NotSame(first, window.NewDatasetDialog);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.NewDatasetDialog);
        Assert.True(t.Vm.IsCreating);
    }

    [AvaloniaFact]
    public async Task The_dialogs_close_box_is_the_forms_close()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var dialog = await OpenNewDatasetAsync(window, t);

        dialog.Close();
        await Wait.UntilAsync(() => window.NewDatasetDialog is null, "the dialog to close");

        Assert.False(t.Vm.IsCreating);
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_refused_create_keeps_the_dialog_open_with_the_message()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var dialog = await OpenNewDatasetAsync(window, t);
        t.Vm.Form.Name = "MVSCE02.CNTL";
        t.Host.Failures["create:MVSCE02.CNTL"] = new HostFileException(HostFileErrorKind.CannotAllocate, "x", 900, "Dynamic allocation Error");

        await t.Vm.CreateCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(t.Vm.IsCreating);
        Assert.Same(dialog, window.NewDatasetDialog);
        Assert.True(dialog.FindControl<TextBlock>("CreateMessage")!.IsVisible);
        await Wait.UntilAsync(() => dialog.FindControl<TextBox>("NewNameBox")!.IsFocused, "the focus back in the name box");
    }

    [AvaloniaFact]
    public async Task A_create_that_succeeds_closes_the_dialog_and_lists_the_new_dataset()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var dialog = await OpenNewDatasetAsync(window, t);
        t.Vm.Form.Name = "MVSCE02.NEW";

        await t.Vm.CreateCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => window.NewDatasetDialog is null, "the dialog to close");

        Assert.False(t.Vm.IsCreating);
        Assert.False(dialog.IsVisible);
        Assert.Contains(t.Vm.Datasets, d => d.Name == "MVSCE02.NEW");
        Assert.Equal("MVSCE02.NEW", t.Vm.SelectedDataset?.Name);
    }

    [AvaloniaFact]
    public async Task Closing_the_window_while_a_create_runs_closes_the_dialog_too()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var dialog = await OpenNewDatasetAsync(window, t);
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Gate = new TaskCompletionSource();
        try
        {
            _ = t.Vm.CreateCommand.ExecuteAsync(null);
            await Wait.UntilAsync(() => t.Vm.IsBusy, "the create to start");

            window.Close();

            Assert.False(window.IsVisible);
            Assert.False(dialog.IsVisible);
        }
        finally
        {
            t.Host.Gate.SetResult();
        }
    }

    [AvaloniaFact]
    public async Task The_context_menus_bind_the_same_commands_as_the_toolbars()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");

        var datasetMenu = Named<ListBox>(window, "DatasetList").ContextMenu!;
        datasetMenu.Open(Named<ListBox>(window, "DatasetList"));
        Dispatcher.UIThread.RunJobs();
        try
        {
            var items = datasetMenu.Items.OfType<MenuItem>().ToDictionary(i => i.Name!);
            Assert.Same(t.Vm.NewDatasetCommand, items["DatasetMenuNew"].Command);
            Assert.Same(t.Vm.RenameDatasetCommand, items["DatasetMenuRename"].Command);
            Assert.Same(t.Vm.DeleteDatasetCommand, items["DatasetMenuDelete"].Command);
            Assert.Same(t.Vm.RefreshCommand, items["DatasetMenuRefresh"].Command);
            Assert.Equal(window.NewDatasetGesture, items["DatasetMenuNew"].InputGesture);
            Assert.Equal(window.RefreshGesture, items["DatasetMenuRefresh"].InputGesture);
        }
        finally
        {
            datasetMenu.Close();
        }

        var memberMenu = Named<ListBox>(window, "MemberList").ContextMenu!;
        memberMenu.Open(Named<ListBox>(window, "MemberList"));
        Dispatcher.UIThread.RunJobs();
        try
        {
            var items = memberMenu.Items.OfType<MenuItem>().ToDictionary(i => i.Name!);
            Assert.Same(t.Vm.ViewCommand, items["MemberMenuView"].Command);
            Assert.Equal(window.ViewGesture, items["MemberMenuView"].InputGesture);
            Assert.Same(t.Vm.DownloadCommand, items["MemberMenuDownload"].Command);
            Assert.Same(t.Vm.UploadCommand, items["MemberMenuUpload"].Command);
            Assert.Same(t.Vm.RenameMemberCommand, items["MemberMenuRename"].Command);
            Assert.Same(t.Vm.DeleteCommand, items["MemberMenuDelete"].Command);
            Assert.Equal(new KeyGesture(Key.Enter), items["MemberMenuDownload"].InputGesture);
            Assert.Equal(new KeyGesture(Key.Delete), items["MemberMenuDelete"].InputGesture);
        }
        finally
        {
            memberMenu.Close();
        }
    }

    [AvaloniaFact]
    public async Task Double_clicking_a_member_downloads_it()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.CNTL");
        // The Standard seed lists COMPILE as a member but stores no content for it (FakeHostFileService.Text is
        // keyed separately): without this the download itself would fail Not found, and the double-click could
        // never be told apart from a click that reached DownloadCommand but failed for an unrelated reason.
        t.Host.Text["MVSCE02.CNTL(COMPILE)"] = ["//COMPILE JOB", "//STEP EXEC PGM=IEFBR14"];
        window.UpdateLayout();
        var list = Named<ListBox>(window, "MemberList");
        var row = (Control)list.ContainerFromIndex(1)!;
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        var target = Path.Combine(Path.GetTempPath(), $"lizterm-{Guid.NewGuid():N}.txt");
        t.Picker.Result = target;
        try
        {
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);

            await Wait.UntilAsync(() => t.Vm.StatusText.StartsWith("✓"), "the download");
            Assert.Contains("save:", t.Picker.Calls.Single(c => c.StartsWith("save:")));
            Assert.Equal(new[] { "COMPILE" }, t.Vm.SelectedMembers.Select(m => m.Name));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [AvaloniaFact]
    public async Task The_command_key_shortcuts_refresh_and_open_new()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        var modifiers = window.RefreshGesture.KeyModifiers;
        var raw = modifiers.HasFlag(KeyModifiers.Meta) ? RawInputModifiers.Meta : RawInputModifiers.Control;
        t.Host.AddDataset("MVSCE02.NEW", dsorg: "PS");

        window.KeyPress(Key.R, raw, PhysicalKey.R, null);
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 5, "the refreshed listing");

        window.KeyPress(Key.N, raw, PhysicalKey.N, null);
        await Wait.UntilAsync(() => window.NewDatasetDialog is { IsVisible: true }, "the New dataset dialog");
        window.NewDatasetDialog!.Close();
        await Wait.UntilAsync(() => window.NewDatasetDialog is null, "the dialog to close");
    }

    [AvaloniaFact]
    public async Task Delete_in_the_dataset_list_asks_about_the_dataset()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        await t.ChooseAsync("MVSCE02.DB");
        window.UpdateLayout();
        ((Control)Named<ListBox>(window, "DatasetList").ContainerFromIndex(3)!).Focus();

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");

        Assert.Contains("MVSCE02.DB", t.Vm.Confirmation!.Message);
        t.Vm.Confirmation.CancelCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task The_verbs_carry_tooltips_that_name_their_keys()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        Assert.Equal($"Refresh the list ({window.RefreshGesture.ToString("p", null)})", ToolTip.GetTip(Named<Button>(window, "RefreshButton")));
        Assert.Equal($"Allocate a new dataset ({window.NewDatasetGesture.ToString("p", null)})", ToolTip.GetTip(Named<Button>(window, "NewDatasetButton")));
        Assert.Equal("Download the selected members (Enter)", ToolTip.GetTip(Named<Button>(window, "DownloadButton")));
        Assert.Equal("Delete the selected members (Delete)", ToolTip.GetTip(Named<Button>(window, "DeleteButton")));
    }
}
