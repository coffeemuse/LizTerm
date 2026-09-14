# LizTerm.Core

Notes for working in this project. The root `CLAUDE.md` has the rules that apply everywhere (the dependency rule,
snapshots, threading, zero-based coordinates). Core depends on the BCL only and never names Avalonia or b3270.

## Model

- `ConnectionState` is declared in a convenient order, not b3270's: the fixtures go tcp-pending, telnet-pending,
  tls-pending, and back to telnet-pending. Group states with `IsConnected()` (safe because the Connected members are
  declared last) and `HasSocket()` (spelled out, because the states past the TCP connect are not contiguous). Never
  read the declaration order as a progression.
- Cells store what b3270 *renders* — foreground, background, `CellRendition` flags — not raw 3270 field attributes.
  Protected and numeric status are not modeled: b3270 enforces field rules and reports violations through the
  keyboard lock (`oerr protected` etc.).
- `Cell.SameStyleAs` (foreground, background and rendition equal, whatever the character) is the one
  run-segmentation predicate. `TerminalScreen`'s renderer and `ScreenHtml`'s capture both use it, so they cannot
  disagree about where a run ends.
- `ScreenBuffer` is the backend's mutable grid (single writer); `Snapshot()` clones it into an immutable
  `ScreenSnapshot` that carries the cursor. `ScreenSnapshot.ToText()` is the plain-text capture, and `HasBlink` is
  computed once, where the cells are already in hand, so nothing rescans the grid for blink on every screen.
- `ScreenRegion` is a selection: inclusive, zero-based, always normalized.
- `ScreenSearch.Find` is the Find scan: case-insensitive, in reading order, over one entry per *character* rather
  than per cell so a DBCS match cannot begin or end mid-character. It runs against one immutable snapshot, so it
  needs no locking and keeps nothing between calls.
- `AppPaths` owns the per-OS config root, with `profiles`, `logs`, `settings.json` and `tags.json` beneath it.

## `IEmulatorSession`

- Bound to one `SessionProfile` at construction; `ConnectAsync` takes no host.
- `ConnectAsync(ConnectOptions?, CancellationToken)`:
  - A cancelled token ends the attempt with `OperationCanceledException` and leaves the session reusable.
  - `ConnectOptions(VerifyCertificate, Pin)` overrides the profile's verification and pin for one attempt.
  - `ConnectionFailedException.CertificateVerificationFailed` marks the text b3270 sends for an unverifiable
    certificate.
  - After a failed or cancelled connect it waits for `Disconnected` before throwing, bounded by `DisconnectTimeout`
    (5 s): b3270 answers the Connect run before it reports `not-connected`, and on a real gateway that report lags
    by up to a few seconds. Every wait a cancel depends on is bounded the same way, so a wedged engine cannot hold
    an attempt open past its own cancellation.
  - After disposal it throws `ObjectDisposedException` rather than starting a new engine, so a call that outlives
    its window cannot leave an engine running unowned. `FakeEmulatorSession` mirrors this.
