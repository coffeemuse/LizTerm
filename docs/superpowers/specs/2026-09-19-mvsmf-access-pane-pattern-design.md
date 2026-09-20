# LizTerm: mvsMF Access — the pane pattern and a polish pass on the dataset browser

Design agreed 2026-09-19. It follows the dataset browser spec of 2026-09-16
(`2026-09-16-lizterm-mvsmf-dataset-browser-design.md`, "the browser spec" below), the token-auth spec of 2026-09-18
and the manage-slice spec of 2026-09-19. It restructures the browser window's controls without changing what the
window can do, and it sets the shape a USS browser will copy.

## 1. Purpose

The browser lists, downloads, uploads, creates, renames and deletes, and every one of those works. Its controls are
where each slice left them: New, Rename and Delete for datasets sit under the dataset list; Download, Upload, Rename
and Delete for members sit in the window's bottom bar between the transfer options; the new-dataset form takes over
the member pane; there is no refresh, no context menu, no double-click, and the window is named after one half of
what it will hold. This pass:

1. gives each pane a **toolbar** of the verbs that act on its selection, a **title row** and a **footer** that says
   what a bulk verb will hit, all laid out by one shared **pane control** (§4);
2. moves the transfer options into a **drop-down button** that shows the current mode (§5);
3. moves the new-dataset form into an **owned modal dialog** so the two panes always mean the same thing (§6);
4. adds **context menus**, **double-click** and **shortcuts** bound to the same commands as the toolbars (§7);
5. renames the window to **mvsMF Access** (§3) and adds **Refresh**.

It changes no host call, no transfer path and no view-model behaviour beyond a refresh command and the footer strings.
USS, tabs, jobs and console stay on #17's list (§10), but §11 records how they slot in.

## 2. What exists

`src/LizTerm.App/Views/MvsmfBrowserWindow.axaml` is a `DockPanel`: preview strip and filter row docked top, the
error banner below them, the confirmation strip and the action/status bar docked bottom, and a two-column grid with a
`GridSplitter` filling the middle. The left column is the dataset list with its column header, a Load more button
and a `WrapPanel` of New…, Rename…, Delete…. The right column is a `Panel` of four `IsVisible`-switched overlays:
the choose hint, the sequential note, the member pane (header, filter box, list, Load more), the upload review pane
and the create pane. The bottom bar holds the Text/Binary radios, Trim and Verify check boxes, Download…, Upload…,
Rename…, Delete…, Cancel, the binary padding note, the status line and the indeterminate progress bar.

`MvsmfBrowserViewModel` is one partial class split by concern (core, Paging, Downloads, Uploads, Delete, Manage,
Create) with `NewDatasetFormViewModel` and `ConfirmationRequest` beside it. Every host call goes through
`HostFileConnection.RunAsync`. Nothing in the window anticipates a second browser; on the Core side `HostPathKind`
already leaves room for a USS kind, as the browser spec §3.1 says.

## 3. Decisions

- **The window is "mvsMF Access".** "Host Files" was considered and rejected: a dataset is not a file, and a PDS
  member is not a file, and the first tab would carry a wrong name forever. Naming the window after the server is
  accurate wherever mvsMF is installed (it ships with TK5 and installs on TK4- and hand-rolled MVS 3.8), explains why
  the menu item appears only when a profile has an mvsMF URL, scopes the preview strip and the guide, and stretches
  to jobs and console where "Browser" would not. Type and file names keep their `MvsmfBrowser*` names; only
  user-facing text changes. No user-facing text may imply MVS/CE is the only host.
- **Verbs live above the list they act on.** A pane's toolbar holds exactly the verbs that act on that pane's
  selection. Window-level actions (List, Cancel, Retry, the guide link) stay on the window.
- **Text buttons with a few Unicode marks, no icon package.** ↻ on Refresh, ⇣ on Download, ⇡ on Upload; every
  other verb is its label alone. No dependency, identical on all three platforms, and no meaning carried by a glyph
  alone, the same rule the status line follows.
- **One shared pane control, per-pane view models.** The control owns the frame; each pane keeps its own commands,
  columns and rows. A generic pane view model was rejected because datasets, members, directories and files share
  no columns and no verbs.
- **Settings are not verbs.** Text/Binary, Trim and Verify move into a drop-down button whose label shows the mode.
  Persistent toggles were the fallback if the drop-down feels hidden in use.
