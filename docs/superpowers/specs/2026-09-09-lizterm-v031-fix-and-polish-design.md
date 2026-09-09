# LizTerm v0.3.1: fix and polish

Date: 2026-09-09. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessor:
`2026-09-08-lizterm-m3e-packaging-design.md` (plan 3e, which shipped v0.3.0 earlier the same day and whose §8
manual pass is still outstanding for three of the six RIDs). Status: approved in discussion on 2026-09-09;
awaiting review of this text.

## 1. Purpose

v0.3.0 is published. This is the first pass back through the app *as shipped*: two bugs found by using it, the
polish items raised while reviewing it, and one CI gate that has never been proven to bite.

It is deliberately not a feature milestone. Every item here was already filed, every item is provable on a Mac
plus CI, and nothing in it claims anything about Windows or Linux that CI does not already prove — Robert has
neither machine available for hands-on checks, which is a constraint this plan respects rather than works
around.

Ten issues: #39, #50 (bugs), #42, #41, #38, #40, #16 (polish), #44, #45 (the profile editor's two combos), and
#13 (the macOS engine gate's negative fixtures).

Decisions taken in the brainstorm on 2026-09-09:

- **#23 is cut**, not deferred vaguely. Its own text says it is worth doing after #22, and #22 cannot be
  answered without a real Windows machine and a real Linux desktop. While both menus exist the keystroke hint
  has to live in *header text* on the native side, which the parity guard compares — so #23 forces a parity
  decision that #22 would make moot. Section 8 records this.
- **#47 (the bell) is cut.** It is a subsystem, not polish: a new indication, a **sixth event on
  `IEmulatorSession`**, an `IBellRinger` seam with per-platform P/Invoke, and a rate-limit decision. It earns
  its own plan.
- **#44 and #45 are done as one pair.** Same window, same source, same trap; shipping one annotated combo
  beside one bare one is worse than shipping neither.
- **#16 ships its menu half only.** The chord question stays open (§4.5).

Two of the ten issues describe fixes that are wrong as written. Sections 4.3 and 6 record the corrections and
why.

## 2. The two bugs

### 2.1 #39: Connect stays enabled while connected

Connect a session, pick File > Connect again, and the error banner reads
`Unexpected error: verifyHostCert cannot change while connected`.

`ConnectCommand` (`src/LizTerm.App/ViewModels/SessionViewModel.cs:241`) is a bare `[RelayCommand]` with no
`CanExecute`. CommunityToolkit disables an async command only *while it runs*, so the item comes back the
moment the session is up. `ConnectAsync` then sends `Set verifyHostCert` before `Connect`, b3270 refuses it on
a connected session, and the resulting `EmulatorActionException` is neither a `ConnectionFailedException` nor a
`BackendUnavailableException` — so it lands in the generic catch, whose "Unexpected error: " prefix is reserved
for what we did not anticipate. Here we can anticipate it exactly.

**The predicate is `!HasSocket()`, not `!IsConnected`.** Verified in
`src/LizTerm.Core/Session/ConnectionState.cs`: `HasSocket()` is false for `Disconnected`, `Reconnecting`,
`Resolving` and `TcpPending`, and true for everything past the TCP connect. That is the engine's own rule for
refusing the `Set` — b3270 objects whenever it has a host session, which begins before the 3270 session does.
`IsConnected` is the `Connected*` group only, and leaves open a narrow but real window: a connect whose
cancel-or-timeout `Disconnect` wait expired (`DisconnectTimeout`, 5 s) returns with the engine still holding a
socket, the command completed and re-enabled, and `IsConnected` false.

`ApplyConnection` (`:232`) already has the `ConnectionState` in hand and keeps only the bool, so this is one
field.

**Add `_hasSocket` beside `_isConnected` (`:62`); do not replace the bool.** `IsConnected` is public contract —
bound as `IsEnabled` on the File Transfer and Paste items and used as `PasteCommand`'s `CanExecute` — and
replacing it with the raw state would either churn that XAML or need an `[ObservableProperty]` named
`ConnectionState`, which collides with the enum type of the same name. A second `[ObservableProperty]`, set from
the same `ApplyConnection` line, costs one field and no churn.

