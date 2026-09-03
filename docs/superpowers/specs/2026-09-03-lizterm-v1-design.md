# LizTerm v1 Design

Date: 2026-09-03
Status: approved in discussion, pending written review
License: undecided (BSD-3 or GPL; repo private for now; all dependencies are BSD-3 compatible with either)

## 1. Purpose

LizTerm is a cross-platform TN3270 client for retro mainframe hobbyists. It targets macOS, Linux, and Windows with one consistent, modern UI, packaged so a user can download it and run it without installing anything else.

The gap it fills: x3270 is tied to X11 and lags on macOS and Wayland; Vista TN3270 is Windows x86 only; running a different client per platform is inconsistent. Vista TN3270 is the behavioral reference when a UX question has no obvious answer.

Success for v1: a hobbyist can download a release, create a profile for their MVS 3.8j (TK4-/TK5), VM/370, or z/OS host, connect over plain or TLS, log on, work in TSO or CMS, copy and paste JCL, and transfer a file with IND$FILE, without reading a manual.

## 2. Core decision: b3270 as the emulation engine

LizTerm does not implement the 3270 data stream. It bundles `b3270` from the x3270 suite (BSD-3) as a child process and speaks its JSON protocol over stdin/stdout. This is the same architecture the x3270 author uses for wx3270, and the same pattern py3270 and Robot Framework's mainframe library use with s3270.

Why: the differentiator is the client experience, not the protocol. b3270 provides TN3270E, TLS, all code pages including DBCS, extended attributes, IND$FILE, NVT mode, and decades of edge-case handling against exactly the quirky hosts hobbyists run.

Cost accepted: we build and ship a dependency-free b3270 binary for every target (section 8). A later managed C# engine is possible because the UI depends only on the Core session interface (section 4), never on b3270.

Rejected: porting x3270 to C# (months of engine work before a usable client); in-process b3270 via P/Invoke (x3270 is not structured as a library; would mean maintaining a C fork).

## 3. Stack and solution layout

- .NET 10 (LTS). Self-contained, single-directory publish per runtime identifier. No NativeAOT in v1.
- Avalonia 12, Fluent theme tuned rather than replaced. Custom drawing only for the terminal screen. Wayland support comes from Avalonia; no platform-specific code in LizTerm.
- CommunityToolkit.Mvvm for view models (source-generated properties and commands). No ReactiveUI.
- System.Text.Json with source-generated contexts for the b3270 protocol and profile files.
- xUnit for all test projects.
- Font: rbanffy's 3270font (BSD-3), embedded as an Avalonia resource. Not user-configurable in v1.

```
LizTerm.sln
src/LizTerm.Core/                    domain model, session interface, profiles
src/LizTerm.Backend.B3270/           process host, JSON protocol, key table, binary locator
src/LizTerm.App/                     Avalonia UI
tests/LizTerm.Core.Tests/
tests/LizTerm.Backend.B3270.Tests/   wire-log replay fixtures live here
tests/LizTerm.App.Tests/             headless Avalonia tests against a fake session
tests/LizTerm.Integration.Tests/     opt-in; requires LIZTERM_TEST_HOST
native/x3270/                        upstream x3270 source, git submodule at a release tag
native/build/                        per-platform b3270 build and verification scripts
docs/
```

Dependency rule: App depends on Core and Backend.B3270 (the latter only for composition at startup). Backend.B3270 depends on Core. Core depends on nothing but the BCL.

## 4. LizTerm.Core

Small and stable. Both the UI and any backend depend on it.

### 4.1 Screen snapshot

Immutable grid of cells, `Rows x Columns`. Each cell:

- `Rune Character`
- Base field attributes: `Protected`, `Numeric`, `Display` (Normal, Intensified, Hidden), `Modified`
- Extended attributes: `Foreground`, `Background` (IBM color enum: Default, Blue, Red, Pink, Green, Turquoise, Yellow, White, plus the extended set), `Highlight` (None, Underscore, Blink, Reverse), `CharacterSet` (Default, Apl, GraphicEscape)
- `IsFieldAttribute` flag for the attribute-byte position

The backend owns a mutable buffer, applies each update batch, then publishes a snapshot with the cursor position. A model 5 screen is under 6,000 cells; copying is negligible. The UI never touches shared mutable state.

