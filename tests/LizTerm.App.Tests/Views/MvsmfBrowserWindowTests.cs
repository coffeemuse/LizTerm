// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.Tests.ViewModels;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;

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

    [AvaloniaFact]
    public async Task Opens_with_the_preview_strip_and_lists_the_users_datasets()
    {
        var (window, t) = Show();

        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");

        Assert.Equal("mvsMF Browser — MVS/CE (Preview)", window.Title);
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
        Assert.Equal("Load more datasets", moreDatasets.Content);

        await t.ChooseAsync("MVSCE02.BIG");
        Assert.True(moreMembers.IsVisible);
        Assert.Equal("Load more members", moreMembers.Content);

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
        Assert.Equal("MVSCE02.CNTL · 3 members", Named<TextBlock>(window, "MemberHeader").Text);
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
        }
        finally
        {
            File.Delete(file);
        }
    }

    [AvaloniaFact]
    public async Task Binary_mode_shows_the_padding_note()
    {
        var (window, t) = Show();
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 4, "the first listing");
        t.Vm.SelectedDataset = t.Vm.Datasets[0];
        await Wait.UntilAsync(() => !t.Vm.IsBusy, "the members");

        Named<RadioButton>(window, "BinaryModeButton").IsChecked = true;

        Assert.True(t.Vm.IsBinaryMode);
        Assert.True(Named<TextBlock>(window, "PaddingNote").IsVisible);
        Assert.Equal("⚠ Binary transfers to fixed-length datasets are padded to whole records.", Named<TextBlock>(window, "PaddingNote").Text);
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
}
