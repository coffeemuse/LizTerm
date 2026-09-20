# mvsMF Access pane pattern Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the mvsMF browser window into two `BrowserPane`s with their own toolbars, move the transfer
options into a drop-down and the new-dataset form into a dialog, add Refresh, context menus, double-click and
shortcuts, and rename the window to mvsMF Access, without changing any host call or transfer path.

**Architecture:** A new `BrowserPane` UserControl in `src/LizTerm.App/Controls` owns the frame (title row, toolbar,
body, footer) and knows nothing about hosts. `MvsmfBrowserWindow.axaml` becomes two uses of it, with every verb bound
to the command the view model already has. `MvsmfBrowserViewModel` gains only strings (title, footers, drop-down
label) and one command (Refresh, which keeps the selection). `NewDatasetWindow` is an owned modal dialog bound to the
same view model; the browser window opens it when `IsCreating` turns on and it closes itself when `IsCreating` turns
off.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (Fluent theme, compiled bindings on by default), CommunityToolkit.Mvvm,
xunit.v3 with `Avalonia.Headless.XUnit` (`[AvaloniaFact]`).

**Spec:** `docs/superpowers/specs/2026-09-19-mvsmf-access-pane-pattern-design.md`

## Global Constraints

- Every new `.cs` and `.axaml` file starts with the licence header (`This file is part of LizTerm.`,
  `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause`) in its comment syntax; `RepositoryHeadersTests` fails otherwise.
- `LizTerm.Core` is not touched. `LizTerm.App` names no backend outside `SessionFactory.cs` and `HostFileServiceFactory.cs`.
- Every modal dialog opens through `ModalDialogs.ShowDialogAbove`; `ModalDialogsTests` fails for any other `ShowDialog` call.
- Toolbar buttons are text, with three Unicode marks only: `↻ Refresh`, `⇣ Download…`, `⇡ Upload…`. No icon package.
- One code path per verb: a toolbar button, a context-menu item, a shortcut and a double-click bind to the same command instance.
- No user-facing text may imply MVS/CE is the only host. The user-facing name is **mvsMF Access**; type and file names keep `MvsmfBrowser*`.
- Zero-based coordinates, snapshots and the threading contract are untouched by this work (no session code changes).
- Before calling anything done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- Commit after every task with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` as the last line.

Two spec details are amended by this plan, because the code was read after the spec was written: the pane's list
slot is the property `Body`, not `Content` (a `UserControl` already uses `Content` for its own XAML root), and
`NewDatasetWindow` lives in `src/LizTerm.App/Views` beside every other window, not in `Dialogs`, which holds prompt
interfaces and adapters only. Task 7 records both in the spec.

---

## File structure

| File | Responsibility |
|---|---|
| Create `src/LizTerm.App/Controls/BrowserPane.axaml` + `.axaml.cs` | The pane frame: six styled properties, the `pane-verb` button style, nothing about hosts |
| Create `tests/LizTerm.App.Tests/Controls/BrowserPaneTests.cs` | The frame on its own |
| Modify `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs` | Title, `MembersTitle`, `DatasetsFooter`, `MembersFooter`, `TransferModeLabel`, `PaddingNote`, collection hooks |
| Modify `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Paging.cs` | `RefreshCommand`; `MembersHeader` notifications become `MembersFooter` |
| Modify `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs` | `OnModeChanged` pushes the padding note to the status line |
| Modify `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs` | one `MembersHeader` notification |
| Create `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserPaneTextTests.cs` | Footers, title, label, padding note, Refresh |
| Create `src/LizTerm.App/Views/NewDatasetWindow.axaml` + `.axaml.cs` | The allocation form as an owned modal dialog |
| Modify `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` + `.axaml.cs` | Two panes, drop-down, status bar, dialog opening, context menus, double-click, keys |
| Modify `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` | Follow the new names; new tests for the dialog, menus, double-click, keys |
| Modify `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewModelTests.cs`, `MvsmfBrowserPagingTests.cs`, `tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs` | Title and `MembersHeader` assertions |
| Modify `src/LizTerm.App/Views/SessionWindow.axaml` + `.axaml.cs`, `tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs` | Menu item text |
| Modify `src/LizTerm.App/Views/ProfileEditorWindow.axaml`, `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs`, `tests/LizTerm.App.Tests/ViewModels/ProfileEditorMvsmfTests.cs` | Hint and message text |
| Modify `docs/user-guide.md`, `src/LizTerm.App/Assets/Docs/user-guide.html` (regenerated), `CHANGELOG.md`, `README.md`, `src/LizTerm.App/CLAUDE.md`, the spec | Docs |

---

### Task 1: The `BrowserPane` control

**Files:**
- Create: `src/LizTerm.App/Controls/BrowserPane.axaml`
- Create: `src/LizTerm.App/Controls/BrowserPane.axaml.cs`
- Test: `tests/LizTerm.App.Tests/Controls/BrowserPaneTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LizTerm.App.Controls.BrowserPane : UserControl` with styled properties `string Title`, `object? HeaderContent`, `object? Toolbar`, `object? Body`, `string FooterText`, `object? FooterAction`; named parts `TitleText` (TextBlock), `HeaderPresenter`, `ToolbarPresenter`, `BodyPresenter`, `FooterActionPresenter` (ContentPresenter), `FooterTextBlock` (TextBlock); a style `Button.pane-verb` (Padding 8,3; FontSize 13) that applies to any button inside the pane.

- [ ] **Step 1: Write the failing test**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;

namespace LizTerm.App.Tests.Controls;

/// <summary>The pane frame on its own (pane-pattern spec §4): six slots, a footer whose right cell collapses when
/// there is no action, and the compact verb style. Nothing about hosts; the window's half is in
/// MvsmfBrowserWindowTests.</summary>
public class BrowserPaneTests
{
    private static (BrowserPane Pane, Window Window) Show(BrowserPane pane)
    {
        var window = new Window { Width = 480, Height = 360, Content = pane };
        window.Show();
        window.UpdateLayout();
        return (pane, window);
    }

    private static T Part<T>(BrowserPane pane, string name) where T : Control => pane.FindControl<T>(name)!;

    [AvaloniaFact]
    public void Every_slot_lands_in_its_presenter()
    {
        var filter = new TextBox();
        var toolbar = new StackPanel();
        var list = new ListBox();
        var more = new Button { Content = "Load more" };
        var (pane, _) = Show(new BrowserPane
        {
            Title = "Datasets", HeaderContent = filter, Toolbar = toolbar, Body = list,
            FooterText = "4 datasets · none selected", FooterAction = more,
        });

        Assert.Equal("Datasets", Part<TextBlock>(pane, "TitleText").Text);
        Assert.Same(filter, Part<ContentPresenter>(pane, "HeaderPresenter").Child);
        Assert.Same(toolbar, Part<ContentPresenter>(pane, "ToolbarPresenter").Child);
        Assert.Same(list, Part<ContentPresenter>(pane, "BodyPresenter").Child);
        Assert.Equal("4 datasets · none selected", Part<TextBlock>(pane, "FooterTextBlock").Text);
        Assert.Same(more, Part<ContentPresenter>(pane, "FooterActionPresenter").Child);
    }

    [AvaloniaFact]
    public void The_footer_action_cell_collapses_without_an_action()
    {
        var (pane, window) = Show(new BrowserPane { Title = "Members", FooterText = "No members" });
        var action = Part<ContentPresenter>(pane, "FooterActionPresenter");
        Assert.False(action.IsVisible);

        pane.FooterAction = new Button { Content = "Load more" };
        window.UpdateLayout();
        Assert.True(action.IsVisible);
    }

    [AvaloniaFact]
    public void A_verb_button_in_the_toolbar_is_compact()
    {
        var verb = new Button { Content = "New…", Classes = { "pane-verb" } };
        var (_, _) = Show(new BrowserPane { Toolbar = new StackPanel { Children = { verb } } });

        Assert.Equal(new Thickness(8, 3), verb.Padding);
        Assert.Equal(13, verb.FontSize);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~BrowserPaneTests"`
Expected: build error, `BrowserPane` does not exist.

- [ ] **Step 3: Write the control**

`src/LizTerm.App/Controls/BrowserPane.axaml`:

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<!-- One pane of the mvsMF Access window (pane-pattern spec §4): title row, toolbar, body, footer. The window fills
     the slots with its own controls, whose bindings compile against the window's view model; this control binds
     only to its own properties and knows nothing about hosts. -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="using:LizTerm.App.Controls"
             x:Class="LizTerm.App.Controls.BrowserPane">
  <UserControl.Styles>
    <Style Selector="TextBlock.pane-title">
      <Setter Property="FontWeight" Value="SemiBold" />
      <Setter Property="VerticalAlignment" Value="Center" />
      <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>
    <Style Selector="TextBlock.pane-footer">
      <Setter Property="Foreground" Value="#A0A0A0" />
      <Setter Property="FontSize" Value="12" />
      <Setter Property="VerticalAlignment" Value="Center" />
    </Style>
    <!-- The verbs: compact, so four of them and a drop-down fit a half-width pane. -->
    <Style Selector="Button.pane-verb">
      <Setter Property="Padding" Value="8,3" />
      <Setter Property="FontSize" Value="13" />
      <Setter Property="MinHeight" Value="0" />
    </Style>
    <Style Selector="DropDownButton.pane-verb">
      <Setter Property="Padding" Value="8,3" />
      <Setter Property="FontSize" Value="13" />
      <Setter Property="MinHeight" Value="0" />
    </Style>
  </UserControl.Styles>
  <Border BorderBrush="#33373D" BorderThickness="1" CornerRadius="4">
    <DockPanel>
      <DockPanel x:Name="TitleRow" DockPanel.Dock="Top" Margin="8,6,8,4">
        <ContentPresenter x:Name="HeaderPresenter" DockPanel.Dock="Right" Margin="8,0,0,0"
                          Content="{Binding $parent[controls:BrowserPane].HeaderContent}" />
        <TextBlock x:Name="TitleText" Classes="pane-title" Text="{Binding $parent[controls:BrowserPane].Title}" />
      </DockPanel>
      <Border DockPanel.Dock="Top" BorderBrush="#33373D" BorderThickness="0,1,0,1" Padding="8,4">
        <ContentPresenter x:Name="ToolbarPresenter" Content="{Binding $parent[controls:BrowserPane].Toolbar}" />
      </Border>
      <Border DockPanel.Dock="Bottom" BorderBrush="#33373D" BorderThickness="0,1,0,0" Padding="8,4">
        <DockPanel>
          <ContentPresenter x:Name="FooterActionPresenter" DockPanel.Dock="Right" Margin="8,0,0,0"
                            Content="{Binding $parent[controls:BrowserPane].FooterAction}"
                            IsVisible="{Binding $parent[controls:BrowserPane].FooterAction, Converter={x:Static ObjectConverters.IsNotNull}}" />
          <TextBlock x:Name="FooterTextBlock" Classes="pane-footer" Text="{Binding $parent[controls:BrowserPane].FooterText}" />
        </DockPanel>
      </Border>
      <ContentPresenter x:Name="BodyPresenter" Content="{Binding $parent[controls:BrowserPane].Body}" />
    </DockPanel>
  </Border>