### 4.2 Cursor

`Row`, `Column`, zero-origin, published with each snapshot so screen and cursor never tear.

### 4.3 Connection state

Enum: `Disconnected`, `Resolving`, `Connecting`, `TlsHandshake`, `ConnectedNvt`, `Connected3270`, `Connected3270E`. Accompanied by TLS details when secured: protocol, whether the host certificate was verified.

### 4.4 Keyboard status

Derived from the OIA, expressed as enums rather than glyphs:

- `KeyboardLock`: `Unlocked`, `WaitingForHost`, `MinusFunction`, `ProtectedField`, `NumericOnly`, `Overflow`, `NotConnected`, `Other(text)`
- `InsertMode` bool, `Typeahead` bool, `LuName` string, `Model` (2..5, extended flag)

### 4.5 Session profile

`Name`, `Host`, `Port` (default 23, or 992 when TLS on), `UseTls`, `VerifyCertificate` (default true), `Model` (2..5), `Extended` (default true), `CodePage` (default `cp037`), `LuName` (optional).

Stored as one JSON file per profile in the per-user config directory (`~/.config/LizTerm`, `~/Library/Application Support/LizTerm`, `%APPDATA%\LizTerm`). Certificate verification defaults on; a profile may turn it off because hobbyist hosts are almost all self-signed.

### 4.6 Session interface

```
IEmulatorSession : IAsyncDisposable
  Task ConnectAsync(SessionProfile, CancellationToken)
  Task DisconnectAsync()
  Task SendKeyAsync(TerminalKey)
  Task TypeTextAsync(string)
  Task PasteTextAsync(string)            // margin-aware multi-line paste
  Task MoveCursorAsync(int row, int col)
  Task<IFileTransfer> StartTransferAsync(FileTransferRequest)   // exposes progress events and Cancel
  ScreenSnapshot CurrentScreen { get; }
  event ScreenUpdated(ScreenSnapshot)
  event StatusChanged(KeyboardStatus)
  event ConnectionChanged(ConnectionState, TlsInfo?)
  event Faulted(BackendFault)            // process died, protocol broken
```

`TerminalKey` enum: `Enter`, `Clear`, `PF1..PF24`, `PA1..PA3`, `Attn`, `SysReq`, `Reset`, `Tab`, `BackTab`, `Home`, `EraseEof`, `EraseInput`, `Delete`, `Backspace`, `Insert` (toggle), `Dup`, `FieldMark`, `Newline`, `Up`, `Down`, `Left`, `Right`.

`FileTransferRequest`: `Direction` (Send, Receive), `LocalPath`, `HostFile`, `HostType` (Tso, Vm, Cics), `Mode` (Text, Binary), `CrLf` bool, `Remap` bool, and TSO allocation fields `Lrecl`, `Blksize`, `Recfm`, `AllocationUnits`, `PrimarySpace`, `SecondarySpace`, all optional.

### 4.7 Threading rule

A backend raises all events on one dedicated reader thread, in order. Core knows nothing about UI threads. The App layer marshals to the Avalonia dispatcher.

### 4.8 Explicitly not in Core

No physical-key mapping, no rendering hints, no b3270 message names.

## 5. LizTerm.Backend.B3270

The only project that knows b3270 exists.

### 5.1 Process host

Spawns the bundled b3270 in JSON mode with stdin and stdout redirected and stderr captured to the log. Both directions are newline-delimited JSON, one message per line. A dedicated reader thread parses and dispatches; a writer serializes outbound messages under a lock. Disconnect requests a clean exit, kills after a timeout, and disposal always tears the process down.

### 5.2 Message model

Typed records for: hello/initialize (with version), screen-mode, erase, screen (row change runs with attribute blocks), cursor, oia, connection, tls, run-result, ft (transfer progress), popup (errors and info), setting. Unknown message types are logged and ignored so a newer b3270 does not break the client. The backend refuses a b3270 whose protocol version is older than the pinned minimum.

Exact field names are taken from the b3270 documentation of the pinned release during planning, not from memory; the JSON format changed across 4.x releases.

### 5.3 Screen application