- **A form is a dialog.** New dataset opens an owned modal window, as the profile editor and Preferences do. The
  upload review stays in-window because it is a working surface read while the upload runs and it benefits from the
  width; it gets the pane's title and toolbar so the swap is obviously deliberate.
- **One code path per verb.** Toolbar button, context-menu item, shortcut and double-click all bind to the same
  command instance. A test asserts it.
- **Tabs arrive with the second tab.** A `TabControl` with one tab is dead chrome; the window grows tabs when USS
  lands (§11).

## 4. The pane control

`BrowserPane` is a `UserControl` in `src/LizTerm.App/Controls`, next to `Keypad` and `SessionSwitcher`, in their
AXAML plus code-behind shape. It has no view model, knows nothing about hosts, and exposes five styled properties:

| Property | Type | Slot |
|---|---|---|
| `Title` | `string` | Title row, left |
| `HeaderContent` | `object?` | Title row, right (the member filter box) |
| `Toolbar` | `object?` | The verb row under the title |
| `Body` | `object?` | Column header plus list, filling the pane (a UserControl's own Content is its XAML root) |
| `FooterText` | `string` | Footer, left ("41 datasets · 1 selected") |
| `FooterAction` | `object?` | Footer, right (the Load more button); the footer's right cell collapses when null |

The control owns the borders, spacing, the title-row typography, the toolbar's button style (one `Classes="pane-verb"`
style for compact buttons) and the footer's secondary colour. A caller that needs the list replaced (the upload
review) swaps `Body` and `Toolbar` on its own bindings; the frame does not change.

The layout, top to bottom: title row · toolbar · content · footer, each separated by the same hairline the window's
other strips use. The window's two panes and the future USS panes are four uses of this one control.

### 4.1 The Datasets pane

- `Title`: "Datasets".
- `Toolbar`: New…, Rename…, Delete…, ↻ Refresh. New is always enabled; Rename and Delete follow the existing
  can-execute rules; Refresh re-runs the current filter (`ListCommand` with the current text) and is the only new
  verb in this pass.
- `Content`: the existing column header (NAME, DSORG, RECFM, LRECL) and `DatasetList`, unchanged, dimming
  unsupported rows as today.
- `FooterText`: "<n> datasets · <m> selected", "1 selected" and "no selection" as the cases require; the count comes
  from the paging state.
- `FooterAction`: the existing Load more button, visible when `HasMoreDatasets`.

### 4.2 The Members pane

- `Title`: the selected dataset's name; "Members" when nothing is selected.
- `HeaderContent`: the member filter box.
- `Toolbar`: ⇣ Download…, ⇡ Upload…, Rename…, Delete…, and at the far right the transfer drop-down (§5).
- `Content`: the existing column header (MEMBER, STATUS) and `MemberList`. When nothing is selected the list is
  empty and its placeholder is the choose hint ("Choose a dataset on the left." or the cannot-be-opened text). A
  sequential dataset shows the same pane with an empty list whose placeholder is the sequential note, so the pane
  never disappears. While an upload review is open, `Content` is the review list, `Title` is "Upload to <dataset>"
  and the verbs give way to Close and Upload plus the Expand tabs check box; the transfer drop-down stays at the
  toolbar's right end in both states, because the mode can be changed while a review is open and the queued files
  are re-checked; the frame stays.
- `FooterText`: "<n> members · <m> selected"; during a review, "<n> files".
- `FooterAction`: the existing Load more button, visible when `HasMoreMembers`.

### 4.3 The window around them

The `DockPanel` order stays: preview strip, filter row, error banner docked top; confirmation strip and status bar
docked bottom; the pane grid fills. The status bar becomes the status line, the indeterminate progress bar and
Cancel (visible while busy, since cancel is window-level). The binary padding note leaves the bar and becomes the
status line's text when Binary is chosen on a fixed-record dataset. The right column's overlays shrink from five to
the one `BrowserPane` whose content the view model switches.

## 5. The transfer drop-down

A `DropDownButton` at the right end of the member toolbar, labelled "Transfer: Text" or "Transfer: Binary", opens a
`MenuFlyout` of:

- Text and Binary as radio items;
- a separator;
- Trim trailing blanks and Verify after upload as check items.

