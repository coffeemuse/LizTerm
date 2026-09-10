# LizTerm tier-1: quick connect, oversize, keep-alive, and auto-reconnect

Date: 2026-09-10. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessor:
`2026-09-09-lizterm-the-screen-design.md` (merged to `main` as aa89400 on 2026-09-09).
Status: approved in discussion on 2026-09-10; engine probes run and folded in the same day (§9.1);
awaiting review of this text.

## 1. Purpose

The four remaining `tier-1` issues, all about *reaching* a host rather than looking at one: #29 (Quick Connect
without saving a profile), #30 (oversize screen geometry), #37 (keep-alive), and #28 (auto-reconnect after a
host-initiated disconnect).

They are one milestone because three of them add a field to `SessionProfile` and a row to the profile editor,
two of them add an argument to the argv `B3270Session` already builds, and the fourth — #29 — is the one that
asks what all of those settings mean when there is no profile at all. Shipped separately, the profile schema
and its JSON round trip are opened four times and #29's central question is answered twice.

The engine supports every one of them today. Nothing here is blocked on x3270, and nothing here adds a member
to `IEmulatorSession`.

Vista TN3270's feature list is the behavioural reference for #29, #30 and #28, as the v1 design spec directs
where the spec is silent. Every engine claim below was taken from b3270 4.5ga6's source or from a live `Set`
round trip recorded in the issues, and §9 lists the three that must be re-verified live before implementation
rather than trusted here.

Decisions taken in the brainstorm on 2026-09-10, each justified in full by the section that owns it:

1. **All four in one milestone**, with #28 sequenced last so its lifecycle work lands on a green base (§1).
2. **#28 takes b3270's `reconnect` toggle only, never `retry`**, armed only after a connect has succeeded
   (§6.1). The first-connect path is therefore untouched by construction.
3. **Keep-alive defaults to 60 seconds — on. Auto-reconnect defaults to off** (§2.2). Both defaults are
   retroactive to every profile already on disk, which is the mechanism, not a side effect.
4. **Quick Connect connects with the record's defaults**, and the session window gains **Save as
   Profile...** so an ad hoc connection is the start of a profile rather than a dead end (§7, §8).
5. **Minimum surface**: three flat fields on `SessionProfile`, one new pure Core type, two new editor rows,
   one new picker row, one new menu item. No new interfaces, and no restructure of the editor beyond
   reordering its rows (§10, §11).

## 2. The schema

### 2.1 Three flat fields, appended

`SessionProfile` gains three parameters at the end of its positional list:

```csharp
int KeepAliveSeconds = 60,
bool AutoReconnect = false,
string? Oversize = null
```

Appended rather than inserted, so every existing positional construction and `with` expression in `src/` and
`tests/` keeps compiling unchanged.

Flat rather than nested under a `ConnectionSettings` record. Nesting was considered and rejected: the record's
XML comment records that `ProfileJsonContext`'s source generator honours *constructor defaults* for a field
missing from a file, where it ignores an init-only property's initializer and reads the CLR default instead. A
nested record has no constructor default that survives a missing JSON object — it arrives null and needs a
`?? new()` at every read site — which trades a documented, tested mechanism for an undocumented one, in a
milestone whose whole delivery vehicle is that mechanism.

### 2.2 The defaults are retroactive, and that is the point

Because a missing field reads its constructor default, **every profile already on disk gains a 60-second
keep-alive the moment 0.4.0 runs**, without being rewritten. No migration, no rewrite pass, no version stamp.

That is a deliberate choice and not a convenience:

- **Keep-alive on (60s)** because the failure it prevents is invisible. A NAT entry expires or a firewall reaps
  an idle connection; the window keeps showing the last screen it received, so the session *looks* alive until
  the next keystroke fails. A TELNET NOP costs one small write a minute and prevents that entire class. A user
  who never finds the checkbox is exactly the user this protects.
- **Auto-reconnect off** because it changes what the user *sees*, and because a reconnect is not free: it
  re-establishes the host session rather than merely the socket, so an LU can move. That is a thing to opt into.

The two interact, which is why they ship together: a working keep-alive means auto-reconnect fires against
genuine host and network failures rather than against idle reaping it would have prevented.

`SessionProfileTests` must assert this directly — a JSON file carrying none of the three fields reads back
`60`, `false` and `null`. That test *is* the keep-alive decision; without it the decision lives only in a
constructor signature that a later refactor could quietly change.