Screen messages carry per-row change runs. The backend applies them to its mutable buffer and publishes one snapshot per screen message, with the current cursor. Coalescing is a later optimization only if profiling demands it.

### 5.4 Action correlation

Each outbound run command carries a tag. Pending tags map to completion sources; the matching run-result completes the task with success or the error text. Every Core action method therefore resolves when b3270 has processed the action.

### 5.5 Key and action mapping

One table from `TerminalKey` to b3270 action names, PF and PA parameterized. `TypeTextAsync` becomes a String action. `PasteTextAsync` uses b3270's margin-aware paste action if the pinned version provides one; otherwise it splits on newlines and sends Newline between lines. `MoveCursorAsync` uses the zero-origin action. Connect builds the x3270 host string from the profile (TLS prefix, LU name, host, port); certificate verification is set through a b3270 setting rather than a host prefix. Transfer maps `FileTransferRequest` to the Transfer action's keyword arguments and turns ft messages into progress events.

### 5.6 Binary locator

Looks under the runtime-specific native folder beside the app (`runtimes/<rid>/native/b3270[.exe]`). `LIZTERM_B3270_PATH` overrides for development. On macOS and Linux it also checks the executable bit, a classic zip-extraction failure, and reports it distinctly.

### 5.7 Fault handling

Unexpected process exit raises `Faulted` with the last stderr lines and drops the session to Disconnected. A malformed line is logged and skipped, never fatal.

### 5.8 Diagnostics and fixture recording

A wire log toggle writes every raw line in both directions, timestamped, to a file. It serves as the bug-report mechanism and as the replay fixture recorder: a wire log from a real session becomes a replay test without editing.

## 6. LizTerm.App

### 6.1 Startup and splash

A small borderless splash window shows name, version, and the 3270 glyph mark. It closes on the first of: click or keypress, or a short timer, but never before a minimum of about one second. Behind it the app loads the profile store and locates b3270. If the binary check fails, the splash gives way to the error, not the picker.

Then the profile picker: list of saved profiles, Connect, New, Edit, Delete. Double-click connects. A command-line argument of a profile name or `host[:port]` skips the picker. Closing the last session window brings the picker back; quitting from the picker exits. This rule is identical on all platforms.

### 6.2 Session window

One window per session (as Vista TN3270), titled with profile name and host. Menu bar:

- File: New Session, Connect, Disconnect, File Transfer..., Close
- Edit: Copy, Paste, Select All
- Keys: Clear, Reset, Attn, SysReq, PA1..PA3, PF13..PF24
- Help: About, Wire Log (toggle, opens folder)

Screen control fills the center; status bar at the bottom.

### 6.3 Screen control

Custom control overriding `Render`. Computes the largest cell size that fits its bounds at the font's aspect ratio, letterboxes, and draws each row as glyph runs grouped by attribute (a few dozen draw calls for 80x24, not one per cell). Handles background color, underline, reverse, blink on a timer, cursor, and selection highlight. Invalidates on each snapshot. Minimum window size prevents unreadable cells.

### 6.4 Colors

Fields with extended color use the IBM color names directly. Fields without extended color use the classic 3279 base mapping: unprotected normal green, unprotected intensified red, protected normal blue, protected intensified white, on black. Not user-configurable in v1.

### 6.5 Keyboard

Key events pass through one default keymap table to a `TerminalKey`, else fall through to Avalonia's text input event (so dead keys and IMEs behave) and become `TypeTextAsync`. Defaults:

| Physical | Terminal key |
|---|---|
| Enter | Enter |
| F1..F12 | PF1..PF12 |
| Shift+F1..F12 | PF13..PF24 |
| Escape | Reset |
| Tab / Shift+Tab | Tab / BackTab |
| Insert | Insert toggle |
| Home | Home |
| End | EraseEof |
| Delete / Backspace | Delete / Backspace |
| Arrows | Up, Down, Left, Right |
| Page Up / Page Down | PA1 / PA2 |
| Ctrl+C, Ctrl+V (Cmd on macOS) | Copy / Paste |

The table is cross-checked against wc3270 and Vista TN3270 defaults during planning. Not user-editable in v1.

### 6.6 Mouse, selection, clipboard

