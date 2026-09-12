# LizTerm: Help menu links, and the bundled offline user guide

Date: 2026-09-12. Parent spec: `2026-09-03-lizterm-v1-design.md`. Slice of
`2026-09-12-lizterm-v050-release-scope.md` §4.1. Issue: #48.
Status: approved in discussion on 2026-09-12.

## 1. Purpose

`docs/user-guide.md` exists and is complete enough to be the manual. It reaches nobody who has installed
LizTerm: the Help menu offers a wire-log toggle, a folder, and About. A user who wants to know how to send PA1
has to find the project on GitHub, which assumes they know the project is on GitHub and that they have a
network.

This spec ships that guide with the application, as HTML, opened in the user's own browser. It also adds the
three project links issue #48 called phase 1.

It is the last feature item in 0.5.0 and the release's stated schedule risk. §11 records the reduction that
stays available if it turns out to be larger than this spec expects.

## 2. The four questions, and what closes them

Release scope §4.1 names four open questions and calls them the reason this item is the long pole. Three of
them are closed by one decision — §3 — and the fourth stops existing as a consequence.

| Question | Answer |
|---|---|
| Which files ship | `docs/user-guide.md` alone. §5. |
| What format | HTML, generated from the Markdown, one self-contained file. §4. |
| Where it lands in each of the six packages | Inside the assembly. No per-format work at all. §3. |
| How the app locates it, and what it does when absent | `AssetLoader` at a fixed `avares://` URI. It is never absent. §3. |

**A correction to §4.1's reasoning, which is left standing in that document as the record.** It argues from
`PublishSingleFile` leaving loose content files beside the executable. `PublishSingleFile` is not enabled
anywhere in this repository — not in `Directory.Build.props`, not in `LizTerm.App.csproj`, not in
`release.yml`, whose six publish steps pass only `-c Release -r <rid> --self-contained -p:Version=<v> -o`. The
publish output is an ordinary directory. That makes loose files *easier* than §4.1 assumed, not harder, and
this spec still does not use them, for the reasons in §3.

## 3. Delivery: an embedded resource, exactly as LICENSE already is

The generated HTML is committed at `src/LizTerm.App/Assets/Docs/user-guide.html` and picked up by the existing
`<AvaloniaResource Include="Assets\**" />` glob in `LizTerm.App.csproj`. No new item group; the file simply
lands where the glob already looks.

This is not a new pattern in this project. `LizTerm.App.csproj` already embeds `LICENSE` and
`THIRD-PARTY-NOTICES.txt`, and the comment beside them states the reasoning that applies here unchanged: *no
archive or installer this pipeline produces carries a LICENSE file beside the app*. `AppLicense.All` reads
them through `AssetLoader.Open` at an `avares://` URI. The user guide follows that path exactly.

What this buys, against the alternative of a loose file beside the executable:

- **Packaging becomes a non-question.** A resource compiled into `LizTerm.App.dll` travels wherever the
  assembly travels: the macOS `.app` bundle, the `.deb`, the `.rpm`, the Windows installer and all three ZIPs,
  with nothing added to `LizTerm.parcel` and nothing to verify per format. §7 of the release scope — the
  cross-platform pass — gains no new item because of this feature.
- **The absent case is designed out rather than handled.** A loose file is missing under `dotnet run` from a
  source tree, which is the normal development case and the one every headless test runs in. An embedded
  resource is present in every build that compiled, including tests. There is no fallback path to write, and
  no fallback path to forget to test.

The cost is one extraction step: a browser cannot read an `avares://` URI, so the bytes are written to a file
first. §6 covers it. This is a small, testable, wholly-owned piece of work, traded against per-format
packaging verification across five package formats and three operating systems. It is the right trade at this
point in the release.

## 4. The converter

### 4.1 It lives in the test project and never ships

The application does not convert anything. It reads an embedded resource. The Markdown-to-HTML converter is
therefore ordinary C# in `tests/LizTerm.App.Tests/`, is never referenced by `LizTerm.App`, and adds nothing to
the shipped binary — no dead code, no package, no dependency. The dependency rule is untouched: `LizTerm.Core`
is not involved, `LizTerm.Backend.B3270` is not involved, and `LizTerm.App` gains one interface and one
resource.

There is no build-time execution. Nothing runs during `dotnet build`, nothing is added to `release.yml`, and
the converter never runs in a publish job — which is the specific cost §4.1 worried about when it weighed
HTML against raw Markdown.

### 4.2 The generated file is committed, and a test holds it to the source

`src/LizTerm.App/Assets/Docs/user-guide.html` is a committed artifact. One test converts `docs/user-guide.md`
and asserts the result equals the committed file. Editing the guide without regenerating fails the suite.