### 2.3 The one Core type: `OversizeGeometry`

`src/LizTerm.Core/Session/OversizeGeometry.cs`, beside `TerminalModel`, `CodePage`, `TerminalType` and
`PinMerge` — the pure, BCL-only, individually tested rules the previous milestone established as the home for
exactly this kind of decision. §4 gives its rules.

## 3. Where the settings reach the engine

`B3270Session.BuildArguments` (`src/LizTerm.Backend.B3270/B3270Session.cs:141`) is today:

```csharp
["-json", "-utf8", "-model", HostStringBuilder.ModelArgument(profile), "-codepage", profile.CodePage]
```

It gains two conditional pairs:

- `-oversize <geometry>` when `Profile.Oversize` is non-empty;
- `-set nopSeconds=<n>` when `Profile.KeepAliveSeconds` is greater than zero. **`-set name=value` is the only
  form**: `-nopSeconds 60` is rejected outright as `Unknown or incomplete option` (measured, §9.1).

Both are **omitted at their defaults**, because argv is evaluated once per process and b3270's own defaults
already match (`oversize` unset, `nopSeconds` 0). This is deliberately *not* the "send every toggle explicitly
every time" rule that `EffectiveTlsOptions` follows for TLS: that rule exists because a *connect* can inherit
the previous connect's settings within one engine process, and argv cannot.

`BuildArguments` stays a pure static, so both are asserted directly rather than through a running engine.

**Why argv and not a runtime `Set`** for these two: it needs no new protocol handling, and it sidesteps the
unverified question of whether a runtime `oversize` change takes effect on the next connect, on the next
process, or not at all. `reconnect` is the exception and must be a runtime `Set` — see §6.

## 4. #30: oversize screen geometry

### 4.1 b3270's real limit is an area, not a rectangle

`Common/ctlr.c` rejects an oversize when:

- either dimension is zero while the other is set;
- either dimension exceeds `MAX_ROWS_COLS` (`0x3fff`, 16383);
- **`columns × rows > MAX_ROWS_COLS`** — an area compared against the same linear constant;
- columns are below the model's columns, or rows below the model's rows.

The area rule is the one that surprises. At 160 columns the limit allows 102 rows (16383 / 160 = 102.4), and
Vista's advertised "200 lines by 200 characters" is 40,000 cells and is rejected outright. **Whatever the UI
says must describe b3270's limit, not Vista's claim.**

### 4.2 We refuse it, because the engine refuses it badly

b3270 reports these rejections with a `popup`, which reaches LizTerm as a `HostMessage` — an unexplained line
in the error banner and no connection, with nothing tying it to the field the user typed. `OversizeGeometry`
therefore validates in the editor, in our own words, against the model the profile has selected, and a profile
that fails validation cannot be saved.

Each rejection returns its own message naming the actual limit and the actual model, for example:

- `"160x103 is 16,480 cells; b3270 allows 16,383. At 160 columns the most is 102 rows."`
- `"Oversize must be at least 132 columns and 27 rows for model 5."`

### 4.3 The column/row order trap

**`TerminalModel.ToString()` renders `2 — 24x80`, which is rows × columns. b3270's `-oversize` takes columns ×
rows: `100x50` is 100 wide and 50 tall.** The Model drop-down and the Oversize field therefore sit adjacent in
the editor stating their geometry in opposite orders.

This is not a defect to tidy away in either direction:

- The drop-down's `24x80` is the conventional way to name a 3270 model's geometry, it is user-visible text
  landed by #45, and `CatalogueTests` asserts it exactly.
- `-oversize`'s `cols x rows` is the engine's format and the one every x3270 document a user might copy from
  uses. Silently swapping it would send a transposed geometry to the engine.

The resolution is labelling, not reordering. The field's placeholder reads `columns x rows, e.g. 132x43`, and
every message `OversizeGeometry` produces names which number is which — never a bare `132x43`. A future reader
tempted to make the two fields agree should read this section first.

### 4.4 Parsing

`OversizeGeometry.TryParse(string? text, TerminalModel model, out OversizeGeometry? geometry, out string? error)`:

- null, empty or whitespace is **valid** and means no oversize — the field is optional and blank is its
  default, not an error;
- otherwise the text is `<columns>x<rows>`, digits only on both sides, `x` accepted in either case;
- anything else is a format error naming the expected form;
- then the four rules of §4.1, each with its own message.