The items bind to the properties the view model already has (`IsTextMode`, `IsBinaryMode`, `TrimTrailingBlanks`,
`VerifyUploads`), so the view models and their tests do not change. The label is a bound string
(`TransferModeLabel`) so a test can assert it follows the mode. The same control, unchanged, serves the USS file pane.

## 6. The new-dataset dialog

`NewDatasetWindow` under `src/LizTerm.App/Views`, beside every other window (`Dialogs` holds prompt interfaces and
adapters), owned by the browser window and shown modally. It binds the existing `NewDatasetFormViewModel`: the same
fields (name, type, RECFM, LRECL, BLKSIZE, space unit, primary, secondary, directory blocks), the same per-field
problem lines and message. Create runs the allocation through the
view model as today and closes on success; a failure keeps the dialog open with the message shown; Escape and Cancel
close it without allocating. The create pane and `IsCreating` leave the window.

## 7. Context menus, double-click and keys

- Each list has a `ContextMenu` whose items bind to the same command instances as that pane's toolbar, in the same
  order, with the shortcut shown as the item's input gesture text.
- Double-clicking a member runs Download, matching Enter. Double-clicking a dataset does nothing new; selection
  already loads its members.
- Keys, all local to this window (the session window's menu-gesture rule is not touched): Cmd/Ctrl+R Refresh,
  Cmd/Ctrl+N New dataset, Delete and Backspace delete in whichever list has focus, Enter downloads in the member
  list. Escape keeps its order: cancel a question, then cancel a running operation, then close. Tooltips on the
  toolbar buttons name the shortcut.

## 8. Unchanged

The status-line marks (✓ ✗ ⚠ ⟳ –), the red connection-failure banner with Retry, the confirmation strip with its
input box and "Apply to all", sign-in and certificate handling, paging, ETag handling and every transfer path.

## 9. Testing

- `tests/LizTerm.App.Tests/Controls`: a headless test instantiates a `BrowserPane`, fills the slots and checks each
  lands in the tree and the `pane-verb` style applies inside the pane, and that the footer's right cell collapses
  when `FooterAction` is null.
- View-model tests gain the refresh command (re-runs the current filter, disabled while busy) and the footer strings
  for the count and selection cases.
- A headless window test checks that every toolbar verb and every context-menu item share a command instance, that
  double-click on a member invokes Download, that the transfer label follows the mode, and that the dialog binds and
  closes on success and stays open on failure.
- `RepositoryHeadersTests` covers the new files.
- A hands-on pass by Robert against MVS/CE for what headless tests cannot see: the flyout, the dialog's ownership and
  centring, and the context menus on macOS.

## 10. Docs and out of scope

- `docs/user-guide.md`: the section is renamed, describes the two panes and their toolbars, lists the shortcuts, and
  says mvsMF ships with TK5 and installs on TK4- and hand-rolled MVS 3.8. The in-app guide the preview strip's link
  opens gets the same name and sentence. The profile editor's mvsMF field text is checked for the same.
- `CHANGELOG.md` under Unreleased: the rename, the toolbars, Refresh, the drop-down, the dialog, the context menus
  and shortcuts.
- `src/LizTerm.App/CLAUDE.md`: a paragraph on `BrowserPane` and the rule that a pane's toolbar and context menu bind
  to the same commands.
- The 2026-09-16 browser spec is history and stays as written.
- Out of scope: USS, tabs, jobs, console, copy, Sign Out, in-app viewing of member text, a determinate progress bar,
  and the rest of #17's parked list.

## 11. How USS slots in (recorded, not built)

When USS lands the window gains a `TabControl` with Datasets and USS tabs. The filter row moves inside the Datasets
tab; USS has a path bar instead. The USS tab hosts two `BrowserPane` instances, directories on the left and files on
the right, each with its own view model and toolbar (New…, Rename…, Delete…, ↻ Refresh over directories; ⇣ Download…,
⇡ Upload…, Rename…, Delete… and the transfer drop-down over files). The status line, progress, Cancel, the
confirmation strip and the banner stay on the window and serve both tabs. `HostPathKind` gains a USS kind on the Core
side. The pane control needs nothing new for it.

## 12. Delivery

One PR on `claude/mvsmf-browser-ui-polish-1717ae`, test-first, the zero-warning check before it is called done, then
Robert's hands-on pass.