- `TransferAsync(FileTransferRequest, IProgress<long>?, CancellationToken)` is one call. It completes when the
  transfer ends and returns a `FileTransferResult` whose `Message` is the engine's or host's final text verbatim —
  success, failure or cancel. (b3270 reports a cancel as a failure reading "Transfer canceled by user"; a host
  failure landing in the same moment keeps the host's text.) The byte count travels only through the progress
  callback. It throws only for non-outcomes: `InvalidOperationException` (never started, or a transfer already
  running), `OperationCanceledException` (only for a token already cancelled on entry), and
  `BackendUnavailableException` (the engine has died, or dies mid-transfer).
- `FileTransferRequest.Validate()` checks only what would be refused outright. Fields that do not apply to the
  direction, mode or host type are ignored downstream, never errors.
- `Engine` names the binary, its source — `Bundled`, `Override`, or `Unknown` — and, after the hello, its version.
  `Unknown` means *absent*: nothing anywhere the locator looked, which About renders as "not found" rather than
  borrowing a provenance. A binary that is present but unusable keeps its own path and source (see `SessionFactory`
  in `src/LizTerm.App/CLAUDE.md`).
- `WireLogPath`, `StartWireLog` and `StopWireLog` make the wire log a session capability that survives an engine
  restart.

## Profiles

- `SessionProfile.PinnedCertificate` is a `CertificatePin` — SHA-256 fingerprint as colon-separated upper-case hex,
  subject, and the PEM chain, leaf first — or null. How the pin, verification and trust anchors combine on a connect
  is the backend's rule (`B3270Session.ConnectAsync`, in `src/LizTerm.Backend.B3270/CLAUDE.md`).
- `DestructiveBackspace` defaults to true. Every x3270-family default keymap erases; the old belief that x3270
  defaults to cursor-left came from the name of its `BackSpace()` action.
- The profile JSON writes every field, so a saved `false` survives a change of default.
- `ProfileStore` addresses a profile by the name **inside** its file, never by `FileNameFor(name)` alone. A file
  renamed by hand, or copied from a platform whose invalid-character list differs, is still that profile's;
  addressed by the computed name, its star and Delete were silent no-ops and every save wrote a second file.
  `FileNameFor`'s file is only tried first. A file holding the name exactly wins wherever it sits, and only then one
  holding it ignoring case (a case-only rename), so `MVS` and `mvs` in two files never act on each other's file. A
  profile no file holds gets that name, or `name (2).json` and on when a file is already there, because
  `FileNameFor` is not one-to-one (`a/b` and `a:b`) and the file there may be another profile or one the user can
  still repair. When two files hold one name, every operation picks the same one: `FileNameFor`'s, else the first by
  ordinal file name. `LoadAll` lists only that one, because the picker and `TagMaintenance` act on a row by its name,
  so a row for the other file would star, edit and delete the first. `Save` throws, writing nothing, when a file it
  reads on the way cannot be opened: that file may be the profile's own, and an edit written elsewhere would sit
  behind it once it opens again. Saving under a name a profile already holds still overwrites it, which is what the
  picker's New and a rename onto an existing name have always done.
- `SessionProfile.Tags` is a `TagSet`, not a list, and that is load-bearing: a record's synthesised `Equals`
  compares a collection member by **reference**, so a list would make a profile read back from disk unequal to
  the one written — which `ProfileStoreTests` asserts it is. `TagSet` is a struct with hand-written value
  equality. Anyone adding another collection field to a record here needs the same treatment; hand-writing
  `Equals` on the record instead would silently stop covering each field added after it.
- Tag **names** live on the profile; a tag's **colour** lives in `TagRegistry`, loaded from `tags.json`, so one
  tag is one colour everywhere. `FAVORITE` is reserved: always present, always gold, never written to the file,
  and an entry for it in a hand-edited file is ignored. Reconciliation — registering a name the file does not
  know — belongs to the App layer, never to `ProfileStore.LoadAll`, because a load must not write.
  `TagMaintenance.Load` registers in memory so Manage Tags can list a tag only a profile knows, and writes nothing;
  the action that follows writes the registry anyway.
- `TagMaintenance` (#88) is the one place a tag changes across profiles. Each action reads the profiles and
  `tags.json` fresh, writes the carriers and then `tags.json`, and stops at the first failed write. A plain rename
  first defines the new name beside the old one, in the old colour, so wherever it stops both names are one colour
  and the retry is a merge that keeps it — a departure from the Manage Tags spec's table (4.2), which wrote that
  pair only after a partial failure and so left a crash between two writes with two colours for one tag. A merge,
  a case-only rename and a delete have nothing to prepare. It compares `TagSet.Names` **ordinally**, never with
  `TagSet.Equals`: equality ignores case, so a case-only rename would look unchanged and never be written. Its
  tests force a failed write by putting a directory at the file's `.tmp` path, which both stores write first.

## Settings (`LizTerm.Core.Settings`)

- `AppSettings` is the app-wide record: positional, a default on every parameter, and **flat by policy**, because
  `SettingsLayers.Merge` overlays top-level keys and would replace a nested object whole. `CrosshairMode` lives
  here because the record names it. The enum is written by name.
- The user file is a **sparse overlay**. `SettingsStore.Update(change)` applies the change to what is on disk
  *now* (re-read first, as `ProfileStore.Update` does) and writes exactly the keys the file already held plus the
  keys that now differ from the layers beneath (`SettingsLayers.UserDocument`). A key never touched stays absent
  and follows the default; one set once stays pinned, even set back to the default. Known keys are rewritten
  from the record, so a bad value heals on the first save; unknown keys are copied verbatim, so a newer build's
  key survives an older build saving.
- `SettingsLayers.Read` drops a key whose value will not deserialise and keeps the rest, so a hand-edited typo
  costs that key alone. `Load` never throws. `Update` throws `InvalidDataException` naming the file when the
  file is not a JSON object, rather than overwrite something the user may be editing.
- `SettingsLayers` is pure; every merge and pinning case is a plain unit test. Only the user file is wired: a
  system layer is one more entry in `Merge`'s list, and an environment variable would be the topmost layer.

## Security (`LizTerm.Core.Security`)

BCL-only, which is why these live in Core, where the integration tests can use them too.

- `ICertificateFetcher` / `SslStreamCertificateFetcher`: one handshake that captures and accepts the chain. It keeps
  only the leaf plus the host-sent extras from the chain policy's `ExtraStore` — never a root the chain engine
  supplied from the system store — is bounded at 10 s, and throws `IOException` "No TLS answer ..." on timeout.
- `CertificateReader`: fingerprint, PEM, certificate count, and whether a chain is pinnable. .NET validates the
  chain with its own self-signed members as the only trust roots, which is what OpenSSL will do with the pin file.
- `ITrustAnchorSource`: `SystemTrustAnchors` reads the OS root store (`LocalMachine` and `CurrentUser`) once per
  instance and exports it as PEM; a store that will not open contributes nothing. `NoTrustAnchors` is the default,
  which leaves an engine on its own trust.