Pure, BCL-only, no I/O, no engine. `ToString()` returns `{Columns}x{Rows}`, which is what `BuildArguments`
passes to `-oversize`.

**A model outside the catalogue.** `TerminalModel.Find` returns null for a model number a hand-edited profile
invented, and the drop-down's own comment records that such a model has no geometry — which is why it renders
as a bare number rather than lying with `9 — 0x0`. `TryParse` has nothing to compare against in that case, so
it applies every rule in §4.1 **except** the model floor, and says so in no message. That is the honest
outcome: the format, ceiling and area rules are properties of b3270 and still hold, while the floor is a
property of a model we do not have. b3270 still refuses the combination at connect time through the same
`popup` path a hand-edited model already takes today, so nothing new is unguarded.

### 4.5 The screen side is asserted, not assumed

`screen-mode` already reports the resulting geometry and `ScreenBuffer` already resizes on it, so the UI should
need no change at all. No existing fixture exercises a non-model geometry, so that is a claim rather than a
fact: this milestone adds a trimmed replay fixture at an oversize geometry and asserts the resulting snapshot
dimensions, in the project's habit of proving the thing rather than reasoning about it.

## 5. #37: keep-alive

### 5.1 What it does, and the sentence that must reach the user

`nopSeconds` sends a **TELNET NOP** on an interval. That keeps the TCP connection and any NAT mapping alive,
which is exactly the network-layer drop it exists to prevent.

It is **not** 3270 data. A host application does not see it as terminal activity, and it will not stop TSO or
CICS logging an idle terminal off — those timers count real I/O. A user who reads "keep-alive" as "stops the
host timing me out" has been misled by the label.

The editor's label therefore carries the caveat inline, in the style the Keyboard row already established:

> Send a keep-alive every `[60]` seconds (0 = off; keeps the network connection open, does not prevent a host
> idle logoff)

### 5.2 Shape

One `SessionProfile` field, `KeepAliveSeconds`, `0` meaning off, default 60 (§2.2), passed as
`-set nopSeconds=<n>` in the argv (§3). Validation: a non-negative integer. No fixture: NOPs are outbound and
the inbound stream is unchanged, so `IndicationParser` learns nothing new.

## 6. #28: auto-reconnect

### 6.1 `reconnect` only, armed after success

b3270 has two independent toggles, both `false` by default:

- **`retry`** — keep retrying a connect that *failed* (`Common/host.c` arms `try_reconnect` after
  `RECONNECT_ERR_MS`);
- **`reconnect`** — reconnect after the *host* drops an established session, after `RECONNECT_MS` (2 s).

The two are not as separate as that reads, and the difference matters downstream. `host.c:643` sets
`host_retry_mode = appres.reconnect || appres.retry` on **every** `host_connect`, and the `NC_FAILED` branch
(`host.c:651-656`) re-arms `try_reconnect` at `RECONNECT_ERR_MS` whenever that mode is on. So with `reconnect`
alone, a reconnect *attempt* that itself fails is retried, and goes on being retried indefinitely — against a
host that stays down the engine cycles `Reconnecting → TcpPending → Reconnecting` every few seconds until
something disconnects it. That is arguably what auto-reconnect should do, and this milestone keeps it; what it
means is that `reconnect` is not confined to one attempt per drop. §6.4's command guards and the CA-file
lifetime in §3 both depend on that being true: an engine-driven attempt runs with no `ConnectCommand` in
flight, and it rebuilds its TLS context — reloading `caFile` — each time round.

**This milestone takes `reconnect` and does not take `retry`.**

`retry` is the half that collides with everything `ConnectAsync` is built on. That contract is deliberate and
carefully bounded: a failed Connect run means the attempt is over, which is what raises
`ConnectionFailedException`, what feeds `ICertificatePrompt` through
`ConnectionFailedException.CertificateVerificationFailed`, and what `SessionViewModel`'s 30 s `ConnectTimeout`
is measuring. With `retry` on, none of those three premises hold: the engine is already trying again behind a
certificate dialog the user is still reading.

So `reconnect` is armed with a runtime `Set(reconnect,true)` **only after `ConnectCoreAsync` has returned
successfully**. Before that moment the connect path behaves exactly as it does today, which is not a matter of
care during implementation but a property of when the toggle is written.