- `_hasSocket` carries `[NotifyCanExecuteChangedFor]` for both `ConnectCommand` and `DisconnectCommand`.
- Both menus follow from that one change, because both Connect items bind `Command`
  (`SessionWindow.axaml:24` native, `:96` classic) rather than being Click-driven like the Edit items.
  `NativeMenuTests.Every_native_item_can_actually_be_activated` stays satisfied: the item still has a Command.

**Connect while connected is disabled, not turned into reconnect.** x3270's own File menu disables it, and
reconnect is a larger behaviour that overlaps #28. It is not smuggled in here.

`DisconnectCommand` (`:401`) gets the same treatment in the same pass — it is live on a disconnected session
today, harmlessly (with no pending attempt it calls `DisconnectAsync`, which sends nothing when already
disconnected), but it is the same inconsistency one line away.

Its predicate is **not** the inverse of Connect's. Its doc comment records that it also cancels a *pending*
connect, so it is `HasSocket || a connect is pending` — a pending connect is exactly when Disconnect is most
wanted, and `HasSocket()` is false through `Resolving` and `TcpPending`. The pending half needs its own
observable: `_connectCts` is a plain field, so assigning it raises nothing. Set an `[ObservableProperty]`
alongside it, on both the assignment and the clear, carrying `[NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]`.
Do not reach for `ConnectCommand.IsRunning` instead: it is true for the whole of `ConnectWithAsync`, including
the certificate prompt and the profile save that deliberately run after the catch clauses.

### 2.2 #50: a stale picker silently overwrites a certificate pin

`ProfileStore.Update` (`src/LizTerm.Core/Profiles/ProfileStore.cs:34`) exists precisely because a session
window's profile is fixed at construction, so a pin written from a session must be merged into the file as it
is on disk. `App.WritePinBack` is its only caller. The picker has the same staleness problem and none of the
care: `ProfilePickerViewModel.EditAsync` (`:66`) calls `_store.Save(edited)` — a whole-record overwrite.

The reported sequence: a picker opened via File > New Session alongside a running session reads `MVS` with no
pin; the session then pins a certificate and `WritePinBack` merges it correctly; the picker still holds its
older copy; Edit loads `_pinnedCertificate` from that copy as null, and Save writes the whole record back. The
pin is gone, and the editor never rendered its pin panel, so nothing looked missing.

Severity is **lost work, not a security hole**: the overwritten profile keeps `VerifyCertificate = true`, so
the next connect fails verification and prompts again. The failure direction is more prompting, not a silent
downgrade.

Three fixes, all taken:

**Freshness.** The picker calls `Reload()` from the window's `Activated`, and again before opening the editor.
`Reload()` already preserves the selection by name. Cost is a directory read on focus.

**Correctness.** The editor's Save merges rather than overwrites. This is the durable half: #43 (profile tags
and notes) and #19 (preferences) are both flagged in the issue as widening this class, and freshness alone
leaves the property intact.

The rule is that **the editor owns every field it edits, and does not own the pin.** The pin is read from disk
at save time and carried forward. Two subtleties make this more than a call to `Update`:

- **Forget must survive the merge.** The editor's Forget button is the only route back to default trust, and it
  is expressed today by nulling the editor's copy of the pin — which is indistinguishable from "the editor
  never had one", the very state the stale copy produces. It becomes an explicit flag on the editor view model
  (`PinCleared`), and the merge carries the disk pin forward unless that flag is set. Nulling a field cannot
  mean two things.
- **The editor already drops a pin on purpose, and the merge must not undo that.** `_pinnedFor` plus
  `RefreshPin()` null the pin when Host or Port is edited away from the values it was taken from, and restore it
  when they come back — the record's own comment says a pin belongs to the host and port it was taken from. So
  "carry the disk pin forward" is wrong on its own: it would resurrect a pin the user deliberately invalidated
  by repointing the profile. The rule is to carry it forward **only when the edited host and port still match
  the ones on disk**.