</UserControl>
```

`src/LizTerm.App/Controls/BrowserPane.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;

namespace LizTerm.App.Controls;

/// <summary>One pane of the mvsMF Access window (pane-pattern spec §4): a title row with an optional header
/// control, a toolbar of the verbs that act on this pane's selection, a body (the column header and list) and a
/// footer (a count and an optional action). It owns the frame, the spacing and the verb style, and knows nothing
/// about hosts: the window fills the slots and binds their contents to its own view model. The Datasets and
/// Members panes are two uses of it; a USS browser's directory and file panes will be two more.</summary>
public partial class BrowserPane : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<BrowserPane, string>(nameof(Title), "");

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<object?> ToolbarProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(Toolbar));

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(Body));

    public static readonly StyledProperty<string> FooterTextProperty =
        AvaloniaProperty.Register<BrowserPane, string>(nameof(FooterText), "");

    public static readonly StyledProperty<object?> FooterActionProperty =
        AvaloniaProperty.Register<BrowserPane, object?>(nameof(FooterAction));

    public BrowserPane() => InitializeComponent();

    /// <summary>The title row's text: the pane's name, or the dataset it shows.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The title row's right-hand control, such as a filter box.</summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    /// <summary>The row of verbs under the title.</summary>
    public object? Toolbar
    {
        get => GetValue(ToolbarProperty);
        set => SetValue(ToolbarProperty, value);
    }

    /// <summary>The column header and the list. Named Body because a UserControl's Content is its own XAML.</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>The footer's text: "41 datasets · 1 selected".</summary>
    public string FooterText
    {
        get => GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    /// <summary>The footer's right-hand control, such as Load more; the cell collapses when null.</summary>
    public object? FooterAction
    {
        get => GetValue(FooterActionProperty);
        set => SetValue(FooterActionProperty, value);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~BrowserPaneTests"`
Expected: 3 passed. If `Every_slot_lands_in_its_presenter` finds a null `Child`, add `window.UpdateLayout()` is already there; then check the `$parent[controls:BrowserPane]` binding compiled (a build warning names it).

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/Controls/BrowserPane.axaml src/LizTerm.App/Controls/BrowserPane.axaml.cs tests/LizTerm.App.Tests/Controls/BrowserPaneTests.cs
git commit -m "Add BrowserPane, the shared frame for the mvsMF Access panes (#17)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: View model strings and Refresh

**Files:**
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Paging.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Uploads.cs`
- Modify: `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.Delete.cs:98`
- Modify: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserViewModelTests.cs:22,66`
- Modify: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserPagingTests.cs:68,80,93,155,163`
- Modify: `tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs:80`
- Modify: `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs:42,117`
- Test: `tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserPaneTextTests.cs`

**Interfaces:**
- Consumes: the existing `MvsmfBrowserViewModel` (`Datasets`, `Members`, `VisibleMembers`, `Uploads`, `SelectedDataset`, `HasMoreDatasets`, `HasMoreMembers`, `_memberPattern`, `_selectedMembers`, `IsReviewingUpload`, `UploadHeader`, `Mode`, `IsBinaryMode`, `ShowPaddingNote`, `ListCoreAsync`, `SelectAsync`, `RunExclusiveAsync`, `Plural`).
- Produces: `string Title` = `"mvsMF Access — {profile} (Preview)"`; `string MembersTitle`; `string DatasetsFooter`; `string MembersFooter`; `string TransferModeLabel` (`"Transfer: Text"` / `"Transfer: Binary"`); `const string PaddingNote`; `IAsyncRelayCommand RefreshCommand`. `MembersHeader` is removed.

- [ ] **Step 1: Write the failing tests**

`tests/LizTerm.App.Tests/ViewModels/MvsmfBrowserPaneTextTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>The strings the two panes show (pane-pattern spec §4.1 and §4.2), the drop-down's label (§5), the
/// padding note on the status line, and Refresh.</summary>
public class MvsmfBrowserPaneTextTests
{
    [Fact]
    public void The_title_names_mvsmf_access()
    {
        var t = BrowserTestHost.Create();
        Assert.Equal("mvsMF Access — MVS/CE (Preview)", t.Vm.Title);
    }

    [Fact]
    public async Task The_footers_count_what_is_listed_and_selected()
    {
        var t = BrowserTestHost.Create();
        Assert.Equal("No datasets", t.Vm.DatasetsFooter);
        Assert.Equal("Members", t.Vm.MembersTitle);
        Assert.Equal("", t.Vm.MembersFooter);

        await t.ListAsync();
        Assert.Equal("4 datasets · none selected", t.Vm.DatasetsFooter);

        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.Equal("4 datasets · 1 selected", t.Vm.DatasetsFooter);
        Assert.Equal("MVSCE02.CNTL", t.Vm.MembersTitle);
        Assert.Equal("3 members · none selected", t.Vm.MembersFooter);

        t.Select("ALLOC", "HELLO");
        Assert.Equal("3 members · 2 selected", t.Vm.MembersFooter);

        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.Equal("MVSCE02.UFSHOME", t.Vm.MembersTitle);
        Assert.Equal("", t.Vm.MembersFooter);
    }

    [Fact]
    public async Task The_footers_mark_a_list_the_host_has_more_of()
    {
        var t = BrowserTestHost.Create(seed: BrowserTestHost.Large, pageSize: 2);
        await t.ListAsync();
        Assert.Equal("2+ datasets · none selected", t.Vm.DatasetsFooter);

        await t.ChooseAsync("MVSCE02.BIG");
        Assert.Equal("2+ members · none selected", t.Vm.MembersFooter);

        await t.FilterMembersAsync("L");
        Assert.Equal("2+ matching · none selected", t.Vm.MembersFooter);
    }

    [Fact]
    public async Task A_local_member_filter_counts_the_rows_it_shows()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        await t.FilterMembersAsync("HEL");

        Assert.Equal("1 of 3 members · none selected", t.Vm.MembersFooter);
    }

    [Fact]
    public async Task An_open_review_titles_the_pane_and_counts_its_files()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        var file = Path.Combine(Path.GetTempPath(), $"lizterm-{Guid.NewGuid():N}.jcl");
        await File.WriteAllTextAsync(file, "x\n");
        try
        {
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);

            Assert.True(t.Vm.IsReviewingUpload);
            Assert.Equal("Upload to MVSCE02.CNTL", t.Vm.MembersTitle);
            Assert.Equal("1 file", t.Vm.MembersFooter);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task The_transfer_label_follows_the_mode()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.Equal("Transfer: Text", t.Vm.TransferModeLabel);

        t.Vm.IsBinaryMode = true;
        Assert.Equal("Transfer: Binary", t.Vm.TransferModeLabel);
    }

    [Fact]
    public async Task Choosing_binary_on_a_fixed_dataset_puts_the_padding_note_on_the_status_line()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.Equal("3 members", t.Vm.StatusText);

        t.Vm.IsBinaryMode = true;
        Assert.Equal(MvsmfBrowserViewModel.PaddingNote, t.Vm.StatusText);

        // A load library is binary by itself and not fixed-length: the listing's count stays.
        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.True(t.Vm.IsBinaryMode);
        Assert.Equal("1 member", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_lists_the_filter_again_and_keeps_the_chosen_dataset()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.AddDataset("MVSCE02.NEW", dsorg: "PS");
        t.Host.Members["MVSCE02.CNTL"].Add("ADDED");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(5, t.Vm.Datasets.Count);
        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
        Assert.Equal(4, t.Vm.Members.Count);
        Assert.Equal("4 members", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_says_when_the_chosen_dataset_is_gone()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.Datasets.RemoveAll(d => d.Name == "MVSCE02.CNTL");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Null(t.Vm.SelectedDataset);
        Assert.Equal(3, t.Vm.Datasets.Count);
        Assert.Equal("3 datasets · MVSCE02.CNTL is no longer listed", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_with_nothing_chosen_is_a_plain_listing()
    {
        var t = BrowserTestHost.Create();
        await t.ListAsync();
        t.Host.AddDataset("MVSCE02.NEW", dsorg: "PS");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(5, t.Vm.Datasets.Count);
        Assert.Equal("5 datasets", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_is_off_while_an_operation_runs_or_a_review_is_open()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.True(t.Vm.RefreshCommand.CanExecute(null));

        t.Host.Gate = new TaskCompletionSource();
        var listing = t.Vm.ListCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.IsBusy, "the listing to start");
        Assert.False(t.Vm.RefreshCommand.CanExecute(null));
        t.Host.Gate.SetResult();
        await listing;
        Assert.True(t.Vm.RefreshCommand.CanExecute(null));
    }
}
```

Then update the four existing assertions: `MvsmfBrowserViewModelTests.cs:22`, `SessionWindowMvsmfTests.cs:80` and
`MvsmfBrowserWindowTests.cs:42` to `"mvsMF Access — MVS/CE (Preview)"`; `MvsmfBrowserViewModelTests.cs:66` to
`Assert.Equal("MVSCE02.CNTL", t.Vm.MembersTitle); Assert.Equal("3 members · none selected", t.Vm.MembersFooter);`;
in `MvsmfBrowserPagingTests.cs` replace the five `MembersHeader` assertions with `MembersFooter` ones:

| Line | Was | Becomes |
|---|---|---|
| 68 | `"MVSCE02.LOAD · 1 member"` | `"1 member · none selected"` |
| 80 | `"MVSCE02.BIG · 2+ members"` | `"2+ members · none selected"` |
| 93 | `"MVSCE02.BIG · 5 members"` | `"5 members · none selected"` |
| 155 | `"MVSCE02.BIG · 2+ matching"` | `"2+ matching · none selected"` |
| 163 | `"MVSCE02.BIG · 4 matching"` | `"4 matching · none selected"` |

Leave `MvsmfBrowserWindowTests.cs:117` for Task 4 (it names a control that goes then); note it fails until then.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserPaneTextTests"`
Expected: build error, `DatasetsFooter`, `MembersTitle`, `RefreshCommand` and `PaddingNote` do not exist.

- [ ] **Step 3: Implement the strings**

In `MvsmfBrowserViewModel.cs`:

1. `Title` becomes `$"mvsMF Access — {_access.ProfileName} (Preview)"`.
2. Add `using System.Globalization;` and, in the constructor after `WatchForm();`:

```csharp
        // The footers count the collections, so they follow every add, clear and refill.
        Datasets.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DatasetsFooter));
        Members.CollectionChanged += (_, _) => OnPropertyChanged(nameof(MembersFooter));
        VisibleMembers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(MembersFooter));
        Uploads.CollectionChanged += (_, _) => OnPropertyChanged(nameof(MembersFooter));
```

3. On `_selectedDataset`, replace `nameof(MembersHeader)` with `nameof(MembersTitle), nameof(DatasetsFooter), nameof(MembersFooter)`.
4. On `_mode`, add `nameof(TransferModeLabel)` to its `NotifyPropertyChangedFor`.
5. Replace the `MembersHeader` property, `MembersCount()` and `MembersStatus()` block with:

```csharp
    /// <summary>The Members pane's title row: the chosen dataset's name, the review's header while one is open,
    /// and "Members" when nothing supported is chosen (the body then carries the hint).</summary>
    public string MembersTitle => IsReviewingUpload ? UploadHeader
        : SelectedDataset is { IsSupported: true } dataset ? dataset.Name
        : "Members";

    /// <summary>The Datasets pane's footer: the count, a plus while the host has more, and the selection.</summary>
    public string DatasetsFooter => Datasets.Count == 0 ? "No datasets"
        : $"{Counted(Datasets.Count, HasMoreDatasets, "dataset")} · {(SelectedDataset is null ? "none" : "1")} selected";

    /// <summary>The Members pane's footer: the files under review, else the members shown (with a plus while the
    /// host has more, "matching" when the host applied the filter, "n of m" when the filter narrowed the list
    /// here) and how many are selected. Empty for a dataset that has no member list.</summary>
    public string MembersFooter
    {
        get
        {
            if (IsReviewingUpload) return Plural(Uploads.Count, "file");
            if (SelectedDataset is not { IsPartitioned: true }) return "";
            if (Members.Count == 0) return _memberPattern is null ? "No members" : "No matching members";
            var shown = _memberPattern is not null ? $"{Members.Count}{(HasMoreMembers ? "+" : "")} matching"
                : VisibleMembers.Count < Members.Count ? $"{VisibleMembers.Count} of {Plural(Members.Count, "member")}"
                : Counted(Members.Count, HasMoreMembers, "member");
            var selected = _selectedMembers.Count == 0 ? "none" : _selectedMembers.Count.ToString(CultureInfo.InvariantCulture);
            return $"{shown} · {selected} selected";
        }
    }

    /// <summary>The status line after a member listing: the count, the pattern the host applied, and whether it
    /// has more.</summary>
    private string MembersStatus() =>
        (_memberPattern is null ? Plural(Members.Count, "member") : $"{Members.Count} matching")
        + (_memberPattern is { } pattern ? " " + pattern : "") + (HasMoreMembers ? " shown, more on the host" : "");

    private static string Counted(int count, bool more, string noun) => more ? $"{count}+ {noun}s" : Plural(count, noun);

    /// <summary>The drop-down button's label (pane-pattern spec §5).</summary>
    public string TransferModeLabel => IsBinaryMode ? "Transfer: Binary" : "Transfer: Text";

    /// <summary>Binary transfers to fixed-length records are padded to whole records (compatibility log,
    /// binary-fixed-padding); the status line says so when Binary is chosen on such a dataset.</summary>
    public const string PaddingNote = "⚠ Binary transfers to fixed-length datasets are padded to whole records.";
```

   Keep `ShowPaddingNote` as it is (the Uploads partial reads it).
6. In `SetSelectedMembers`, after `OnPropertyChanged(nameof(SelectedMembers));` add `OnPropertyChanged(nameof(MembersFooter));`.
7. In `ClearMembers`, change `OnPropertyChanged(nameof(MembersHeader));` to `OnPropertyChanged(nameof(MembersFooter));`.
8. In `NotifyCommands()`, add `RefreshCommand.NotifyCanExecuteChanged();` after `ListCommand.NotifyCanExecuteChanged();`.

In `MvsmfBrowserViewModel.Paging.cs`:

1. Line 35: `[NotifyPropertyChangedFor(nameof(MembersHeader))]` becomes `[NotifyPropertyChangedFor(nameof(MembersFooter))]`; on `_hasMoreDatasets` add `[NotifyPropertyChangedFor(nameof(DatasetsFooter))]`.
2. Lines 81 and 110: `nameof(MembersHeader)` becomes `nameof(MembersFooter)`.
3. After `LoadMoreDatasetsAsync`'s block add:

```csharp
    /// <summary>Lists the filter again and keeps the chosen dataset when it is still listed, reloading its members
    /// (pane-pattern spec §4.1). Off whenever List is.</summary>
    [RelayCommand(CanExecute = nameof(CanChooseDataset))]
    private Task RefreshAsync() => RunExclusiveAsync(RefreshCoreAsync, () => RefreshAsync());

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        if (IsReviewingUpload) return;
        if (HostPath.DatasetPatternError(Filter) is { } problem)
        {
            StatusText = "✗ " + problem;
            return;
        }
        var keep = SelectedDataset?.Name;
        await ListCoreAsync(token);
        if (keep is null) return;
        if (Datasets.FirstOrDefault(row => row.Name == keep) is { } row) await SelectAsync(row, token);
        else StatusText = $"{DatasetsStatus()} · {keep} is no longer listed";
    }
```

In `MvsmfBrowserViewModel.Uploads.cs`, replace `partial void OnModeChanged(HostTransferMode value) => RecheckIfReviewing();` with:

```csharp
    partial void OnModeChanged(HostTransferMode value)
    {
        RecheckIfReviewing();
        // Chosen by the user (a dataset's own choice of mode is followed by its member listing, which sets the
        // status line itself): the bar's old padding note becomes the status line.
        if (ShowPaddingNote && !IsBusy) StatusText = PaddingNote;
    }
```

In `MvsmfBrowserViewModel.Delete.cs:98`, `nameof(MembersHeader)` becomes `nameof(MembersFooter)`.

`IsReviewingUpload` (Uploads.cs line 17) gains `nameof(MembersTitle), nameof(MembersFooter)` in its `NotifyPropertyChangedFor`.

- [ ] **Step 4: Run the view-model tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowser"`
Expected: `MvsmfBrowserPaneTextTests` all pass; the view-model and paging suites pass; in `MvsmfBrowserWindowTests`
only `Choosing_a_pds_shows_its_members_and_selection_reaches_the_view_model` fails (it names `MembersHeader`; Task 4
fixes it). If `Choosing_binary_on_a_fixed_dataset…` sees `"3 members"` after the mode change, `ChooseAsync` returned
before the member load's status line was set: check `SelectAsync` is used (it awaits the load) and that
`OnModeChanged` runs after `RecheckIfReviewing`.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.App/ViewModels tests/LizTerm.App.Tests
git commit -m "mvsMF Access: pane titles and footers, the transfer label, the padding note on the status line, and Refresh (#17)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The New dataset dialog

**Files:**
- Create: `src/LizTerm.App/Views/NewDatasetWindow.axaml`
- Create: `src/LizTerm.App/Views/NewDatasetWindow.axaml.cs`
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` (remove `CreatePane`)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs` (open the dialog on `IsCreating`)
- Test: `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs` (replace the three form tests at lines 537–600)

**Interfaces:**
- Consumes: `MvsmfBrowserViewModel.IsCreating`, `Form` (`NewDatasetFormViewModel`), `CreateCommand`, `CloseFormCommand`, `IsIdle`, `IsBusy`; `ModalDialogs.ShowDialogAbove`.
- Produces: `LizTerm.App.Views.NewDatasetWindow : Window` (DataContext is the browser view model; named controls `NewNameBox`, `PartitionedButton`, `SequentialButton`, `NewRecfmBox`, `NewLreclBox`, `NewBlksizeBox`, `TracksButton`, `CylindersButton`, `NewPrimaryBox`, `NewSecondaryBox`, `NewDirectoryBlocksBox`, `CreateMessage`, `CancelButton`, `CreateButton`); `internal NewDatasetWindow? MvsmfBrowserWindow.NewDatasetDialog { get; }`.

- [ ] **Step 1: Write the failing tests**

In `MvsmfBrowserWindowTests.cs`, delete `New_opens_the_form_in_the_right_pane_with_the_focus_in_the_name_box`,
`Escape_closes_the_form_before_the_window` and `Focus_returns_to_the_form_after_a_refused_create`, and add:

```csharp
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
```

Check the `Failures` key shape against `MvsmfBrowserCreateTests.cs:158` (`"create:MVSCE02.NEW"`) and the
`HostFileException` constructor used there; copy that constructor's argument order.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: build error, `NewDatasetWindow` and `NewDatasetDialog` do not exist.

- [ ] **Step 3: Write the dialog**

`src/LizTerm.App/Views/NewDatasetWindow.axaml` (the form is the `CreatePane` body moved out of the browser window,
with the message and buttons under it):

```xml
<!--
  This file is part of LizTerm.
  Copyright 2026 by CoffeeMuse
  SPDX-License-Identifier: BSD-3-Clause
-->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:LizTerm.App.ViewModels"
        x:Class="LizTerm.App.Views.NewDatasetWindow"
        x:DataType="vm:MvsmfBrowserViewModel"
        Title="New dataset" Width="480" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterOwner">
  <Window.Styles>
    <Style Selector="TextBlock.problem">
      <Setter Property="Foreground" Value="#FF8080" />
      <Setter Property="TextWrapping" Value="Wrap" />
      <Setter Property="FontSize" Value="12" />
    </Style>
    <Style Selector="TextBox">
      <Setter Property="FontFamily" Value="Menlo, Consolas, monospace" />
    </Style>
  </Window.Styles>
  <DockPanel Margin="16">
    <StackPanel DockPanel.Dock="Bottom" Spacing="8" Margin="0,12,0,0">
      <TextBlock x:Name="CreateMessage" Text="{Binding Form.Message}" Foreground="#FF8080" TextWrapping="Wrap"
                 IsVisible="{Binding Form.Message, Converter={x:Static ObjectConverters.IsNotNull}}" />
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
        <Button x:Name="CancelButton" Content="Cancel" Command="{Binding CloseFormCommand}" IsCancel="True" />
        <Button x:Name="CreateButton" Content="Create" Command="{Binding CreateCommand}" IsDefault="True" />
      </StackPanel>
    </StackPanel>
    <StackPanel Spacing="4" IsEnabled="{Binding IsIdle}">
      <TextBlock Text="Name" />
      <TextBox x:Name="NewNameBox" Text="{Binding Form.Name}" MaxLength="44" />
      <TextBlock Classes="problem" Text="{Binding Form.NameProblem}" IsVisible="{Binding Form.NameProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
      <TextBlock Text="Type" Margin="0,6,0,0" />
      <StackPanel Orientation="Horizontal" Spacing="12">
        <RadioButton x:Name="PartitionedButton" GroupName="NewType" Content="Partitioned (PDS)" IsChecked="{Binding Form.IsPartitioned}" />
        <RadioButton x:Name="SequentialButton" GroupName="NewType" Content="Sequential" IsChecked="{Binding Form.IsSequential}" />
      </StackPanel>
      <Grid ColumnDefinitions="*,8,*,8,*" Margin="0,6,0,0">
        <StackPanel Grid.Column="0" Spacing="4">
          <TextBlock Text="RECFM" />
          <TextBox x:Name="NewRecfmBox" Text="{Binding Form.Recfm}" MaxLength="4" />
          <TextBlock Classes="problem" Text="{Binding Form.RecfmProblem}" IsVisible="{Binding Form.RecfmProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
        <StackPanel Grid.Column="2" Spacing="4">
          <TextBlock Text="LRECL" />
          <TextBox x:Name="NewLreclBox" Text="{Binding Form.Lrecl}" MaxLength="5" />
          <TextBlock Classes="problem" Text="{Binding Form.LreclProblem}" IsVisible="{Binding Form.LreclProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
        <StackPanel Grid.Column="4" Spacing="4">
          <TextBlock Text="BLKSIZE" />
          <TextBox x:Name="NewBlksizeBox" Text="{Binding Form.Blksize}" MaxLength="5" />
          <TextBlock Classes="problem" Text="{Binding Form.BlksizeProblem}" IsVisible="{Binding Form.BlksizeProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
      </Grid>
      <TextBlock Text="Space" Margin="0,6,0,0" />
      <StackPanel Orientation="Horizontal" Spacing="12">
        <RadioButton x:Name="TracksButton" GroupName="NewSpace" Content="Tracks" IsChecked="{Binding Form.IsTracks}" />
        <RadioButton x:Name="CylindersButton" GroupName="NewSpace" Content="Cylinders" IsChecked="{Binding Form.IsCylinders}" />
      </StackPanel>
      <Grid ColumnDefinitions="*,8,*,8,*">
        <StackPanel Grid.Column="0" Spacing="4">
          <TextBlock Text="Primary" />
          <TextBox x:Name="NewPrimaryBox" Text="{Binding Form.Primary}" MaxLength="5" />
          <TextBlock Classes="problem" Text="{Binding Form.PrimaryProblem}" IsVisible="{Binding Form.PrimaryProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
        <StackPanel Grid.Column="2" Spacing="4">
          <TextBlock Text="Secondary" />
          <TextBox x:Name="NewSecondaryBox" Text="{Binding Form.Secondary}" MaxLength="5" />
          <TextBlock Classes="problem" Text="{Binding Form.SecondaryProblem}" IsVisible="{Binding Form.SecondaryProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
        <StackPanel Grid.Column="4" Spacing="4" IsEnabled="{Binding Form.IsPartitioned}">
          <TextBlock Text="Directory blocks" />
          <TextBox x:Name="NewDirectoryBlocksBox" Text="{Binding Form.DirectoryBlocks}" MaxLength="5" />
          <TextBlock Classes="problem" Text="{Binding Form.DirectoryBlocksProblem}" IsVisible="{Binding Form.DirectoryBlocksProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
        </StackPanel>
      </Grid>
    </StackPanel>
  </DockPanel>
</Window>
```

`src/LizTerm.App/Views/NewDatasetWindow.axaml.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The New dataset form as an owned modal dialog (pane-pattern spec §6), bound to the browser's view
/// model so the form, Create and Close are the ones the view model already has. Its life is the view model's
/// IsCreating: the browser window opens it when that turns on, and a create that succeeds, or Close, turns it off
/// and the dialog goes with it. A refused create leaves it on, so the dialog stays with the message. The title
/// bar's close box is the form's Close; while the create runs the form cannot be closed, so neither can the
/// dialog.</summary>
public partial class NewDatasetWindow : Window
{
    private MvsmfBrowserViewModel? _watched;
    private bool _closingWithForm;

    public NewDatasetWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NewNameBox.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = DataContext as MvsmfBrowserViewModel;
        if (_watched is not null) _watched.PropertyChanged += OnViewModelPropertyChanged;
        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MvsmfBrowserViewModel.IsCreating) || _watched is not { IsCreating: false }) return;
        _closingWithForm = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closingWithForm && _watched is { IsCreating: true } vm)
        {
            // Refused here, then closed through the form: IsCreating turning off comes back as the Close above.
            // Posted, so the view model's notification does not close a window that is still inside Closing.
            e.Cancel = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (vm.CloseFormCommand.CanExecute(null)) vm.CloseFormCommand.Execute(null);
            });
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = null;
        base.OnClosed(e);
    }
}
```

- [ ] **Step 4: Open it from the browser window**

In `MvsmfBrowserWindow.axaml`, delete the whole `<DockPanel x:Name="CreatePane" …>` element (from its opening tag to
its `</DockPanel>`, the last child of the right-hand `Panel`).

In `MvsmfBrowserWindow.axaml.cs`:

1. Add `using LizTerm.App.Dialogs;`.
2. Replace the two `IsCreating` cases in `OnViewModelPropertyChanged` with:

```csharp
            case nameof(MvsmfBrowserViewModel.IsCreating) when vm.IsCreating:
                _ = ShowNewDatasetAsync(vm);
                break;
            case nameof(MvsmfBrowserViewModel.IsCreating) when !vm.IsBusy:
                // Closed without an operation (Cancel, Escape, the close box): the dialog gave the keyboard back to
                // this window, which puts it where the window opens. A form an operation closes is handled by the
                // IsBusy case above.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_watched is not { IsCreating: false, IsBusy: false }) return;
                    if (FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: true }) return;
                    FilterBox.Focus();
                }, DispatcherPriority.Loaded);
                break;
```

3. Add, after `OnViewModelPropertyChanged`:

```csharp
    /// <summary>The open New dataset dialog, if any (pane-pattern spec §6); for the tests and the close path.</summary>
    internal NewDatasetWindow? NewDatasetDialog { get; private set; }

    /// <summary>Opens the form as a modal dialog over this window. The dialog closes itself when IsCreating turns
    /// off. A dialog that cannot be shown is a status line, and the form is closed so the commands come back.</summary>
    private async Task ShowNewDatasetAsync(MvsmfBrowserViewModel vm)
    {
        if (NewDatasetDialog is not null) return;
        var dialog = new NewDatasetWindow { DataContext = vm };
        NewDatasetDialog = dialog;
        try
        {
            await dialog.ShowDialogAbove(this);
        }
        catch (Exception ex)
        {
            vm.StatusText = "✗ Could not open the New dataset window: " + ex.Message;
            if (vm.CloseFormCommand.CanExecute(null)) vm.CloseFormCommand.Execute(null);
        }
        finally
        {
            NewDatasetDialog = null;
        }
    }
```

4. In `OnKeyDownTunnel`, delete the line `else if (vm.IsCreating) vm.CloseFormCommand.Execute(null);` (a modal dialog takes the keys while it is open).
5. In `OnClosed`, before `ViewModel?.Dispose();`, add `NewDatasetDialog?.Close();` (an owned dialog closes with its owner anyway; this makes the order explicit before the view model is disposed).

- [ ] **Step 5: Run the window tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: the five new tests pass; the rest of the file as before (the one `MembersHeader` failure stays until Task
4). If `Escape_in_the_dialog…` times out, the headless `KeyPress` reached the browser window rather than the dialog:
call `dialog.Activate()` before the key press. If `A_refused_create…` loses the focus assertion, the dialog's
`IsEnabled="{Binding IsIdle}"` dropped it during the create; add to `NewDatasetWindow` a `PropertyChanged` case for
`IsBusy` turning off that posts `NewNameBox.Focus()` at `DispatcherPriority.Loaded` when `_watched is { IsCreating: true }`.

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~ModalDialogsTests"`
Expected: pass (the dialog goes through `ShowDialogAbove`).

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "mvsMF Access: New dataset is an owned modal dialog (#17)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: The window becomes two panes

**Files:**
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` (whole body)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs` (summary only)
- Test: `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`

**Interfaces:**
- Consumes: `BrowserPane` (Task 1); `MembersTitle`, `DatasetsFooter`, `MembersFooter`, `TransferModeLabel`, `RefreshCommand` (Task 2).
- Produces: named controls kept: `PreviewStrip`, `GuideLink`, `FilterBox`, `ListButton`, `ErrorBanner`, `RetryButton`, `ConfirmationStrip` and its children, `CancelButton`, `Progress`, `StatusLine`, `DatasetList`, `LoadMoreDatasetsButton`, `NewDatasetButton`, `RenameDatasetButton`, `DeleteDatasetButton`, `MemberFilterBox`, `MemberPane` (DockPanel), `ChooseHint`, `SequentialNote`, `MemberList`, `LoadMoreMembersButton`, `DownloadButton`, `UploadButton`, `RenameMemberButton`, `DeleteButton`, `ReviewPane`, `UploadList`, `ExpandTabsBox`, `CloseReviewButton`, `StartUploadButton`. New: `DatasetsPane`, `MembersPane` (BrowserPane), `RefreshButton`, `DatasetToolbar`, `MemberToolbar`, `ReviewToolbar`, `TransferButton` (DropDownButton), `TextModeItem`, `BinaryModeItem`, `TrimItem`, `VerifyItem` (MenuItem). Removed: `TextModeButton`, `BinaryModeButton`, `TrimBox`, `VerifyBox`, `PaddingNote`, `MemberHeader`, `DatasetButtons`.

- [ ] **Step 1: Write the failing tests**

In `MvsmfBrowserWindowTests.cs`:

1. Add `using Avalonia.Controls.Primitives;` and `using LizTerm.App.Controls;`.
2. In `Choosing_a_pds_shows_its_members_and_selection_reaches_the_view_model`, replace the `MemberHeader` line with:

```csharp
        Assert.Equal("MVSCE02.CNTL", Named<BrowserPane>(window, "MembersPane").Title);
        Assert.Equal("3 members · none selected", Named<BrowserPane>(window, "MembersPane").FooterText);
```

3. Replace `Binary_mode_shows_the_padding_note` with:

```csharp
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

        var datasetVerbs = Named<StackPanel>(window, "DatasetToolbar").Children.OfType<Button>().Select(b => b.Content).ToList();
        Assert.Equal(new object?[] { "New…", "Rename…", "Delete…", "↻ Refresh" }, datasetVerbs);
        Assert.Same(t.Vm.RefreshCommand, Named<Button>(window, "RefreshButton").Command);

        var memberVerbs = Named<DockPanel>(window, "MemberToolbar").GetLogicalDescendants().OfType<Button>()
            .Where(b => b is not DropDownButton).Select(b => b.Content).ToList();
        Assert.Equal(new object?[] { "⇣ Download…", "⇡ Upload…", "Rename…", "Delete…" }, memberVerbs);

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
```

4. In `The_upload_review_replaces_the_member_pane`, after the `ReviewPane` visibility assertion add:

```csharp
            Assert.Equal("Upload to MVSCE02.CNTL", Named<BrowserPane>(window, "MembersPane").Title);
            Assert.Equal("1 file", Named<BrowserPane>(window, "MembersPane").FooterText);
            Assert.True(Named<StackPanel>(window, "ReviewToolbar").IsVisible);
            Assert.False(Named<DockPanel>(window, "MemberToolbar").IsVisible);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: the new and changed tests fail (`DatasetsPane` not found and so on); `Each_pane_has_its_toolbar…` fails on the null `TextModeButton` check.

- [ ] **Step 3: Rewrite the window body**

Replace everything from `<DockPanel>` to `</DockPanel>` (the window's content) in `MvsmfBrowserWindow.axaml` with the
following, and add `xmlns:controls="using:LizTerm.App.Controls"` to the `Window` element. The `Window.Styles` block
stays as it is.

```xml
  <DockPanel>
    <Border x:Name="PreviewStrip" DockPanel.Dock="Top" Background="#F5C542" Padding="12,5">
      <StackPanel Orientation="Horizontal" Spacing="6">
        <TextBlock Text="⚠ Feature preview. mvsMF access is new and still being refined." VerticalAlignment="Center" />
        <Button x:Name="GuideLink" Classes="link" Command="{Binding OpenGuideCommand}">
          <TextBlock Text="What to expect…" TextDecorations="Underline" FontWeight="SemiBold" />
        </Button>
      </StackPanel>
    </Border>

    <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto" Margin="12,8">
      <TextBlock Text="Filter" VerticalAlignment="Center" Margin="0,0,8,0" />
      <TextBox Grid.Column="1" x:Name="FilterBox" Text="{Binding Filter}" FontFamily="Menlo, Consolas, monospace"
               PlaceholderText="HLQ.** — for example MVSCE02.**" IsEnabled="{Binding CanChooseDataset}" />
      <Button Grid.Column="2" x:Name="ListButton" Content="List" Margin="8,0,0,0" Command="{Binding ListCommand}" />
    </Grid>

    <Border x:Name="ErrorBanner" DockPanel.Dock="Top" Background="#402020" Padding="12,5" IsVisible="{Binding HasError}">
      <DockPanel>
        <Button x:Name="RetryButton" DockPanel.Dock="Right" Content="Retry" Command="{Binding RetryCommand}"
                IsVisible="{Binding CanRetry}" />
        <TextBlock Text="{Binding ErrorText, StringFormat='✗ {0}'}" TextWrapping="Wrap" VerticalAlignment="Center" />
      </DockPanel>
    </Border>

    <!-- The confirmation strip is unchanged: copy it verbatim from the current file, from
         <Border x:Name="ConfirmationStrip" … to its closing </Border>. -->

    <!-- The bottom bar is only status now (pane-pattern spec §4.3): the verbs are in the panes, the transfer
         options in the Members toolbar's drop-down, and the padding note is a status line. -->
    <Border DockPanel.Dock="Bottom" Background="#1C1E22" Padding="12,6" BorderBrush="#33373D" BorderThickness="0,1,0,0">
      <DockPanel>
        <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" Spacing="8" Margin="8,0,0,0">
          <ProgressBar x:Name="Progress" Width="160" IsIndeterminate="True" IsVisible="{Binding IsBusy}" VerticalAlignment="Center" />
          <Button x:Name="CancelButton" Content="Cancel" Command="{Binding CancelCommand}" IsVisible="{Binding IsBusy}" />
        </StackPanel>
        <TextBlock x:Name="StatusLine" Text="{Binding StatusText}" TextWrapping="Wrap" VerticalAlignment="Center" />
      </DockPanel>
    </Border>

    <Grid ColumnDefinitions="*,4,*" Margin="12,0,12,8">
      <controls:BrowserPane x:Name="DatasetsPane" Grid.Column="0" Title="Datasets" FooterText="{Binding DatasetsFooter}">
        <controls:BrowserPane.Toolbar>
          <StackPanel x:Name="DatasetToolbar" Orientation="Horizontal" Spacing="6">
            <Button x:Name="NewDatasetButton" Classes="pane-verb" Content="New…" Command="{Binding NewDatasetCommand}" />
            <Button x:Name="RenameDatasetButton" Classes="pane-verb" Content="Rename…" Command="{Binding RenameDatasetCommand}" />
            <Button x:Name="DeleteDatasetButton" Classes="pane-verb" Content="Delete…" Command="{Binding DeleteDatasetCommand}" />
            <Button x:Name="RefreshButton" Classes="pane-verb" Content="↻ Refresh" Command="{Binding RefreshCommand}" />
          </StackPanel>
        </controls:BrowserPane.Toolbar>
        <controls:BrowserPane.FooterAction>
          <Button x:Name="LoadMoreDatasetsButton" Classes="pane-verb" Content="Load more" Command="{Binding LoadMoreDatasetsCommand}"
                  IsVisible="{Binding HasMoreDatasets}" />
        </controls:BrowserPane.FooterAction>
        <controls:BrowserPane.Body>
          <DockPanel>
            <Grid DockPanel.Dock="Top" ColumnDefinitions="*,56,56,56" Margin="8,4">
              <TextBlock Classes="header" Text="NAME" />
              <TextBlock Classes="header" Grid.Column="1" Text="DSORG" />
              <TextBlock Classes="header" Grid.Column="2" Text="RECFM" />
              <TextBlock Classes="header" Grid.Column="3" Text="LRECL" />
            </Grid>
            <ListBox x:Name="DatasetList" ItemsSource="{Binding Datasets}" SelectedItem="{Binding SelectedDataset}"
                     SelectionMode="Single" IsEnabled="{Binding CanChooseDataset}">
              <ListBox.ItemTemplate>
                <DataTemplate x:DataType="vm:DatasetRow">
                  <Grid ColumnDefinitions="*,56,56,56" Opacity="{Binding IsSupported, Converter={x:Static vm:MvsmfBrowserConverters.DimUnlessTrue}}">
                    <TextBlock Classes="cell" Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
                    <TextBlock Classes="cell" Grid.Column="1" Text="{Binding Dsorg}" />
                    <TextBlock Classes="cell" Grid.Column="2" Text="{Binding Recfm}" />
                    <TextBlock Classes="cell" Grid.Column="3" Text="{Binding Lrecl}" />
                  </Grid>
                </DataTemplate>
              </ListBox.ItemTemplate>
            </ListBox>
          </DockPanel>
        </controls:BrowserPane.Body>
      </controls:BrowserPane>

      <GridSplitter Grid.Column="1" ResizeDirection="Columns" Background="#33373D" />

      <controls:BrowserPane x:Name="MembersPane" Grid.Column="2" Title="{Binding MembersTitle}" FooterText="{Binding MembersFooter}">
        <controls:BrowserPane.HeaderContent>
          <TextBox x:Name="MemberFilterBox" Width="140" Text="{Binding MemberFilter}" PlaceholderText="Filter members"
                   IsEnabled="{Binding ShowMemberPane}" />
        </controls:BrowserPane.HeaderContent>
        <controls:BrowserPane.Toolbar>
          <Panel>
            <DockPanel x:Name="MemberToolbar" IsVisible="{Binding !IsReviewingUpload}">
              <DropDownButton x:Name="TransferButton" DockPanel.Dock="Right" Classes="pane-verb" Content="{Binding TransferModeLabel}"
                              IsEnabled="{Binding IsIdle}">
                <DropDownButton.Flyout>
                  <MenuFlyout>
                    <MenuItem x:Name="TextModeItem" Header="Text" ToggleType="Radio" GroupName="TransferMode"
                              IsChecked="{Binding IsTextMode, Mode=TwoWay}" />
                    <MenuItem x:Name="BinaryModeItem" Header="Binary" ToggleType="Radio" GroupName="TransferMode"
                              IsChecked="{Binding IsBinaryMode, Mode=TwoWay}" />
                    <Separator />
                    <MenuItem x:Name="TrimItem" Header="Trim trailing blanks" ToggleType="CheckBox"
                              IsChecked="{Binding TrimTrailingBlanks, Mode=TwoWay}" IsEnabled="{Binding IsTextMode}" />
                    <MenuItem x:Name="VerifyItem" Header="Verify after upload" ToggleType="CheckBox"
                              IsChecked="{Binding VerifyUploads, Mode=TwoWay}" IsEnabled="{Binding IsTextMode}" />
                  </MenuFlyout>
                </DropDownButton.Flyout>
              </DropDownButton>
              <StackPanel Orientation="Horizontal" Spacing="6">
                <Button x:Name="DownloadButton" Classes="pane-verb" Content="⇣ Download…" Command="{Binding DownloadCommand}" />
                <Button x:Name="UploadButton" Classes="pane-verb" Content="⇡ Upload…" Command="{Binding UploadCommand}" />
                <Button x:Name="RenameMemberButton" Classes="pane-verb" Content="Rename…" Command="{Binding RenameMemberCommand}" />
                <Button x:Name="DeleteButton" Classes="pane-verb" Content="Delete…" Command="{Binding DeleteCommand}" />
              </StackPanel>
            </DockPanel>
            <StackPanel x:Name="ReviewToolbar" Orientation="Horizontal" Spacing="6" IsVisible="{Binding IsReviewingUpload}">
              <Button x:Name="CloseReviewButton" Classes="pane-verb" Content="Close" Command="{Binding CloseReviewCommand}" />
              <Button x:Name="StartUploadButton" Classes="pane-verb" Content="Upload" Command="{Binding StartUploadCommand}" />
              <CheckBox x:Name="ExpandTabsBox" Content="Expand tabs (every 8 columns)" IsChecked="{Binding ExpandTabs}"
                        IsEnabled="{Binding IsTextMode}" Margin="8,0,0,0" />
            </StackPanel>
          </Panel>
        </controls:BrowserPane.Toolbar>
        <controls:BrowserPane.FooterAction>
          <Button x:Name="LoadMoreMembersButton" Classes="pane-verb" Content="Load more" Command="{Binding LoadMoreMembersCommand}"
                  IsVisible="{Binding HasMoreMembers}" />
        </controls:BrowserPane.FooterAction>
        <controls:BrowserPane.Body>
          <Panel>
            <TextBlock x:Name="ChooseHint" Text="{Binding ChooseHint}" TextWrapping="Wrap" Margin="12"
                       Foreground="#A0A0A0" IsVisible="{Binding ShowChooseHint}" />
            <TextBlock x:Name="SequentialNote" Margin="12" TextWrapping="Wrap"
                       Text="Sequential dataset: Download and Upload act on the dataset itself."
                       IsVisible="{Binding ShowSequentialNote}" />

            <DockPanel x:Name="MemberPane" IsVisible="{Binding ShowMemberPane}">
              <Grid DockPanel.Dock="Top" ColumnDefinitions="110,*" Margin="8,4">
                <TextBlock Classes="header" Text="MEMBER" />
                <TextBlock Classes="header" Grid.Column="1" Text="STATUS" />
              </Grid>
              <ListBox x:Name="MemberList" ItemsSource="{Binding VisibleMembers}" SelectionMode="Multiple"
                       IsEnabled="{Binding IsIdle}">
                <ListBox.ItemTemplate>
                  <DataTemplate x:DataType="vm:MemberRow">
                    <Grid ColumnDefinitions="110,*">
                      <TextBlock Classes="cell" Text="{Binding Name}" />
                      <TextBlock Grid.Column="1" Text="{Binding Status}" TextTrimming="CharacterEllipsis" />
                    </Grid>
                  </DataTemplate>
                </ListBox.ItemTemplate>
              </ListBox>
            </DockPanel>

            <DockPanel x:Name="ReviewPane" IsVisible="{Binding IsReviewingUpload}">
              <TextBlock DockPanel.Dock="Bottom" Text="{Binding ReviewMessage}" Foreground="#FF8080" TextWrapping="Wrap" Margin="8,4"
                         IsVisible="{Binding ReviewMessage, Converter={x:Static ObjectConverters.IsNotNull}}" />
              <ScrollViewer>
                <ItemsControl x:Name="UploadList" ItemsSource="{Binding Uploads}" Margin="8,0">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:UploadRow">
                      <Border BorderBrush="#33373D" BorderThickness="0,0,0,1" Padding="0,6">
                        <StackPanel Spacing="4">
                          <DockPanel>
                            <TextBox DockPanel.Dock="Right" Width="110" Text="{Binding MemberName}" MaxLength="8"
                                     FontFamily="Menlo, Consolas, monospace" IsReadOnly="{Binding Sent}" />
                            <TextBlock Text="{Binding FileName}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                          </DockPanel>
                          <TextBlock Text="{Binding NameProblem}" Foreground="#FF8080"
                                     IsVisible="{Binding NameProblem, Converter={x:Static ObjectConverters.IsNotNull}}" />
                          <TextBlock Text="{Binding Problems}" TextWrapping="Wrap" FontSize="12"
                                     IsVisible="{Binding Problems, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                          <TextBlock Text="{Binding Status}" FontSize="12"
                                     IsVisible="{Binding Status, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                        </StackPanel>
                      </Border>
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </ScrollViewer>
            </DockPanel>
          </Panel>
        </controls:BrowserPane.Body>
      </controls:BrowserPane>
    </Grid>
  </DockPanel>
```

The `ShowMemberPane`, `ShowChooseHint` and `ShowSequentialNote` rules in the view model still read `!IsCreating`;
that is harmless (the dialog is modal) and stays.

In `MvsmfBrowserWindow.axaml.cs`, the class summary becomes:

```csharp
/// <summary>mvsMF Access (browser spec §4, pane-pattern spec §4): two BrowserPanes, each with the verbs that act on
/// its own selection, over a window-level status line. Owned by its session window and shown with ShowAbove; it
/// never refuses to close — closing cancels what runs and releases the connection.</summary>
```

- [ ] **Step 4: Run the window tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: all pass. Known trouble spots:

- `The_column_headers_are_ispf_style_capitals` walks logical descendants: the header `TextBlock`s live inside a
  `BrowserPane`'s `Body`; a `ContentPresenter` keeps its presented control as a logical child, so they are found. If
  not, the test may use `window.GetVisualDescendants()` instead.
- `The_transfer_menu_binds…`: if the items' `IsChecked` is false for every item, the flyout's `DataContext` did not
  reach them. Fix in the window's constructor, not the test: `TransferButton.Flyout!.Opened += (_, _) => { }` is not
  enough; set the items' DataContext explicitly once: after `InitializeComponent()`,
  `((MenuFlyout)TransferButton.Flyout!).Items.OfType<Control>().ToList().ForEach(i => i.Bind(DataContextProperty, this.GetObservable(DataContextProperty)));`
  and record why in a comment.
- `Focus_returns_to_the_member_list_after_a_delete` and the other focus tests rely on `FocusedList()` reading
  `DatasetList` and `MemberList`, which keep their names and still exist in the window's name scope.

- [ ] **Step 5: Run the whole App test project and the zero-warning check**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "mvsMF Access: two BrowserPanes with their own toolbars, the transfer drop-down, and a status-only bottom bar (#17)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Context menus, double-click and shortcuts

**Files:**
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` (context menus)
- Modify: `src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs` (gestures, double-click, keys)
- Test: `tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs`

**Interfaces:**
- Consumes: `this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers` (the pattern at `SessionWindow.axaml.cs:533`); `Gestures.DoubleTappedEvent`.
- Produces: `ContextMenu`s named `DatasetMenu` and `MemberMenu` with items `DatasetMenuNew`, `DatasetMenuRename`, `DatasetMenuDelete`, `DatasetMenuRefresh`, `MemberMenuDownload`, `MemberMenuUpload`, `MemberMenuRename`, `MemberMenuDelete`; `internal KeyGesture RefreshGesture` and `NewDatasetGesture` on the window; tooltips on the verbs.

- [ ] **Step 1: Write the failing tests**

Add to `MvsmfBrowserWindowTests.cs` (`using Avalonia.Input;` and `using Avalonia;` are needed):

```csharp
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

        window.KeyPress(Key.R, raw, PhysicalKey.KeyR, null);
        await Wait.UntilAsync(() => t.Vm.Datasets.Count == 5, "the refreshed listing");

        window.KeyPress(Key.N, raw, PhysicalKey.KeyN, null);
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

        Assert.Equal($"Refresh the list ({window.RefreshGesture})", ToolTip.GetTip(Named<Button>(window, "RefreshButton")));
        Assert.Equal($"Allocate a new dataset ({window.NewDatasetGesture})", ToolTip.GetTip(Named<Button>(window, "NewDatasetButton")));
        Assert.Equal("Download the selected members (Enter)", ToolTip.GetTip(Named<Button>(window, "DownloadButton")));
        Assert.Equal("Delete the selected members (Delete)", ToolTip.GetTip(Named<Button>(window, "DeleteButton")));
    }
```

Check `FakeFilePicker.PickSaveLocationAsync` records its call as `"save:…"` in `Calls` (read the fake); if it records
another prefix, use that. The double-click test's `ContainerFromIndex(1)` is `COMPILE` in the standard seed.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: the five new tests fail (`ContextMenu` null, `RefreshGesture` missing, no double-click handler, Cmd/Ctrl+R does nothing, no tooltips).

- [ ] **Step 3: Add the context menus and tooltips in AXAML**

In `MvsmfBrowserWindow.axaml`:

1. Inside `<ListBox x:Name="DatasetList" …>`, before `<ListBox.ItemTemplate>`, add:

```xml
              <ListBox.ContextMenu>
                <ContextMenu x:Name="DatasetMenu">
                  <MenuItem x:Name="DatasetMenuNew" Header="New…" Command="{Binding NewDatasetCommand}" />
                  <MenuItem x:Name="DatasetMenuRename" Header="Rename…" Command="{Binding RenameDatasetCommand}" />
                  <MenuItem x:Name="DatasetMenuDelete" Header="Delete…" Command="{Binding DeleteDatasetCommand}" InputGesture="Delete" />
                  <Separator />
                  <MenuItem x:Name="DatasetMenuRefresh" Header="Refresh" Command="{Binding RefreshCommand}" />
                </ContextMenu>
              </ListBox.ContextMenu>
```

2. Inside `<ListBox x:Name="MemberList" …>`, before `<ListBox.ItemTemplate>`, add:

```xml
                <ListBox.ContextMenu>
                  <ContextMenu x:Name="MemberMenu">
                    <MenuItem x:Name="MemberMenuDownload" Header="Download…" Command="{Binding DownloadCommand}" InputGesture="Enter" />
                    <MenuItem x:Name="MemberMenuUpload" Header="Upload…" Command="{Binding UploadCommand}" />
                    <MenuItem x:Name="MemberMenuRename" Header="Rename…" Command="{Binding RenameMemberCommand}" />
                    <MenuItem x:Name="MemberMenuDelete" Header="Delete…" Command="{Binding DeleteCommand}" InputGesture="Delete" />
                  </ContextMenu>
                </ListBox.ContextMenu>
```

3. Tooltips on the verbs whose key is fixed (the command-key ones are set in code, since the modifier is the platform's):

```xml
<Button x:Name="DownloadButton" … ToolTip.Tip="Download the selected members (Enter)" />
<Button x:Name="UploadButton" … ToolTip.Tip="Upload files into this dataset" />
<Button x:Name="RenameMemberButton" … ToolTip.Tip="Rename the selected member" />
<Button x:Name="DeleteButton" … ToolTip.Tip="Delete the selected members (Delete)" />
<Button x:Name="RenameDatasetButton" … ToolTip.Tip="Rename the selected dataset" />
<Button x:Name="DeleteDatasetButton" … ToolTip.Tip="Delete the selected dataset (Delete)" />
```

- [ ] **Step 4: Gestures, double-click and keys in the code-behind**

In `MvsmfBrowserWindow.axaml.cs`:

1. Add `using Avalonia.Controls.Platform;` (for `GetPlatformSettings`) and `using LizTerm.App.Keyboard;` if `PlatformHotkeys.Fallback` is used.
2. Add two properties and set them in the constructor, after `InitializeComponent()`:

```csharp
    /// <summary>Cmd+R on macOS, Ctrl+R elsewhere: the platform's command modifier, as the session window's Find.</summary>
    internal KeyGesture RefreshGesture { get; private set; } = new(Key.R, KeyModifiers.Control);

    /// <summary>Cmd+N on macOS, Ctrl+N elsewhere.</summary>
    internal KeyGesture NewDatasetGesture { get; private set; } = new(Key.N, KeyModifiers.Control);
```

```csharp
        var modifiers = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        RefreshGesture = new KeyGesture(Key.R, modifiers);
        NewDatasetGesture = new KeyGesture(Key.N, modifiers);
        DatasetMenuRefresh.InputGesture = RefreshGesture;
        DatasetMenuNew.InputGesture = NewDatasetGesture;
        ToolTip.SetTip(RefreshButton, $"Refresh the list ({RefreshGesture})");
        ToolTip.SetTip(NewDatasetButton, $"Allocate a new dataset ({NewDatasetGesture})");
        MemberList.AddHandler(Gestures.DoubleTappedEvent, OnMemberDoubleTapped);
```

   `DatasetMenuRefresh` and `DatasetMenuNew` are generated fields because they carry `x:Name` in the window's XAML.
   If the generated field is missing (a name inside a `ContextMenu` is registered lazily in some Avalonia versions),
   look them up: `((ContextMenu)DatasetList.ContextMenu!).Items.OfType<MenuItem>().First(i => i.Name == "DatasetMenuRefresh")`.

3. Add the handler:

```csharp
    /// <summary>A double-click on a member row is Download, as Enter is. On a row only: a double-click on the empty
    /// part of the list selects nothing and must download nothing.</summary>
    private void OnMemberDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null) return;
        if (vm.DownloadCommand.CanExecute(null)) _ = vm.DownloadCommand.ExecuteAsync(null);
    }
```

4. In `OnKeyDownTunnel`, add three cases before the `Key.Escape` case:

```csharp
            case Key.R when e.KeyModifiers == RefreshGesture.KeyModifiers:
                e.Handled = true;
                if (vm.RefreshCommand.CanExecute(null)) _ = vm.RefreshCommand.ExecuteAsync(null);
                break;
            case Key.N when e.KeyModifiers == NewDatasetGesture.KeyModifiers:
                e.Handled = true;
                if (vm.NewDatasetCommand.CanExecute(null)) vm.NewDatasetCommand.Execute(null);
                break;
            case Key.Delete or Key.Back when DatasetList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DeleteDatasetCommand.CanExecute(null)) _ = vm.DeleteDatasetCommand.ExecuteAsync(null);
                break;
```

   The existing `Key.Delete or Key.Back when MemberList.IsKeyboardFocusWithin` case stays; the two are disjoint
   because only one list can hold the focus.

- [ ] **Step 5: Run the window tests**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~MvsmfBrowserWindowTests"`
Expected: all pass. If `Double_clicking_a_member_downloads_it` never downloads, the headless pointer did not count a
double tap: add `await Task.Delay(1)` is wrong (it widens the gap); instead check the two presses land on the same
point and that `row` is the `ListBoxItem` (`ContainerFromIndex` returns it). If the `ContextMenu` items have no
`Command` until opened, the test already opens the menu first.

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.App/Views/MvsmfBrowserWindow.axaml src/LizTerm.App/Views/MvsmfBrowserWindow.axaml.cs tests/LizTerm.App.Tests/Views/MvsmfBrowserWindowTests.cs
git commit -m "mvsMF Access: context menus, double-click download, Cmd/Ctrl+R and +N, Delete on a dataset, tooltips (#17)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: The rename everywhere the user sees it

**Files:**
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml:29,197`
- Modify: `src/LizTerm.App/Views/SessionWindow.axaml.cs:54,966,1013`
- Modify: `tests/LizTerm.App.Tests/Views/SessionWindowMvsmfTests.cs:46`
- Modify: `src/LizTerm.App/Views/ProfileEditorWindow.axaml:289`
- Modify: `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs:555`
- Modify: `tests/LizTerm.App.Tests/ViewModels/ProfileEditorMvsmfTests.cs:182`
- Modify: `src/LizTerm.App/Dialogs/AvaloniaCredentialPrompt.cs:11`, `src/LizTerm.App/Dialogs/ModalDialogs.cs:15`, `src/LizTerm.App/ViewModels/SessionViewModel.cs:432`, `src/LizTerm.App/ViewModels/MvsmfBrowserViewModel.cs:14`, `tests/LizTerm.App.Tests/Files/FakeFilePickerTests.cs:9` (comments)

**Interfaces:**
- Consumes: nothing new.
- Produces: the menu item header `mvsMF _Access...` (both menus); the native lookup key `"mvsMF _Access..."`.

- [ ] **Step 1: Write the failing test**

In `SessionWindowMvsmfTests.cs:46`, change the lookup to `"mvsMF _Access..."`. Add one assertion to the test that
checks the menu item (the one that reads `Classic(window)`; if none asserts the header, add to the first test in the
file):

```csharp
        Assert.Equal("mvsMF _Access...", Classic(window).Header);
```

In `ProfileEditorMvsmfTests.cs:182`, change the expected message to
`"✗ The host's certificate is not trusted. Open mvsMF Access from a session to review it."`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~SessionWindowMvsmfTests|FullyQualifiedName~ProfileEditorMvsmfTests"`
Expected: the native lookup returns null (fails) and the message assertion fails.

- [ ] **Step 3: Rename**

1. `SessionWindow.axaml:29`: `Header="mvsMF _Access..."`; line 197: the same on `MvsmfBrowserMenuItem`.
2. `SessionWindow.axaml.cs:54`: `"mvsMF _Access..."`; line 966 comment: `The open mvsMF Access window, if any`; line 1013: `"Could not open mvsMF Access: " + ex.Message`.
3. `ProfileEditorWindow.axaml:289`: `Text="The z/OSMF REST address for File &gt; mvsMF Access (a feature preview). /zosmf is added when the path is empty; use https:// through a TLS proxy."`.
4. `ProfileEditorViewModel.cs:555`: `"✗ The host's certificate is not trusted. Open mvsMF Access from a session to review it."`.
5. The five comments: replace "the mvsMF Browser" with "mvsMF Access" (`AvaloniaCredentialPrompt.cs:11`, `ModalDialogs.cs:15`, `SessionViewModel.cs:432`, `MvsmfBrowserViewModel.cs:14`, `FakeFilePickerTests.cs:9`).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/LizTerm.App.Tests`
Expected: all pass.

Run: `grep -rn "mvsMF Browser" src tests` → no output.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "Rename the mvsMF Browser to mvsMF Access in every user-facing string (#17)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Docs, changelog, notes and the spec amendments

**Files:**
- Modify: `docs/user-guide.md` (lines 15, 54, 312–372, 634)
- Regenerate: `src/LizTerm.App/Assets/Docs/user-guide.html`
- Modify: `CHANGELOG.md` (the Unreleased mvsMF entry, lines 25–31)
- Modify: `README.md:137`
- Modify: `src/LizTerm.App/CLAUDE.md` (the "## mvsMF Browser" section, lines 828 and 886–985)
- Modify: `docs/superpowers/specs/2026-09-19-mvsmf-access-pane-pattern-design.md` (§4 table, §6 path)

- [ ] **Step 1: The user guide**

In `docs/user-guide.md`:

1. Line 15: `- [mvsMF Access (preview)](#mvsmf-access-preview)`.
2. Line 54: `(the address and userid for mvsMF Access, a feature preview)`.
3. Line 312: `## mvsMF Access (preview)`.
4. Lines 319–322: replace the paragraph with:

```markdown
[mvsMF](https://github.com/mvslovers/mvsmf) is a z/OSMF-style REST server for MVS 3.8j. It ships with TK5 and can be
installed on TK4- or a hand-rolled MVS 3.8. **mvsMF Access** uses it to list datasets and members, download and
upload them, and create, rename and delete them, without typing anything on the 3270 screen. It is a second way to
move files alongside [IND$FILE](#file-transfer-indfile), and it doesn't need the session to be logged on, or even
connected.
```

5. Line 335: `Once a profile has a URL, its session window has **File > mvsMF Access...**.` (rest of the paragraph unchanged; "the open browser" becomes "the open window").
6. Replace the `### Browsing` section's first two paragraphs (from "Type a dataset pattern" to "act on the dataset itself.") with:

```markdown
Type a dataset pattern in **Filter**, such as `MVSCE02.**`, and choose **List**. The window has two panes, each
with a toolbar of the actions that apply to what is selected in it, and a status line at the bottom.

The **Datasets** pane on the left lists the matching datasets with their **NAME**, **DSORG**, **RECFM** and
**LRECL**. Its toolbar has **New…**, **Rename…**, **Delete…** and **Refresh** (which lists the filter again and keeps
the dataset you had chosen). VSAM and direct-access (`DA`) datasets are listed as **(not supported)**: nothing can be
downloaded from or uploaded to them. The footer says how many datasets are listed and whether one is selected.

Choosing a partitioned dataset (a PDS) lists its members in the pane on the right, titled with the dataset's name,
where **Filter members** narrows the list as you type and you can select several members at once. In the filter,
`*` stands for any run of characters and `%` for exactly one, so `IEF*` keeps the members starting with IEF. Its
toolbar has **Download…**, **Upload…**, **Rename…** and **Delete…**, and a **Transfer** drop-down that shows the
current mode (**Text** or **Binary**) and holds **Trim trailing blanks** and **Verify after upload**. The footer says
how many members are shown and how many are selected. For a sequential dataset, **Download…** and **Upload…** act on
the dataset itself.

Each list also has a right-click menu with the same actions, and these keys: **Enter** or a double-click on a
member downloads it, **Delete** deletes the selected members (or, in the Datasets pane, the selected dataset),
**Cmd+R** (macOS) or **Ctrl+R** refreshes, **Cmd+N** or **Ctrl+N** opens **New dataset**, and **Escape** cancels a
question, then a running operation, then closes the window.
```

7. Where the guide describes the member header's plus (`the member header shows a plus (**500+ members**)`), change to `the Members pane's footer shows a plus (**500+ members**)`.
8. Where the guide describes the New dataset form (search `New…` in the section), say it opens a **New dataset** window rather than a pane, and that **Cancel** or **Escape** closes it.
9. Line 634: `- **mvsMF Access is a preview**`.
10. Search the section for any remaining "browser" that names the window and replace with "the window" or "mvsMF Access".

Regenerate the bundled copy:

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
```

Expected: pass, and `git status` shows `src/LizTerm.App/Assets/Docs/user-guide.html` modified. If
`The_guide_uses_no_construct_the_converter_cannot_render` fails, the new text used a Markdown construct the converter
lacks; reword rather than extend the converter.

- [ ] **Step 2: Changelog and README**

In `CHANGELOG.md`, replace the Unreleased entry at lines 25–31 with:

```markdown
- **mvsMF Access (feature preview).** On MVS 3.8j hosts running [mvsMF](https://github.com/mvslovers/mvsmf) 1.1.0
  or later (it ships with TK5 and installs on TK4- or a hand-rolled MVS 3.8), a second way to move files beside
  IND$FILE: sign in once per session window, then list, download, upload and delete. Long lists arrive 500 entries
  at a time with **Load more** for the rest, and on such a library the member filter is the host's work. It can
  create, rename and delete datasets and members, and warns before replacing a member that changed on the host
  since you downloaded it. The window has a **Datasets** pane and a **Members** pane, each with a toolbar of its own
  actions and a footer that counts what is listed and selected; **Refresh** lists again and keeps your place; the
  transfer mode and options sit in a **Transfer** drop-down; **New dataset** is its own window; each list has a
  right-click menu, a double-click on a member downloads it, and **Cmd/Ctrl+R** and **Cmd/Ctrl+N** refresh and
  create. It is not yet complete and there are likely bugs. See the user guide's
  [mvsMF Access section](https://github.com/coffeemuse/LizTerm/blob/main/docs/user-guide.md#mvsmf-access-preview).
```

`README.md:137`: `the mvsMF Browser` becomes `mvsMF Access`.

- [ ] **Step 3: The App notes**

In `src/LizTerm.App/CLAUDE.md`:

1. Line 828: `## mvsMF Access`.
2. Line 886: `- **File > mvsMF Access...** (`mvsMF _Access...`, no shortcut: the menu rule)`.
3. In the bullet beginning `- **Create** (`Create.cs`, `NewDatasetFormViewModel`)`, replace the sentence that starts "While the form is open every other operation is off" through "the sequential note." with:

```markdown
  While the form is open every other operation is off (`!IsCreating` in each rule). The form is `NewDatasetWindow`,
  an owned modal dialog over the browser window, bound to the same view model: the window opens it when
  `IsCreating` turns on (`ShowNewDatasetAsync`, through `ShowDialogAbove`) and the dialog closes itself when
  `IsCreating` turns off; its close box is `CloseFormCommand`, executed from a posted callback because the
  notification would otherwise close a window still inside `Closing`. A refused create leaves `IsCreating` on and
  the dialog open with `Form.Message`.
```

4. Add a bullet before the `- The window pushes the member selection` bullet:

```markdown
- **The panes** (pane-pattern spec §4): `MvsmfBrowserWindow` is two `Controls/BrowserPane`s, a `UserControl` with
  six slots (`Title`, `HeaderContent`, `Toolbar`, `Body`, `FooterText`, `FooterAction`) that owns the frame and the
  `pane-verb` button style and binds only to its own properties; the window's controls in the slots bind to the
  view model as usual. A pane's toolbar, its list's `ContextMenu`, the window's keys and a double-click all bind
  the same command instance (`The_context_menus_bind_the_same_commands_as_the_toolbars` pins it), so a verb has one
  code path. The strings are the view model's: `MembersTitle` (the dataset's name, the review's header, or
  "Members"), `DatasetsFooter` and `MembersFooter` (count, a plus while the host has more, "n of m" under a local
  filter, "matching" under a host one, and the selection), `TransferModeLabel` for the `DropDownButton` whose
  `MenuFlyout` holds the mode radios and the two upload check items. `RefreshCommand` lists the filter again and
  keeps the chosen dataset through `SelectAsync`, or says it is no longer listed. Choosing Binary on a
  fixed-length dataset puts `PaddingNote` on the status line (`OnModeChanged`); a dataset's own choice of mode is
  followed by its member listing, which sets the status line itself. The window's key shortcuts use the platform's
  command modifier (`RefreshGesture`, `NewDatasetGesture`), as the session window's Find does. A USS browser is two
  more panes in the same frame, on a `TabControl` that arrives with it (spec §11).
```

5. Search the section for "browser" as a name for the window and prefer "the window" or "mvsMF Access"; the type names stay.

- [ ] **Step 4: Spec amendments**

In `docs/superpowers/specs/2026-09-19-mvsmf-access-pane-pattern-design.md`:

1. §4 table: `| `Content` | `object?` | Column header plus list, filling the pane |` becomes
   `| `Body` | `object?` | Column header plus list, filling the pane (a UserControl's own Content is its XAML root) |`,
   and in §4 prose "swaps `Content` and `Toolbar`" becomes "swaps `Body` and `Toolbar`".
2. §6: "`NewDatasetWindow` under `src/LizTerm.App/Dialogs`" becomes "`NewDatasetWindow` under `src/LizTerm.App/Views`, beside every other window (`Dialogs` holds prompt interfaces and adapters)".
3. §9 first bullet: "checks each lands in the tree" stays; add "and the `pane-verb` style applies inside the pane".

- [ ] **Step 5: Verify**

Run: `dotnet test LizTerm.slnx`
Expected: all pass (the live-host tests skip themselves; `UserGuideAssetTests` passes against the regenerated HTML; `RepositoryHeadersTests` passes).

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`
Expected: `0`.

Run: `grep -rn "mvsMF Browser\|mvsmf-browser-preview" README.md CHANGELOG.md docs/user-guide.md src tests` → no output (older changelog sections and `docs/superpowers` may still say it; they are history).

- [ ] **Step 6: Commit**

```bash
git add docs/user-guide.md src/LizTerm.App/Assets/Docs/user-guide.html CHANGELOG.md README.md src/LizTerm.App/CLAUDE.md docs/superpowers/specs/2026-09-19-mvsmf-access-pane-pattern-design.md
git commit -m "mvsMF Access: user guide, changelog, App notes and the two spec amendments (#17)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Hands-on pass and the PR

**Files:** none.

- [ ] **Step 1: Run the app against MVS/CE and walk the checks**

Follow the isolated-HOME recipe in the live-test-host memory (`docs/development.md` has the environment variables).
Open a profile with an mvsMF URL, then **File > mvsMF Access…**, and check:

1. The window title reads `mvsMF Access — <profile> (Preview)`; the menu item reads mvsMF Access….
2. Two bordered panes with titles; the Datasets toolbar reads New… Rename… Delete… ↻ Refresh; the Members toolbar reads ⇣ Download… ⇡ Upload… Rename… Delete… and `Transfer: Text` at the right.
3. The `Transfer` drop-down opens a menu with Text / Binary radios and the two check items; choosing Binary on an FB dataset puts the padding note on the status line and the button reads `Transfer: Binary`.
4. Right-click on a dataset row and on a member row shows the menus, with Cmd+N and Cmd+R shown on the dataset menu (macOS) and Enter / Delete on the member items.
5. Double-clicking a member opens the save dialog.
6. Cmd+R refreshes and keeps the selected dataset; Cmd+N opens **New dataset** centred over the window; Escape closes it; a refused create keeps it open with the message; a successful create closes it and selects the new dataset.
7. The footer of each pane counts correctly, including `n selected` when several members are selected and `500+` on a long library.
8. Keep on Top on the session window: the New dataset dialog stays above the browser window.
9. Nothing in the window or the guide names MVS/CE as the only host.

Record what was found; anything not right is a fix in the task that owns it, then re-run that task's tests.

- [ ] **Step 2: Open the PR**

```bash
git push -u origin claude/mvsmf-browser-ui-polish-1717ae
gh pr create --title "mvsMF Access: the pane pattern and a polish pass on the dataset browser (#17)" --body "$(cat <<'EOF'
Implements docs/superpowers/specs/2026-09-19-mvsmf-access-pane-pattern-design.md.

- `BrowserPane`, a shared frame (title row, toolbar, body, footer) that the Datasets and Members panes are two uses of and the USS browser will reuse
- Verbs move into each pane's toolbar; the bottom bar is only status, progress and Cancel
- Transfer mode and the upload options move into a `Transfer: Text|Binary` drop-down
- New dataset is an owned modal dialog bound to the same view model
- Refresh (keeps the chosen dataset), context menus, double-click download, Cmd/Ctrl+R and +N, Delete on a dataset, tooltips
- The window is named mvsMF Access; the guide says mvsMF ships with TK5 and installs on TK4- and hand-rolled MVS 3.8

Hands-on pass against MVS/CE: see the checklist in the plan's Task 8.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

If the pulls API answers a 5xx on the body, create with a one-line body and then `PATCH /repos/coffeemuse/LizTerm/issues/<n>` with the full text (the workaround recorded in memory).

---

## Self-review

**Spec coverage.** §3 decisions: name (Task 6, 7), verbs above lists (4), text buttons with three marks (4), shared
control with per-pane view models (1, 4), settings in a drop-down (4), dialog for the form and titled review (3, 4),
one code path per verb (5), tabs deferred (nothing to build). §4 slots and the two panes' contents (1, 2, 4), §4.3
the bar (4). §5 drop-down (4, label in 2). §6 dialog (3). §7 menus, double-click, keys, tooltips (5). §8 unchanged
surfaces: no task touches them. §9 tests: control (1), view model (2), window (3, 4, 5), headers (files carry them),
hands-on (8). §10 docs (7). §11 recorded in the spec and the App notes (7). §12 one PR (8).

**Placeholders.** None: every step carries its code or its exact edit. Two steps name a fallback if a headless
behaviour differs (the flyout's DataContext, the double-tap count); each fallback is itself concrete.

**Type consistency.** `Body` (not `Content`) in Tasks 1, 4, 7. `MembersTitle`, `DatasetsFooter`, `MembersFooter`,
`TransferModeLabel`, `PaddingNote`, `RefreshCommand` defined in Task 2 and used in 4, 5, 7. `NewDatasetDialog`,
`RefreshGesture`, `NewDatasetGesture` defined on the window in Tasks 3 and 5 and used in the tests of 3 and 5. Named
controls listed in Task 4's Interfaces match the AXAML and the tests. `MembersHeader` is removed in Task 2 and its
last reference (the window test) is rewritten in Task 4.
