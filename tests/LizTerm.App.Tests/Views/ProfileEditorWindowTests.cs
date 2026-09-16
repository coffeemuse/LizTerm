// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ProfileEditorWindowTests
{
    private static ProfileEditorViewModel Vm(Window window) => (ProfileEditorViewModel)window.DataContext!;

    private static TabItem SelectedTab(Window window) =>
        (TabItem)window.FindControl<TabControl>("Tabs")!.SelectedItem!;

    private static void Save(Window window) =>
        window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>The tag box's own text box, which is what keyboard focus and typed text reach.</summary>
    private static TextBox FocusTagBox(Window window)
    {
        var box = window.FindControl<AutoCompleteBox>("TagBox")!;
        var text = box.GetVisualDescendants().OfType<TextBox>().First();
        text.Focus();
        Dispatcher.UIThread.RunJobs();
        return text;
    }

    [AvaloniaFact]
    public void The_pinned_block_shows_only_for_a_pinned_profile_and_forget_hides_it()
    {
        var pinned = new ProfileEditorWindow(new SessionProfile
        {
            Name = "gw", Host = "gw", UseTls = true, PinnedCertificate = new CertificatePin("8C:13", "CN=gw", "pem"),
        });
        pinned.Show();
        var panel = pinned.FindControl<Border>("PinPanel")!;
        Assert.True(panel.IsVisible);
        Assert.Equal("CN=gw", pinned.FindControl<TextBlock>("PinSubject")!.Text);
        Assert.Equal("SHA-256 8C:13", pinned.FindControl<TextBlock>("PinFingerprint")!.Text);
        pinned.FindControl<Button>("ForgetButton")!.Command!.Execute(null);
        Assert.False(panel.IsVisible);

        var plain = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        plain.Show();
        Assert.False(plain.FindControl<Border>("PinPanel")!.IsVisible);
        Assert.Equal("Backspace erases the previous character", plain.FindControl<CheckBox>("BackspaceBox")!.Content);
    }

    /// <summary>Three tabs, opening on Connection with the cursor in Name.</summary>
    [AvaloniaFact]
    public void It_opens_on_connection_with_the_name_focused()
    {
        var window = new ProfileEditorWindow(null);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Connection", "Terminal", "Organize"],
            window.FindControl<TabControl>("Tabs")!.Items.Cast<TabItem>().Select(t => t.Header));
        Assert.Same(window.FindControl<TabItem>("ConnectionTab"), SelectedTab(window));
        Assert.True(window.FindControl<TextBox>("NameBox")!.IsFocused);
        Assert.Equal("New Session Profile", window.Title);
    }

    /// <summary>A refused Save shows the tab the message is about and outlines the field, and the outline goes
    /// when the message does.</summary>
    [AvaloniaFact]
    public void A_refused_save_shows_the_fields_tab_and_outlines_it()
    {
        var window = new ProfileEditorWindow(null);
        window.Show();
        var tabs = window.FindControl<TabControl>("Tabs")!;
        var name = window.FindControl<TextBox>("NameBox")!;

        tabs.SelectedItem = window.FindControl<TabItem>("OrganizeTab");
        Save(window);
        Assert.Same(window.FindControl<TabItem>("ConnectionTab"), SelectedTab(window));
        Assert.Equal("Give the profile a name.", window.FindControl<TextBlock>("ValidationText")!.Text);
        Assert.Contains("invalid", name.Classes);

        Vm(window).Name = "p";
        Vm(window).Host = "h";
        Vm(window).SelectedModelChoice = ModelChoice.Other;
        Vm(window).ColumnsDisplay = "10";
        tabs.SelectedItem = window.FindControl<TabItem>("ConnectionTab");
        Save(window);
        Assert.Same(window.FindControl<TabItem>("TerminalTab"), SelectedTab(window));
        Assert.DoesNotContain("invalid", name.Classes);
        Assert.Contains("invalid", window.FindControl<NumericUpDown>("ColumnsBox")!.Classes);
        Assert.Contains("invalid", window.FindControl<NumericUpDown>("RowsBox")!.Classes);

        Vm(window).ColumnsDisplay = "80";
        Assert.DoesNotContain("invalid", window.FindControl<NumericUpDown>("ColumnsBox")!.Classes);
    }

    [AvaloniaFact]
    public void A_refused_tag_shows_the_organize_tab()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        window.Show();
        Vm(window).TagEntry = new string('x', TagSet.MaxNameLength + 1);

        Save(window);

        Assert.Same(window.FindControl<TabItem>("OrganizeTab"), SelectedTab(window));
        Assert.Contains("invalid", window.FindControl<Border>("TagField")!.Classes);
    }

    [AvaloniaFact]
    public void The_tag_and_note_rows_render_the_profiles_values()
    {
        var registry = new TagRegistry([new("PROD", TagColor.Red), new("MVS", TagColor.Blue)]);
        var window = new ProfileEditorWindow(new SessionProfile
        {
            Name = "mvsce", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]), Note = "no live data",
        }, registry);
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("OrganizeTab");
        window.UpdateLayout();

        var chips = window.FindControl<ItemsControl>("TagChipList")!;
        Assert.Equal(["PROD"], chips.Items.Cast<TagChip>().Select(c => c.Text));
        var remove = chips.GetVisualDescendants().OfType<Button>().Single();
        Assert.Equal("Remove PROD", Avalonia.Automation.AutomationProperties.GetName(remove));
        Assert.Equal(["MVS"], window.FindControl<AutoCompleteBox>("TagBox")!.ItemsSource!.Cast<TagChip>().Select(c => c.Text));
        Assert.True(window.FindControl<CheckBox>("FavoriteBox")!.IsChecked);
        Assert.Equal("no live data", window.FindControl<TextBox>("NoteBox")!.Text);

        remove.Command!.Execute(remove.CommandParameter);
        Assert.Empty(chips.Items);
    }

    /// <summary>Enter adds what is typed and stops there, so it never reaches Save, the default button; Backspace in
    /// the empty box removes the last chip.</summary>
    [AvaloniaFact]
    public void Enter_adds_a_tag_and_backspace_removes_the_last()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h", Tags = TagSet.From(["PROD"]) });
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("OrganizeTab");
        window.UpdateLayout();
        FocusTagBox(window);

        window.KeyTextInput("mvs");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(["PROD", "mvs"], Vm(window).TagNames);
        Assert.False(closed);

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        Assert.Equal(["PROD"], Vm(window).TagNames);
        Assert.False(closed);
    }

    [AvaloniaFact]
    public void A_typed_comma_adds_a_tag()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("OrganizeTab");
        window.UpdateLayout();
        var text = FocusTagBox(window);

        window.KeyTextInput("prod,");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["prod"], Vm(window).TagNames);
        Assert.Equal("", text.Text);
    }

    /// <summary>Choosing a suggestion adds it; Escape closes the list without adding the highlighted one.</summary>
    [AvaloniaFact]
    public void A_chosen_suggestion_becomes_a_chip_and_escape_adds_nothing()
    {
        var registry = new TagRegistry([new("MVS", TagColor.Blue), new("VM", TagColor.Red)]);
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" }, registry);
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("OrganizeTab");
        window.UpdateLayout();
        var box = window.FindControl<AutoCompleteBox>("TagBox")!;
        FocusTagBox(window);
        Assert.True(box.IsDropDownOpen);

        box.SelectedItem = Vm(window).TagSuggestions[1];
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(box.IsDropDownOpen);
        Assert.Empty(Vm(window).TagNames);

        box.IsDropDownOpen = true;
        box.SelectedItem = Vm(window).TagSuggestions[1];
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(["VM"], Vm(window).TagNames);
        Assert.Equal(["MVS"], Vm(window).TagSuggestions.Select(c => c.Text));
        Assert.Equal("", Vm(window).TagEntry);
    }

    /// <summary>A real press and release on a suggestion, because the pointer path is the one that misbehaved in
    /// the app: the drop-down closed before the box recorded the choice, so the first click only put the text in the
    /// box and the second one added the chip.</summary>
    [AvaloniaFact]
    public void A_clicked_suggestion_becomes_a_chip_on_the_first_click()
    {
        var registry = new TagRegistry([new("BBS", TagColor.Amber), new("VM", TagColor.Red)]);
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h", Tags = TagSet.From(["MVS"]) }, registry);
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("OrganizeTab");
        window.UpdateLayout();
        var box = window.FindControl<AutoCompleteBox>("TagBox")!;
        FocusTagBox(window);
        window.KeyTextInput("BB");
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        Assert.True(box.IsDropDownOpen);

        var item = box.GetVisualDescendants().OfType<ListBoxItem>()
            .Concat(TopLevelItems(window))
            .First(i => i.DataContext is TagChip { Text: "BBS" });
        var host = TopLevel.GetTopLevel(item)!;
        var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), host)!.Value;
        host.MouseDown(centre, MouseButton.Left);
        host.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["MVS", "BBS"], Vm(window).TagNames);
        Assert.Equal("", Vm(window).TagEntry);
        Assert.Equal("", box.Text);
    }

    /// <summary>On macOS the drop-down is a native popup: the press selects the row and takes focus from the box,
    /// which closes the drop-down, so the release never arrives. The press alone must choose, and leave the box
    /// focused and empty.</summary>
    [AvaloniaFact]
    public void A_pressed_suggestion_becomes_a_chip_without_a_release()
    {
        var registry = new TagRegistry([new("BBS", TagColor.Amber), new("DEV", TagColor.Blue)]);
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h", Tags = TagSet.From(["MVS"]) }, registry);
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("OrganizeTab");
        window.UpdateLayout();
        var box = window.FindControl<AutoCompleteBox>("TagBox")!;
        var text = FocusTagBox(window);
        window.KeyTextInput("BB");
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();

        var item = TopLevelItems(window).First(i => i.DataContext is TagChip { Text: "BBS" });
        var host = TopLevel.GetTopLevel(item)!;
        host.MouseDown(item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), host)!.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["MVS", "BBS"], Vm(window).TagNames);
        Assert.Equal("", box.Text);
        Assert.True(text.IsFocused);
        Assert.False(box.IsDropDownOpen);
    }

    private static IEnumerable<ListBoxItem> TopLevelItems(Window window) =>
        window.GetVisualDescendants().OfType<ListBoxItem>();

    /// <summary>A pasted paragraph must not reach the list, where it would reshape every row.</summary>
    [AvaloniaFact]
    public void The_note_box_is_single_line_and_capped()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        window.Show();
        var note = window.FindControl<TextBox>("NoteBox")!;
        Assert.False(note.AcceptsReturn);
        Assert.Equal(120, note.MaxLength);
    }

    /// <summary>The size spinners are always there, greyed out showing the model's own size, and open once Other is
    /// chosen, starting from that size.</summary>
    [AvaloniaFact]
    public void The_size_boxes_open_only_for_other()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h", Model = 5 });
        window.Show();
        var modelBox = window.FindControl<ComboBox>("ModelBox")!;
        var columns = window.FindControl<NumericUpDown>("ColumnsBox")!;
        var rows = window.FindControl<NumericUpDown>("RowsBox")!;

        Assert.True(columns.IsVisible);
        Assert.False(columns.IsEnabled);
        Assert.False(rows.IsEnabled);
        Assert.Equal("132", columns.Text);
        Assert.Equal("27", rows.Text);

        modelBox.SelectedItem = ModelChoice.Other;

        Assert.True(columns.IsEnabled);
        Assert.True(rows.IsEnabled);
        Assert.Equal("132", columns.Text);
        columns.Text = "140";
        rows.Text = "30";
        Assert.Equal("140x30", Vm(window).TryBuild()!.Oversize);
    }

    /// <summary>The spinners have no range, so a number outside the engine's limits stays as typed for the view
    /// model's message to explain, rather than being snapped or emptied in silence; the arrows step by one.</summary>
    [AvaloniaFact]
    public void The_size_spinners_keep_an_out_of_range_number_and_step_by_one()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h", Oversize = "100x30" });
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("TerminalTab");
        window.UpdateLayout();
        var columns = window.FindControl<NumericUpDown>("ColumnsBox")!;
        var text = columns.GetVisualDescendants().OfType<TextBox>().First();
        text.Focus();
        text.SelectAll();
        window.KeyTextInput("40");
        window.FindControl<TextBox>("NoteBox")!.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("40", Vm(window).ColumnsText);
        Assert.Null(Vm(window).TryBuild());
        Assert.Contains("at least 80 columns", Vm(window).ValidationMessage);

        text.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal("41", Vm(window).ColumnsText);
    }
}