Three conditions interacting (stale copy, Forget, host/port moved) is more than belongs inline in a command
handler, so it goes in one pure function — `PinMerge.Resolve` in `LizTerm.Core.Profiles` — with its own table of
unit tests. The picker calls it and stays a three-line command.
- **Renames break a naive `Update`.** `EditAsync` handles a rename by deleting the old file
  (`ProfilePickerViewModel.cs:65`) before saving. `Update(edited, …)` loads by `edited.Name`, so after a rename
  it looks up a file that does not exist, falls back to `edited`, and loses the pin exactly as today. The merge
  source must be the **original** name. Shape:

  ```csharp
  var onDisk = _store.Load(original.Name);
  var merged = edited with { PinnedCertificate = PinMerge.Resolve(edited, onDisk, pinCleared) };
  if (renamed) _store.Delete(original.Name);
  _store.Save(merged);
  ```

**Durability.** `ProfileStore.Save` (`:57`) is a plain `File.WriteAllText`, and `Read` swallows
`JsonException`/`IOException`/`UnauthorizedAccessException` and returns null, which `LoadAll` skips silently.
So a write interrupted partway leaves a truncated file that the picker renders as **a profile that simply is
not there** — indistinguishable from one the user deleted. `Save` writes to a temp file in the same directory
and `File.Move(..., overwrite: true)`. A few lines, and a distinct data-loss mode from the one above.

## 3. Scope note: what "near-free" survived contact

Five of the nine items originally called near-free are: #42, #41, #38, #40, and #16's menu half. Of those,
#38 turns out to need a Core change to stay inside the dependency rule (§4.3). The other two — #44 and #45 —
are real work and are treated as such in §5.

## 4. The five

### 4.1 #42: About's copyright line repeats itself

About's credit reads `Copyright 2026 by CoffeeMuse - BSD-3-Clause`. Three lines below, the Licenses box opens
with `LizTerm, BSD-3-Clause`, and a few lines after that the embedded LICENSE opens with
`Copyright 2026 by CoffeeMuse`. The suffix restates what is already on screen twice.

`AppLicense` already has both constants (`src/LizTerm.App/AppLicense.cs`): `Copyright`, and `Notice` which is
`Copyright + " - " + SpdxId`. About switches from `Notice` to `Copyright`. One line. `Notice` stays as it is —
the splash still wants it, and it is deliberately ASCII because the splash renders it in the 3270 font.

### 4.2 #41: "File Transfer..." names the category, not the mechanism

Everything behind that item is IND$FILE typed through the 3270 session. #17 adds a second transport (z/OSMF
REST) and FTP is a possible third, so the item is renamed **"IND$FILE Transfer..."** before there are two. The
jargon is right for this audience: to a retro mainframe hobbyist "IND$FILE" is more precise than "File
Transfer", not less legible.

Both menus are separate declarations of the same item (`SessionWindow.axaml:27` native, `:99` classic) and
change together, because `The_native_menu_matches_the_classic_menu_item_for_item` compares headers.

### 4.3 #38: the status bar says "Model 2-E" where the terminal type is "3279-2-E"

`Model 2` does not say 3278 or 3279 — the colour distinction — and it is the one place a user can read what
terminal they are. It should read the full terminal type, the way a 3270 user writes one.

**The issue's proposed fix breaches the dependency rule.** It says to use `HostStringBuilder.ModelArgument`
(`src/LizTerm.Backend.B3270/Protocol/HostStringBuilder.cs:24`), but `StatusFormatter`
(`src/LizTerm.App/Status/StatusFormatter.cs:62`) is in App, which by convention names
`LizTerm.Backend.B3270` in exactly one place: `SessionFactory.cs`. It would compile — the project reference
exists — and quietly make that two places, which is the kind of drift the rule exists to catch in review.

Instead the terminal-type spelling moves into **Core**, and both callers read it. It is domain data (what
terminal this profile *is*), not protocol: the backend happens to put it on a command line, and the status bar
happens to render it, but neither owns it. `HostStringBuilder.ModelArgument` becomes a delegation, so the
engine argument and the status bar cannot drift apart.

`StatusFormatter`'s assertions are exact by house rule, so its tests change in the same commit.

### 4.4 #40: the app icon reaches the Dock and the installers, but not the two windows about identity

`Assets/Icons/lizterm-256.png` is already an `AvaloniaResource` — the csproj `Remove`s only `lizterm.icns` and
`lizterm.ico` — and is what every window setting `Icon=` points at. No new file is needed.