Click moves the cursor. Drag makes a rectangular selection. Copy produces lines with trailing spaces trimmed, joined by newline. Paste calls `PasteTextAsync`.

### 6.7 Status bar

Rendered in the 3270 font so it reads as one instrument with the screen. Each state shows the font's own OIA glyph (from its private use area, code points taken from the font's documentation) followed by a plain word or phrase:

- Connection state in words, with a lock glyph and TLS state ("TLS, certificate verified" / "TLS, certificate not verified")
- Keyboard state: "Ready", "Waiting for host", "Protected field, press Esc to reset", etc.
- Insert indicator
- Cursor row and column
- Model and LU name

### 6.8 Dialogs

- Profile editor: plain form for the fields in 4.5.
- File transfer: direction, local file picker, host file name, host type (TSO, VM, CICS), text or binary, CRLF and remap toggles; TSO allocation fields under an Advanced expander with sane defaults. Progress dialog with byte count and Cancel.
- Connection failure, unverified certificate, backend fault: plain messages. Certificate failure offers "connect anyway" once with an option to save that choice to the profile. Backend fault links to the wire log.

## 7. Error handling, consolidated

- Backend fault: message with stderr tail and wire log link; window returns to Disconnected with Reconnect available.
- Host unreachable, connection refused, TLS failure: distinct plain messages.
- Certificate verification failure: connect-anyway prompt, optionally persisted to the profile.
- File transfer errors: the host's IND$FILE message shown verbatim.
- Missing or non-executable b3270 at startup: fatal, with what to check.
- Nothing fails silently; no raw JSON is ever shown to the user.

## 8. Building b3270 and packaging

### 8.1 Targets

macOS arm64 and x64; Linux x64 and arm64; Windows x64 and arm64. All six in v1.

### 8.2 Native build

- x3270 source pinned as a git submodule at a release tag. Bumping is a deliberate PR that reruns the full matrix.
- One script per OS under `native/build`. macOS links OpenSSL statically and verifies with `otool -L` that only system libraries remain. Linux does the same with `ldd`, building on the oldest supported glibc. Windows uses the upstream MinGW cross-build from Linux (Schannel, no OpenSSL).
- The verification step is the CI gate: a b3270 with a non-system dynamic dependency fails the build.

### 8.3 Feasibility spike (before the implementation plan commits)

Throwaway: build a dependency-free b3270 on the developer's Mac, run it by hand in JSON mode, connect to the MVS instance plain and over TLS, and save the wire log. This proves the packaging story and yields the first replay fixture. Fallback if static OpenSSL is unworkable on some target: TLS terminated on the .NET side with `SslStream`, b3270 talking plaintext to a loopback proxy.

### 8.4 Publish and release

`dotnet publish` self-contained per runtime identifier with the matching b3270 in the runtime native folder. macOS: app bundle in a zip; signing and notarization deferred, release notes carry the right-click Open instruction until then. Linux: tarball. Windows: zip; installer is v2. Distribution via GitHub Releases; package managers after v1 is proven.

### 8.5 CI

GitHub Actions: a native build job per OS producing b3270 artifacts; a .NET job that builds, runs unit, replay, and headless UI tests, then publishes all six targets consuming the native artifacts; a tagged release job that uploads them. The integration lane runs on a schedule or by label against a Hercules TK4-/TK5 container.

## 9. Testing

- Core unit tests: snapshot construction, profile round trips, host string building, key table.
- Backend replay tests: wire-log fixtures drive a fake process (fixture as stdout, stdin captured); assertions on snapshots, cursor, status, and exact actions written. Every field bug adds a trimmed fixture.
- App tests: screen control and view models against a fake session serving scripted snapshots, on Avalonia's headless platform. Covers scale-to-fit math, selection to clipboard text, keymap dispatch, status wording. No screenshot comparison in v1.
- Integration lane: opt-in via `LIZTERM_TEST_HOST`. Logs on to TK4-/TK5, runs a known TSO command, checks the screen, and does a small IND$FILE round trip both ways.

## 10. Out of scope for v1

Printer sessions (pr3287), scripting and macros, HLLAPI or automation API, DBCS UI concerns, editable keymaps, themes and font choice, tabs, Windows installer, macOS notarization, package-manager distribution.