The state plumbing already exists and needs nothing: `"reconnecting"` is mapped at `B3270Session.cs:480`,
`ConnectionState.Reconnecting` is in Core, `HasSocket()` already excludes it, and `StatusFormatter:20` already
renders `Reconnecting to {host}`.

**No interface change anywhere.** `B3270Session` is bound to one `SessionProfile` at construction, so it reads
`Profile.AutoReconnect` itself. `IEmulatorSession` gains no member, `FakeEmulatorSession` gains nothing, and
the App learns about a reconnect through the `ConnectionChanged` event it already handles.

### 6.2 The real trap: `Disconnect` does not clear the reconnect intent

Measured against b3270 4.5ga6 on 2026-09-10 (§9.1). This section originally described a different trap — that
`DisconnectAsync`'s early-out on `Disconnected` would fire during the engine's two-second reconnect window.
**That premise is false, and the truth is worse.**

**b3270 reconnects from the user's own `Disconnect` action.** On an established session with `reconnect` on,
sending `Disconnect` moves the state straight to `reconnecting`, and the session is back up two seconds later:

```
t=1.42  SEND  Disconnect()
t=1.42  STATE reconnecting
t=3.44  STATE tcp-pending
t=3.44  STATE telnet-pending
t=3.46  STATE connected-3270      <- proxy records a second accept
```

The same holds for a `Disconnect` sent during a countdown already running. So with `AutoReconnect` on and no
disarm, **File > Disconnect does not work**: the session comes back two seconds later and the user is given no
reason. That is the most ordinary path in the application, not an edge case.

The fix is the one this spec already proposed, and it is now mandatory rather than defensive: **every path that
disconnects sends `Set(reconnect,false)` before the `Disconnect`.** Confirmed in the same run:

```
t=1.41  STATE reconnecting
t=1.46  SEND  Set(reconnect,false)
t=1.46  SEND  Disconnect()
t=1.51  STATE not-connected       <- one accept only; stays down
```

This applies to `DisconnectAsync` and to `TryDisconnectQuietlyAsync`, the cancel path.

**On the early-out itself.** `if (ConnectionState == ConnectionState.Disconnected) return;` turns out to be
unreachable during a reconnect, because the engine never reports `not-connected` while armed (§6.3). Placing
the disarm ahead of it is therefore no longer load-bearing for the reason first given — but it stays there
anyway: it costs one action on a path the user is waiting on regardless, and the alternative would rest on an
engine behaviour we have measured once and cannot enforce.

`DisposeAsync` needs no disarm: the Quit exchange ends the process, and a dead engine reconnects to nothing.

### 6.3 An armed drop never reports `not-connected`

The same run answered a question this spec had listed as a probe: on a host-initiated drop with `reconnect`
armed, **b3270 does not report `not-connected` at all.** It goes `connected-3270` -> `reconnecting` directly,
and on to `tcp-pending` two seconds later.

Two consequences, in opposite directions.

**Good for the UI.** The App never sees `Disconnected` during an auto-reconnect, so the status bar moves from
the live session to `Reconnecting to <host>` without flickering through `✕ Not connected`. `StatusFormatter`
needs nothing.

**Bad for `_disconnected`.** `SetConnectionState` completes that source only on `Disconnected`, and installs a
fresh one only when *leaving* `Disconnected`. An armed drop passes through neither, so the source stays as the
uncompleted one installed by the original connect, and a `WaitForDisconnectedAsync` arriving mid-reconnect
would wait out the full 5 s `DisconnectTimeout` before silently giving up.

The disarm fixes this too, and for a measured reason rather than an inferred one: disarming produces the
`not-connected` indication about 50 ms later (t=1.46 -> t=1.51 above), which completes the source and lets the
wait return promptly. So `DisconnectAsync` keeps its existing shape — disarm, `Disconnect`, then wait — and the
wait behaves as it does today.

### 6.4 The App bug this creates, and its fix

`ConnectionState.Reconnecting` becomes reachable for the first time. `IsConnected()` is false there, so after
#39's fix **Connect would be enabled mid-reconnect and Disconnect disabled** — precisely backwards, and an
offer the app cannot honour.

`SessionViewModel` gates Connect on `Disconnected` and Disconnect on not-`Disconnected`, rather than both on
`IsConnected()`.