- **Splash:** the icon goes above the existing wordmark, in the `<ContentControl x:Name="SplashMark">` seam,
  keeping the 480x300 window. `SplashWindowTests` asserts that control is findable by name, so the name stays.
  This is the branding-consistency version; the full art replacement the file's own comment anticipates stays
  available and is a design task, not this.
- **About:** the icon to the left of a name-and-version block, which is what a platform About box looks like on
  all three.
- Both get explicit `Width`/`Height` and `Stretch="Uniform"` rather than sizing to the bitmap. `lizterm-256.png`
  is the right source for both; `lizterm.png` is the 1254x1254 master the csproj comment says not to resize.

**The related gap, closed in the same pass:** `AboutWindow`, `CertificateWindow`, `FileTransferWindow` and
`ProfileEditorWindow` set no `Icon` at all, where `ProfilePickerWindow`, `SessionWindow`, `SplashWindow` and
`StartupErrorWindow` all do. macOS ignores `Window.Icon` entirely, which is why this was invisible on the
platform the branding gap was noticed on; on Windows and Linux those four dialogs show Avalonia's default icon
in the title bar and taskbar. One attribute each.

### 4.5 #16: Dup and FieldMark are in Core but unreachable

`TerminalKey.Dup` and `TerminalKey.FieldMark` exist, and `ActionMap.cs:31-32` maps them to b3270's `Dup` and
`FieldMark`. Nothing in App references either: no keymap entry, no menu item. They are reachable only by a
caller writing C# against `IEmulatorSession`. Both are real data-entry keys on 3270 panels — DUP writes X'1C'
and tabs to the next field, FIELD MARK writes X'1E' — and they turn up in exactly the old CICS and TSO panels
this project's users run.

**The menu half only.** Two items in each menu (`SessionWindow.axaml:48` native, `:109` classic), bound to
`SendKeyCommand` beside the existing Clear / Reset / Attn / SysReq group. That closes "unreachable".

**The keymap half stays open**, deliberately. Choosing chords is the §6.2 exercise from the hardening spec —
Vista's defaults cross-checked against wc3270 — and whatever is chosen has to clear the platform copy, paste
and select-all gestures, which `TerminalScreen` checks *before* the keymap. That is the same constraint that
kept Ctrl+Insert from being PA1, and it is a decision on its own evidence rather than one to take in passing.

## 5. The profile editor's two combos (#44, #45)

### 5.1 Why these are not polish

The Code page field is a bare `TextBox` (`ProfileEditorWindow.axaml:36`) validated only as non-empty
(`ProfileEditorViewModel.cs:87`). A typo does not fail: measured, `b3270 -codepage bogus-page` prints
`Warning: Cannot find code page "bogus-page"` to **stderr**, starts anyway on a fallback, and echoes the bad
name back in its own settings. LizTerm keeps a 50-line stderr tail but surfaces it only on a startup timeout
and on process death — never on a successful start.

So a mistyped code page gives a session that connects normally, a status bar that says nothing, and a character
set that is quietly not the one asked for. On a 3270 client that is wrong characters with no thread to pull.
That, rather than friendliness, is the argument for the drop-down.

The Model combo one row up shows `2`, `3`, `4`, `5` (`ProfileEditorViewModel.cs:39`) and nothing else. Those
numbers mean screen geometry, and geometry is what the user is actually choosing: a model 5 is picked because
it is 27x132.

### 5.2 Where the data lives

Both lists come from b3270's `initialize` block, the same block already parsed for `hello` and `tls-hello`:
`models` carries `{model, rows, columns}` (measured from
`tests/LizTerm.Backend.B3270.Tests/Fixtures/gateway-cert-failure.jsonl`), and `code-pages` carries 41 entries
of `name` plus `aliases`.

But **the editor opens from the picker, where no engine is running**, so the live block is not available when
the combos need filling. The alternatives were a throwaway b3270 started to enumerate, or caching the last
start's block to the config directory — both of which need a Core-level capability to surface a Backend block
to the App, for a list that changes only when x3270 is bumped.

**Static tables, made falsifiable.** The tables live in **`LizTerm.Core`** — not App — for one concrete reason:
`tests/LizTerm.Integration.Tests` references `LizTerm.Backend.B3270` and not App, and the falsifying test has
to reach them. They are domain data, so Core is where they belong anyway.