**Both sides are compared with line endings normalized to `\n`, and the converter normalizes its input the
same way.** This repository has no `.gitattributes`, and the full suite runs on Windows as well as macOS and
Linux — `engines.yml`'s `windows-latest` job builds and tests `LizTerm.slnx` at line 502. A checkout that
converts line endings would otherwise fail this test on that runner alone, for a reason that has nothing to do
with the document. Normalizing in the converter and the comparison is self-contained and holds whatever a
contributor's Git is configured to do; it needs no repository-wide `.gitattributes` and no per-developer
setting.

This is the pattern `RepositoryHeadersTests` already establishes: a test that polices an invariant about the
tree rather than about a running program. It makes the generated file reviewable in a pull request diff, and
it moves the failure from "a user reads a stale page offline" to "CI is red".

Regeneration is an update mode on that same test, enabled by `LIZTERM_UPDATE_DOCS=1`, following the
`LIZTERM_B3270_PATH` and `LIZTERM_TEST_PASSWORD` convention already in use. `docs/development.md` documents
the command.

### 4.3 The supported subset is closed, and measured

This is not a Markdown implementation. It is a converter for one document, whose constructs were counted
rather than guessed. `docs/user-guide.md` as at 2026-09-12 contains:

| Construct | Count |
|---|---|
| `#` headings | 1 |
| `##` headings | 13 |
| `###` headings | 2 |
| Unordered list items, one level, never nested | 41 |
| Table rows | 42, no escaped pipes |
| Fenced code blocks | 1, tagged `text` |
| Lines carrying bold | 57 |
| Lines carrying italic | 4, one inside a table cell |
| Lines carrying inline code | 22 |
| Links | 12 internal anchors, 2 relative, several absolute |

It contains no ordered lists, no blockquotes, no horizontal rules, no nested lists, no reference links, no
images and no raw HTML.

**A test asserts that nothing outside this set appears in the source.** This is the guard that makes a
hand-written converter safe to own: the failure mode of a partial converter is silence — an unsupported
construct is emitted as literal text or dropped, and nobody notices until a user reads it offline. The guard
turns that into a build failure the moment the construct is introduced, and tells whoever introduced it that
the converter needs extending.

### 4.4 Code spans must escape their contents

Line 252 of the guide reads, in part, `` `wire-<profile>-<date>-<time>.log` ``. A converter that emits code
span contents verbatim produces `<profile>` inside `<code>`, which a browser parses as an unknown element and
renders as nothing. The user is shown `wire-.log`.

Code span and code block contents are HTML-escaped — `&`, `<`, `>` — before emission. This is called out
explicitly because it is the one place where the naive implementation is wrong, the output still looks
plausible, and the error ships offline where it cannot be corrected.

### 4.5 Anchors

The guide's twelve internal links are its own table of contents. GitHub derives anchor ids from heading text;
the converter derives them the same way — lowercased, spaces to hyphens, punctuation dropped — so that
`[Keyboard](#keyboard)` resolves against `<h2 id="keyboard">`. A test asserts every `#` link in the source
resolves to an id the converter emitted, so a heading renamed without its links being updated fails rather
than producing a dead link in a page nobody can fix after it ships.

### 4.6 Presentation

One self-contained file: an inline `<style>` block, no separate CSS asset, no web fonts, no scripts. A
`prefers-color-scheme: dark` block so the page is not a white slab for a user in a dark session. Tables get
borders and the fenced block gets a monospace treatment; beyond that the styling is deliberately plain.

## 5. Scope: the guide alone

`docs/user-guide.md` ships. `README.md` does not, and neither does any developer documentation —
`development.md`, `ci-and-release.md`, `engines.md`, `architecture.md`.

The README is an overview of what LizTerm is, how to download it, and how to get through first run. By the
time the application is running, the reader has had every reason to see it already and no remaining need for
it. Its absence offline is not a hardship.

The guide links to `../README.md` twice — once bare, once at `#first-run`, from Known limitations. Those two
become absolute GitHub URLs. §5.1 says at which ref.

### 5.1 Two kinds of link, two different refs

| Link | Ref | Why |
|---|---|---|
| The two `../README.md` cross-links | `blob/v<version>/README.md` — the tag | They are part of the document's own content, and should describe the release the reader is running. |
| The banner's "current version" link, §6.1 | `blob/main/docs/user-guide.md` | Its entire purpose is answering "has this changed since I installed it?", which only `main` can answer. |

The distinction is deliberate and is the kind of thing that gets flattened by a later editor who sees two
GitHub URLs and makes them consistent. It is written down here so that the reason survives.

## 6. The provenance banner

### 6.1 What it says

Above the `<h1>`, a plainly styled note — not a warning colour, not an alert icon:

> Offline copy, shipped with LizTerm 0.5.0. The manual may have been updated since this release — [the current
> version is on GitHub](https://github.com/coffeemuse/LizTerm/blob/main/docs/user-guide.md).

The version shown is whatever `Directory.Build.props` holds when the file is generated; `0.5.0` above is
illustrative, and §6.3 covers the ordering that makes it read `0.4.1` until the version bump.

It states a fact. In the normal case nothing is wrong, and the styling should not suggest otherwise.

### 6.2 It is generated, not authored

The banner exists only in the HTML. `docs/user-guide.md` does not contain it and must not: that file *is* the
live copy, and a note telling its reader to go and find the live copy would be false there. The converter
emits it, which means the golden-file test in §4.2 covers its wording like everything else.

This is what retires the risk that an offline document silently drifts. A bundled copy that can go stale is a
liability; a bundled copy that says it might have, and where to check, is just a document.

### 6.3 The version is baked, and the existing checks enforce it

The banner names a version, which is what makes it useful, and the version is written into the HTML at
generation time from `Directory.Build.props`.

Nothing new is needed to keep it honest, because the chain already exists end to end:

- §4.2's golden-file test pins the committed HTML to `Directory.Build.props`.
- `release.yml`'s `version` job pins `Directory.Build.props` to `LizTerm.parcel` and to the tag.

So a release cannot ship a banner naming a version other than its own. The practical consequence is an
ordering one: this work lands before release scope §5.5, so the committed HTML will say `0.4.1` until the
version bump, at which point the golden-file test fails until the file is regenerated. That is the mechanism
working, not a defect, and §5.5's step gains one regeneration.

## 7. The application

### 7.1 IUriOpener

A new `IUriOpener` in `src/LizTerm.App/Files/`, beside `IFolderOpener`, constructed with the window and
injected at `App.axaml.cs`'s existing site — the same line that already constructs
`new AvaloniaFolderOpener(window)`.

```csharp
public interface IUriOpener
{
    /// <returns>False when the platform could not open it; the caller then shows the target instead.</returns>
    Task<bool> OpenAsync(Uri uri);

    Task<bool> OpenFileAsync(string path);
}
```

Two methods on one interface rather than two interfaces. `OpenAsync` wraps `Launcher.LaunchUriAsync` for the
three project links. `OpenFileAsync` wraps `Launcher.LaunchFileInfoAsync` — the direct sibling of the
`LaunchDirectoryInfoAsync` that `AvaloniaFolderOpener` already calls, and both are confirmed present in the
pinned Avalonia 12.1.2. It takes a path rather than a `file://` URI because the extracted file sits under the
system temp directory, which on Windows routinely contains a space (`C:\Users\First Last\AppData\Local\Temp`);
handing the platform a `FileInfo` avoids URI escaping entirely rather than getting it right.

`FakeUriOpener` beside `FakeFolderOpener`, recording targets and returning a settable result, the same shape.

### 7.2 Extraction

`AssetLoader.Open` the resource, write it to
`Path.Combine(Path.GetTempPath(), $"lizterm-user-guide-{version}.html")`, then `OpenFileAsync` it.

The name is stable rather than a GUID — unlike `B3270Session`'s temporary PEM files, which are per-connection
secrets — so reopening Help overwrites one file instead of accumulating them. It carries the version so that
an upgraded LizTerm cannot serve the previous release's page from a temp directory the OS has not yet cleaned.
The file is rewritten on every open rather than reused: it costs nothing at this size and removes any question
about a truncated or edited leftover.

### 7.3 Failure path

`ShowWireLogsAsync` is the precedent and is followed exactly: try, and when the platform returns false or
throws, set `ErrorMessage` naming the target so a user on a machine with no browser association can read it
and act. For the guide, the message names the GitHub URL rather than the temp path — the URL is what a person
can actually use, where the temp path is an artifact of our implementation.

## 8. The Help menu

Four new items, above the existing diagnostics:

```
Help
  User Guide
  Project on GitHub
  Report an Issue...
  Releases
  ----
  Wire Log
  Show Wire Logs...
  ----                  (AboutSeparator, hidden with About on macOS)
  About LizTerm...
```

Documentation sits above the wire-log items because a stranger opening Help in a release whose theme is a
public debut wants the manual, not a diagnostic toggle.

*Report an Issue...* points at `/issues/new/choose`, which lands on the issue forms release scope §5.4 added,
so the two halves of "file a useful bug report" meet.

Three rules from #48 apply and are not negotiable:

- **Both menus, or the parity guard fails.** `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item`
  walks them item for item. Both live in `SessionWindow.axaml`: the `NativeMenu` at line 138 and the classic
  `MenuItem` at line 230.
- **Every native item carries a `Command` or a `Click` handler.** Avalonia's macOS exporter validates
  `(Command != null || HasClickHandlers) && IsEnabled`; an item with only a binding is greyed on macOS and
  inert everywhere else. `NativeMenuTests.Every_native_item_can_actually_be_activated` is the guard.
- **No `Gesture` on any of them.** A native gesture is an AppKit key equivalent dispatched ahead of the
  window's responder chain, so it would take the keystroke away from the terminal. `Only_the_edit_menu_carries_gestures`
  is the guard.

The new separator sits below *Releases*. It is unconditional, unlike `AboutSeparator`, which
`The_separator_above_about_is_hidden_with_it` covers because About moves to the application menu on macOS.

## 9. Testing

| Test | What it holds |
|---|---|
| Converter unit tests, one per construct in §4.3's table | The conversion itself |
| Code spans escape `<`, `>` and `&` | §4.4, the silent-corruption case |
| Every `#` link resolves to an emitted id | §4.5 |
| The committed HTML equals a fresh conversion | §4.2, the golden file |
| The source uses no construct outside the supported set | §4.3, the guard |
| The banner names `Directory.Build.props`' version | §6.3 |
| `NativeMenuTests` parity and activation, both menus | §8 |
| A `FakeUriOpener` records the three URLs from both menus | §7.1 |
| Extraction writes the resource's bytes to the expected path | §7.2 |
| A false result from the opener sets `ErrorMessage` naming the URL | §7.3 |

All of it runs headless, and needs no browser present. Note which workflow that means: `ci.yml` is a single
`ubuntu-latest` job, so the cross-platform coverage comes from `engines.yml`, which runs the full suite on
`macos-15`, on Linux and on `windows-latest`. The line-ending normalization in §4.2 is what keeps the
golden-file test honest on the third of those.

## 10. Documentation and bookkeeping

- `docs/development.md` — the `LIZTERM_UPDATE_DOCS=1` regeneration command, and the rule that
  `Assets/Docs/user-guide.html` is generated and never hand-edited.
- `docs/user-guide.md` — its own Menus section gains the four Help items.
- `src/LizTerm.App/CLAUDE.md` — `IUriOpener` beside the existing `IFolderOpener` note.
- `tests/CLAUDE.md` — where the converter lives and why it is in the test project.
- Release scope §4.1 and §9 — record this spec, and that 4.1 is no longer unscoped.
- Issue #48 — closed by the resulting PR.

## 11. Out of scope

**The keyboard table's drift from `DefaultKeymap`.** Issue #48 raises it, and it is real: the guide's Keyboard
section is a prose copy of a table the code owns. It is deliberately not in this spec. It is a property of the
Markdown source, identical before and after bundling, and folding it in would grow the release's long pole to
fix something this change does not worsen.

It also does not get its own issue, and the reason is worth recording, because "a documented table can drift
from the code" reads like a standing defect that someone should file. While `DefaultKeymap` is the only keymap
there is, the prose copy is checked by reading it, and a written table is sufficient. What makes it a real
defect is **#18, the user-editable keymap**: once a user can change the bindings, a fixed table in a document
is not stale, it is wrong, and no amount of care in the document can fix it. The resolution is therefore part
of #18's own work rather than a task beside it — when the keymap becomes editable, the app generates the
table from the map in force. Recorded here and on #18; not filed separately.

**An in-app documentation window.** Avalonia 12 ships a first-party `Avalonia.Controls.WebView` with
`NativeWebDialog`, which would render this same HTML in a window of our own. It is rejected for 0.5.0 on
platform prerequisites, not on architecture: on Linux it needs GTK 3, WebKitGTK 4.1 and libsoup 3 present on
the user's machine, which LizTerm currently requires nothing of, and on Windows the WebView2 runtime may be
absent on Windows 10. Declaring those in the `.deb` and `.rpm` to ship a help page is a poor trade; not
declaring them means Help silently does nothing on a minimal install, which is the broken basic release scope
§1 rules out. It is worth revisiting after 0.5.0, when the prerequisite can be decided deliberately and tested
rather than discovered during the cross-platform pass.

**Bundling the README, a website, or a rendered manual for #49.** #49 remains out of scope per release scope
§8; this spec's one-source-of-truth arrangement is what a site would eventually render from.

## 12. The reduction, if this proves larger than expected

Release scope §10 names it and it stays available: **ship §7 and §8 — the links — and defer §3 to §6, the
bundle.** The three project links are a day's work on a proven seam, and the user guide remains reachable on
GitHub for anyone with a network. The theme survives; what is lost is the offline case.

The order of work in the plan should preserve that option: the links first, the bundle second, so the
reduction is a decision not to continue rather than a decision to unpick.