**On the raw state, and not on a bool derived from it.** A guard written as `!HasSocket()` — or as
`!HasSocket() && state != Reconnecting` — is not the same rule: `HasSocket()` is also false for `Resolving`
and `TcpPending`, so both of those offer Connect and grey Disconnect. That was invisible for as long as those
two states were reachable only from inside `ConnectCommand`, whose `AsyncRelayCommand` disables itself for the
length of its run. §6.1's arming makes them reachable with no command running: per `Common/host.c:643`,
`host_retry_mode = appres.reconnect || appres.retry` is set on every `host_connect`, so against a host that
stays down the engine cycles `Reconnecting → TcpPending → Reconnecting` every few seconds unprompted, and a
derived guard flickers both menu items on that cycle. The view model therefore keeps the reported
`ConnectionState` itself as an observable — named `Connection`, since an `[ObservableProperty]` cannot take the
enum type's own name — and writes `CanConnect => !ConnectPending && Connection == Disconnected`,
`CanDisconnect => ConnectPending || Connection != Disconnected`. `ConnectPending` is in both because the state
is still `Disconnected` through the first moments of a manual connect.

## 7. #29: Quick Connect

### 7.1 Nearly all of it already exists

`StartupArguments.Parse` already reads the full ad hoc form `[L:][Y:][lu@]host[:port]` with every awkward case
decided and tested, and `Resolve(profiles)` already synthesizes a complete `SessionProfile` from it — name
`lu@host:port`, port defaulted to 992 or 23 by TLS — while giving an exact saved-profile name match precedence,
because the ad hoc forms overlap legal profile names.

Quick Connect is therefore a text box, a button, and:

```csharp
StartupArguments.Parse([text]).Resolve(Profiles)
```

The box inherits the command line's tie-break rule for free, and cannot drift from it. `StartupArguments` lives
in `LizTerm.App/Startup/` and the picker is App, so nothing moves.

### 7.2 The connection is not saved

An ad hoc connection writes nothing to `ProfileStore`. Writing a profile stays a deliberate act — §8 is how it
becomes one.

Its non-host settings are the record's defaults, which after §2 includes a 60-second keep-alive and no
auto-reconnect.

### 7.3 Enter

`ProfilePickerWindow.axaml:18`'s Connect button carries `IsDefault="True"`, so Enter anywhere in the window
connects the *selected profile* — including Enter in a Quick Connect box the user has just typed a host into.

Rather than stripping `IsDefault`, the text box gets an `OnQuickConnectKeyDown` handler that routes Enter to
the quick-connect command and marks the event handled. This is the shape `SessionWindow.OnFindBoxKeyDown`
already uses to keep the find bar's typing away from `Keymap` and the host: handled at the box means the
window-level default button never sees it. Enter with focus in the list keeps working as it does today.

### 7.4 A bare word is ambiguous, and the message says so

`Parse` reads a bare word as a host only when it contains a dot or is `localhost`, because on the command line
a bare word is ambiguous with a profile name. That ambiguity is just as real in this box, so the rule stays.

The consequence is that typing `tk5`, with no saved profile of that name, resolves to nothing — and a LAN host
genuinely called `tk5` is a plausible thing to type. The fix belongs in the message rather than the parser: the
inline error teaches the port form (`tk5:23`) instead of merely refusing. A syntax error shows inline in the
picker, never as the console usage line, which no one running the GUI has a console to read.

## 8. Save as Profile...

A **File → Save as Profile...** item on the session window, so an ad hoc connection is the start of a profile
rather than a dead end. It opens the existing profile editor pre-filled from the session's profile — every row
already present, including the new ones — and saves through `ProfileStore`.

- In **both** menus, or `NativeMenuTests.The_native_menu_matches_the_classic_menu_item_for_item` fails, which
  is that guard working.
- With a `Command` or a `Click` handler, per
  `NativeMenuTests.Every_native_item_can_actually_be_activated`; if Click-driven it binds its own `IsEnabled`,
  since it gets none of the greying a command's `CanExecute` gives the classic item.
- With **no `Gesture`**. File is not Edit, and a `NativeMenuItem` gesture is an AppKit key equivalent
  dispatched ahead of the key window's responder chain.