The test starts the bundled engine, as `EngineSmokeTests` already does through `BundledEngine.Require()`, and
asserts our tables against the `models` and `code-pages` the engine reports. That turns "the list drifted at the
next x3270 bump" from a silent problem into a red test. It skips without a bundled engine on the same terms as
the rest of that project.

### 5.3 Shape

`ModelChoice(int Number, int Rows, int Columns)` with `DisplayMemberBinding`, rendered `3 — 32x80`. A display
converter alone would have nowhere to read rows and columns from and would have to carry its own hardcoded
lookup, which is the duplication being removed; and #30 (oversize geometry) wants rows and columns as data
rather than text, so this is the shape that does not have to be redone.

Code pages render `name — label`: the engine name leads, because that is what goes on the command line and what
appears in a wire log, and the label is curated rather than raw aliases — `cp1047` has no alias at all and
would render blank, and several others are terse (`uk`, `us-intl`, `oldibm`).

**`bracket` is moved to the front, and alphabetical order is not the way to do it.** The issue worries it
"sorts to the bottom under a name no newcomer can interpret", which matters because it is what this project's
own TK5 sample profile uses. Sorting alphabetically does put `b` ahead of every `cp*` — but it also sorts
`cp1026` ahead of `cp273`, because these are strings and `1` precedes `2`, scattering the numeric families
across the list. Measured against the live engine on 2026-09-09: the 41 entries arrive in ascending numeric
order with `bracket` last.

So the table keeps **the engine's own order and moves `bracket` to the front** — one explicit rule, a natural
reading order for the rest, and the concern answered. The falsifying test asserts set equality rather than
order, so the ordering is ours to choose.

`src/LizTerm.App/Views/TransferLabels.cs` is the in-repo precedent for the converter-based alternative if the
record shape proves awkward for code pages, which have no second field to carry.

### 5.4 The trap both combos share

`_codePage` is a `string` loaded straight from the saved profile, and `Model` gets **no validation at all** in
`TryBuild` (`ProfileEditorViewModel.cs:82-104` checks Name, Host, Port and CodePage, and passes Model through).
A `ComboBox` bound `SelectedItem` has nothing to select when the saved value is outside `ItemsSource` — a
hand-edited file, or a code page a newer engine adds that our table has not caught up with.

**A working profile must not lose its setting because the user opened the editor.** Each combo seeds its list
with the profile's current value when that value is not already there. This is preferred over
`IsEditable="True"`, which reintroduces the free-text typo the change exists to remove. Both need a test with a
profile whose value is outside the table.

## 6. #13: the macOS engine gate has no negative fixtures

`engine-linux` runs `verify-linux.sh` against three binaries every run: the engine it built (must pass),
`/usr/bin/bash` (must be rejected by the dependency allowlist) and a `/bin/true` from a digest-pinned
`debian:12-slim` (must be rejected by the glibc floor), each rejection asserted against the message that arm
prints. `verify-macos.sh` runs against the binary it just built and nothing else. A gate that has silently
stopped rejecting passes `engine-macos` exactly as a working one does, and the job then uploads
`b3270-osx-arm64` on its say-so.

This is the project's own recorded lesson — plan 3b deviation 5, restated as plan 3c deviation 10: a guard that
cannot fail is not a guard. Plan 3c deviation 7 is the concrete precedent for what an unproven gate lets
through: a b3270 built without `LIBS="-ldl -pthread"` reports `TLS provider: None`, is 4.7 MB against 11 MB,
and links **fewer** libraries, so it passes a dependency-only gate more comfortably than the real engine.

### 6.1 Both fixtures the issue proposes are wrong

Measured on an arm64 Mac on 2026-09-09:

- **The dependency-arm fixture is stale.** The issue picks `/opt/homebrew/bin/openssl` and argues it is
  "present by definition wherever this gate can run at all: `build-macos.sh` requires Homebrew `openssl@3`".
  That stopped being true. `build-macos.sh`'s own header now reads "Homebrew is NOT required -- OpenSSL comes
  from the tarball `fetch-openssl.sh` pins, the same one `build-linux.sh` uses." The fixture's whole
  justification is gone; it survives on Robert's Mac only as a leftover.