- Carrying the session's `_pinOverride` when one was taken this run, so a certificate trusted in this window
  survives into the profile it becomes. Note that in practice this can only be a **saved** profile being
  duplicated, never an ad hoc session: `_pinOverride` is set only where `canPin` is true, and `canPin` requires
  `savedTlsProfile`, which requires the `saveProfile` callback `App.OpenSession` passes only for
  `fromStore: true`. An ad hoc session's certificate prompt offers Connect Anyway but not "Trust this
  certificate for this profile", precisely because there is no profile to write it to. The fold is still the
  right code — it is what keeps a duplicated profile's pin — but it is not what carries an ad hoc trust
  decision anywhere.

The editor callback is injected into `SessionViewModel` the way `ICertificatePrompt`, `IFilePicker`,
`IFolderOpener` and `ITextClipboard` already are, keeping the view model free of Avalonia dialog types and the
item assertable with a fake.

Enabled always, not only for ad hoc sessions: for a saved profile the item reads as "duplicate this one", which
is useful and costs nothing to allow.

**A name that already exists overwrites it**, because `ProfileStore.Save` is keyed on the name and the picker's
own New command already behaves exactly this way. Deliberately not a new rule for this one item: the editor is
where the user sees and changes the name, and inventing a collision prompt here — while New silently overwrites
two windows away — would be the inconsistency, not the guard. If overwriting should prompt, it should prompt in
both places, on its own evidence, in its own change.

## 9. Testing and verification

### 9.1 The three live probes, answered 2026-09-10

Run against b3270 4.5ga6 (Homebrew, the pinned version) before this spec was finalised, because §6.2's fix
rested entirely on the first of them. All three are answered; the results are already folded into §3, §6.2 and
§6.3, and are recorded here as the evidence behind them.

**Method, which is reusable.** A host-initiated drop needs a host that drops, so the probe put a TCP proxy in
front of the live TN3270 host on `localhost:2323`, let b3270 establish a real 3270 session through it, then
closed the proxy's connection — a genuine host drop from the engine's point of view, and repeatable without
bouncing Hercules. The same harness serves the manual pass in §9.5.

1. **Does `Set(reconnect,false)` cancel a reconnect already counting down? — Yes.** The control (armed, nothing
   disarms it) went `reconnecting` -> `tcp-pending` -> `connected-3270` in 2 s with a second proxy accept. The
   test, disarming the instant the drop was reported, produced `not-connected` 50 ms later, one accept, and no
   return.
2. **Does an armed drop report `not-connected`? — No.** `connected-3270` -> `reconnecting` directly. This
   answered a question that was asked as an aside and changed §6.3; see there.
3. **Does `-set nopSeconds=60` on the command line take? — Yes.** The `initialize` block reports
   `nopSeconds: 60` and a runtime `Set(nopSeconds)` reads back `60`, against `0` for a control with no flag.
   `-nopSeconds 60` is **not** a valid option (`Unknown or incomplete option`), so `-set name=value` is the
   only form (§3).

**A fourth question the probe raised and answered**, which no issue had asked and which is the most consequential
result of the three runs: b3270 reconnects from the user's own `Disconnect` action, so without a disarm
`AutoReconnect` silently breaks File > Disconnect. §6.2 carries the transcript.

### 9.2 Core

- `OversizeGeometryTests` — one case per rejection arm, asserted on that arm's own message: the format error,
  the zero-dimension case, the per-dimension ceiling, the area limit (160x102 passes, 160x103 fails, 200x200
  fails), and the model floor for each of models 2 through 5. Blank parses as "no oversize" and is valid. Both
  inclusive boundaries are asserted from the *accepting* side too — exactly `16383` in one dimension (paired
  with `1`, against a model with no floor, so neither the area limit nor a floor fires first) and a geometry
  exactly equal to the model's own (`80x24` on model 2) — since an off-by-one in either comparison passes every
  refusal case unchanged.
- `SessionProfileTests` — a JSON file carrying none of the three new fields reads back `60`, `false`, `null`
  (§2.2), and a saved `0` keep-alive survives a round trip rather than reading back as the default.

### 9.3 Backend

- `BuildArguments` asserted directly: `-oversize` present when set and absent when null; `-set nopSeconds=60`
  present at the default and absent at `0`.
- `FakeB3270Process`-driven tests for #28:
  - `reconnect` is armed only after a successful connect, and never on a failed one;
  - a host-initiated drop produces the reconnect state sequence (`gateway-login-tls.jsonl` already carries a
    host-initiated disconnect to trim from);
  - **an explicit Disconnect on an armed, established session stays disconnected** — the §6.2 finding, written
    as a failing test before the disarm exists, since without it the engine reconnects from the user's own
    Disconnect;
  - the same for a Disconnect issued during a countdown already running;
  - a Disconnect on an armed session completes without a `DisconnectTimeout` stall, because the disarm is what
    produces the `not-connected` the wait is waiting for — §6.3.