- **The TLS-arm fixture is rejected by the wrong arm.** The issue picks `/bin/echo`, on the grounds that
  `/bin/echo --version` prints `--version` and exits 0, so it reaches `shared-verify-tls.sh`'s "no OpenSSL TLS
  provider" branch rather than its "the engine did not run" branch. But `verify-macos.sh` checks the machine
  type **first**, and `lipo -archs /bin/echo` reports `x86_64 arm64e` against a `uname -m` of `arm64`. It is
  killed by the architecture check and never reaches the TLS arm at all.

  This generalises: **no macOS system binary can serve as a TLS-arm fixture.** Apple's platform binaries are
  universal and their arm slice is `arm64e`, the signed-pointer ABI — never plain `arm64`. The issue's own
  standard is that a fixture must prove the arm we care about and not the one next to it, and this one fails
  its own test.

### 6.2 Build the fixtures instead

`cc` is guaranteed present, because Xcode command line tools are already a hard requirement of
`build-macos.sh`. Both fixtures are compiled on the spot into `native/build-tmp`:

- **TLS arm:** a trivial C program that prints a line and exits 0. Compiled for the host architecture it
  reports plain `arm64`, so it clears the machine-type check; it links only `libSystem`, so it clears the
  dependency check; and it reports no OpenSSL provider, so it dies at the TLS arm. Exactly one arm bites.
- **Dependency arm:** the same program linked against a throwaway dylib built beside it with an install name
  outside `/usr/lib` and `/System/Library`. It clears the machine-type check and dies at the dependency arm.

This is strictly better than the Homebrew fixture on the issue's own criterion: it depends on nothing the build
does not already require, is hermetic, and behaves identically on a developer's Mac and on `macos-15`. It is
also the same discipline plan 3c applied to Linux — pick a fixture the environment guarantees — applied to an
environment whose guarantees changed.

### 6.3 Where it runs

One step in `engine-macos` that invokes `verify-macos.sh` three times in a single step: the built engine must
pass, each fixture must be rejected **by the message its own arm prints**, not by a bare non-zero exit. Exit
codes cannot separate "the gate rejected it" from "something else went wrong" — plan 3c deviation 11 records
why, and the same shape is used here.

**The step must run whether or not the engine came from the cache.** On a cache hit the build step never
executes, which is exactly when a stale binary sits between the cache and the artifact upload.

Note this edits `native/build/`, which is in the engine cache keys, so the first run after it is a cold rebuild
on the macOS legs. Expected, not a defect.

## 7. Testing and verification

- `dotnet test LizTerm.slnx` — the full suite.
- `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` — the zero-warning check, which an
  incremental build hides.
- New tests, per item: the `CanExecute` boundary for #39 (asserting the `HasSocket` edge, not `IsConnected`);
  a picker test that a session's pin survives an edit, **including across a rename**, and an editor test that
  Forget still clears it; a `ProfileStore` test that an interrupted write leaves the previous file readable;
  `StatusFormatter` exact-string updates for #38; combo tests for a profile whose model or code page is outside
  the table; and the integration test asserting both tables against the bundled engine.
- The macOS gate's three-way invocation is itself the test for #13.
- Manual pass on macOS only: launch, connect to the live host, open the profile editor and check both combos
  round-trip, open About and the splash, send Dup and FieldMark from the Keys menu.

Nothing here is verified by hand on Windows or Linux. Every item either has no platform-specific behaviour or
is already covered by CI's own gates.

## 8. Out of scope

- **#23 (Keys menu keystroke hints)** — blocked behind #22, which needs a real Windows machine and a real Linux
  desktop. Recorded in §1.
- **#22 (flip the menu default, delete the classic menu)** — same hardware block.
- **#47 (the terminal bell)** — its own plan; §1 records why.
- **#16's keymap chords** — §4.5.
- **#12 (no SNI in x3270)** — latent, and its fix would make `native/build` patch-carrying for the first time.
  A decision on its own evidence.
- **#36 (notarization and Authenticode)** — the next milestone, gated on Apple Developer ID enrolment, which is
  administrative lead time rather than work this plan can start.
- The three RIDs still resting on the machine-type assertion alone (`osx-x64`, `linux-arm64`, `win-arm64`) from
  plan 3e §8. Unchanged here, and unchangeable without runners that can execute them.