- A trimmed replay fixture at an oversize geometry, asserting the resulting snapshot dimensions (§4.5), with
  its entry in the fixtures README.

### 9.4 App

- Quick Connect's three outcomes: text naming a saved profile connects that profile; an ad hoc host connects
  without saving; a syntax error shows inline and connects nothing. Plus: Enter in the box does not reach the
  list's default button.
- Editor validation: a bad oversize blocks Save with its own message; changing the model re-validates an
  oversize that was legal under the old one.
- Connect and Disconnect enablement across `Disconnected`, `Reconnecting`, `Resolving`, `TcpPending` and
  connected (§6.4) — as a table over the states, since `Resolving` and `TcpPending` are the two a guard derived
  from `HasSocket()` gets wrong and the ones an engine-driven reconnect cycles through.
- Menu parity and activation for Save as Profile.

### 9.5 Manual, against a live host

CI cannot answer these:

- A real host drop (stop and restart the Hercules listener) with auto-reconnect on: the status bar reads
  `Reconnecting to <host>` and the session comes back.
- The same, with Disconnect pressed during the two-second window: it stays disconnected.
- **The same again for a profile whose certificate is pinned**, with auto-reconnect on: the session comes back,
  rather than failing over and over with `CA database load ... failed` in the error banner. Nothing in CI reaches
  this end to end — it needs a real OpenSSL engine reloading a real `caFile` on a connection it started itself —
  so this checklist is the only place the whole path is exercised. The failure it looks for is the one an
  ephemeral pin file caused: see §3 on the CA file's lifetime.
- While a host that stays **down** is being reconnected to (kill the listener and leave it dead): File →
  Disconnect stays enabled and Connect stays greyed for the whole `Reconnecting → TcpPending → Reconnecting`
  cycle, rather than flickering between them every few seconds (§6.4).
- An oversize profile against a host that accepts one, confirming the screen renders at the new geometry.
- Quick Connect to the live host by `host:port`, then Save as Profile, then connect the saved profile.

## 10. Out of scope

- **`retry`** (§6.1). Retrying a connect that never succeeded is a separate decision with its own evidence,
  and its own interaction with the certificate prompt to design.
- **A global preferences window** — #19. Every setting here is per profile, matching everything else in
  `SessionProfile` today.
- **Restructuring the profile editor into labelled sections.** Considered; the reordering in §11 gets most of
  the benefit for a four-row re-index. A full restructure should ride with #19 or #43, on their evidence.
- **A model or code-page control beside the Quick Connect box.** §8 is the answer to a wrong default.
- **Changing a live session's model or geometry.** Vista allows it; LizTerm binds one session to one profile at
  construction, and that is a v1 design decision, not an oversight.
- **#12 (SNI)**, **#46 (Window menu)**, **#47 (the bell)** — filed, unrelated, not this milestone.

## 11. The editor's two new rows

Row order becomes:

| Row | Label | Contents |
|---|---|---|
| 0 | Name | text |
| 1 | Host | text |
| 2 | Port | text |
| 3 | Security | Use TLS, Verify, pin + Forget |
| 4 | **Connection** | **keep-alive seconds + auto-reconnect checkbox** |
| 5 | Model | model drop-down, Extended |
| 6 | **Oversize** | **text, `columns x rows, e.g. 132x43`** |
| 7 | Code page | drop-down |
| 8 | LU name | text |
| 9 | Keyboard | destructive backspace |

The reach-the-host settings group above the terminal settings, which is why Connection is inserted at 4 rather
than appended: it costs a re-index of four existing rows and buys the grouping without a restructure. The grid
already declares nine `Auto` rows and uses eight, so it declares ten.

Both new rows follow established shapes: Connection stacks its two controls the way Security already stacks
three, and the keep-alive caveat is inline in the label the way the Keyboard row's parenthetical is. The window
stays `Width="480" SizeToContent="Height" CanResize="False"` and grows by roughly two rows.

Existing editor tests reach controls by `x:Name` (`ModelBox`, `CodePageBox`, `BackspaceBox`) rather than by
`Grid.Row`, so the re-index breaks nothing.
