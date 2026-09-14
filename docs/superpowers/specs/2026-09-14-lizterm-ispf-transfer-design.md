# LizTerm: IND$FILE transfers from inside ISPF — design

Date: 2026-09-14. Parent spec: `2026-09-03-lizterm-v1-design.md`. Issue: #109, requested by @mgrossmann.
Status: brainstormed and approved section by section with Robert on 2026-09-14, after a spike against MVS/CE the
same day. §4.5 was corrected later that day, while planning, and Robert chose its new decision.

## 1. Purpose

File > IND$FILE Transfer... starts a transfer by typing the IND$FILE command at the cursor, so today the cursor has
to be at a TSO `READY` prompt or a bare command line. From inside ISPF that means leaving ISPF, or going to ISPF's
TSO command panel first. Another TN3270 client lets its users start a transfer from any ISPF command line, and the
requester has grown used to that convenience.

This spec adds a host type, **ISPF (MVS)**, to the File Transfer dialog. With it, LizTerm types `TSO` ahead of the
command, which tells ISPF to hand the rest to TSO exactly as it would at `READY`.

**Definition of done:** on MVS/CE, a user whose cursor is on the ISPF primary menu's `Option ===>` line chooses
ISPF (MVS), sends a file and receives it back, and ISPF shows its menu again afterwards. This works with the engine
LizTerm ships on every platform.

## 2. Why the issue's own mechanism cannot work

The issue proposed typing `TSO ` at the cursor through `TypeTextAsync` and then calling `Transfer`. In b3270 4.5ga6
that `TSO ` is erased before the host ever sees it:

- `Transfer` builds `IND$FILE GET|PUT ...` and then calls `kybd_prime()` (`Common/ft.c:775`). `kybd_prime`
  (`Common/kybd.c`) moves the cursor to the start of the unprotected field it is in and **blanks the whole field**,
  and only then does `emulate_input` type the command and Enter.
- LizTerm cannot type the whole `TSO IND$FILE ...` itself either. If it did, the host would start the transfer, but
  b3270 ignores the host's file-transfer structured fields when no `Transfer` is pending
  (`Common/ft_dft.c:98`, "no transfer in progress").
- No existing `Transfer` keyword can carry a prefix. `OtherOptions`, the closest, is appended *after* the options.
- And a keyword the engine does not know is **silently ignored**, not refused: in `parse_ft_keywords` the
  `Unknown option` check sits inside the loop over the known keywords, so it never runs once that loop ends without
  a match. A stock 4.5ga6 b3270 given `commandprefix=TSO` goes straight on to open the local file. (Found while
  planning; §4.5 is the consequence.)

The same code is unchanged in x3270 4.6pre1. So the prefix has to come from inside the engine.

## 3. What the spike established

Run on 2026-09-14 against Robert's MVS/CE host (Wally ISPF V2.2.000, IND$FILE 2.0.5), driving b3270 through its
script port. Every transfer that ran used DFT mode, and every file came back byte-identical.

| Test | Engine | Started from | Result |
|---|---|---|---|
| 1 | stock b3270 4.5ga6 | ISPF option 6, the TSO command processor's `===>` | send and receive both work |
| control | stock b3270 4.5ga6 | ISPF primary menu, `Option ===>` | "Transfer did not start within 30s"; ISPF only repaints |
| 2 | scratch build, `TSO ` prepended in `ft.c` | ISPF primary menu, `Option ===>` | send and receive both work |
| 2c | the same scratch build | Utility Selection Menu (option 3), `Option ===>` | receive works |

Also observed:

- The trace shows the scratch engine typing `TSO IND$FILE PUT SPIKE109.PRIM ASCII CRLF` into the `Option ===>` field.
- After a prefixed transfer ISPF returned straight to its panel with **no `***` pause**: the host's closing message
  arrives through the transfer's own `FT:MSG` structured field, not as TSO line output. LizTerm needs no extra
  handling after a transfer. (`TSO DELETE` from the same line did stop at `***`.)
- The primary menu's `Option ===>` field is 65 positions wide. `TSO ` costs 4, and b3270's existing "not enough room
  in the input field" check counts it, so only a long host name plus several allocation options could overflow, and
  that error already reaches the user.
- **Wally ISPF never answers a Clear** on its panels; the keyboard stays locked until Reset. PF3 on the primary menu
  ends ISPF but leaves the panel on screen with `READY` written over row 1. Both matter to the live test (§8.4).
- The spike's artifacts were b3270 traces, not LizTerm wire logs, so the replay fixture still has to be recorded
  through LizTerm (§8.3).

## 4. Decisions

**4.1 Carry a patch in the repo, and document ISPF option 6.** The patch is maintained whether or not upstream
adopts it; Robert submits it upstream himself once this work is in. Option 6 goes in the user guide as well, although
the requester already knew it. Rejected: waiting for upstream only, because the timeline is not ours; documentation
only, because it does not give the requester what they asked for.

**4.2 A host type, not a check box.** The issue sketched an "ISPF mode" check box in the dialog's main panel. Robert
chose an `ISPF (MVS)` entry in the existing Host type drop-down instead, so the dialog keeps one control for "what
am I talking to".

**4.3 The engine gets a small free-text `CommandPrefix` keyword.** It mirrors `OtherOptions`: free text, typed ahead
of `IND$FILE` rather than after the options. LizTerm's ISPF (MVS) sends `host=tso` plus `commandprefix=TSO`.
Rejected:

- **A new `Host=ispf` in the engine**, matching the drop-down one for one. Every TSO test in the engine would also
  have to accept ISPF: about 13 sites in `ft.c` alone, plus about 7 in `icmd.c` and 10 in the Motif dialog for the
  other front ends. More to rebase on every x3270 bump.
- **A TSO-only boolean such as `Ispf=yes`.** ISPF-specific, and no smaller than the prefix.

**4.4 The patch covers only what LizTerm's engine uses.** Two files: `Common/ft.c` and `include/ft_private.h`. No
`Transfer` help text in `icmd.c`, no interactive c3270/wc3270 prompt, no X resource, no Motif dialog field: none of
them has any bearing on LizTerm, and it is not known whether upstream wants the feature at all. The aim is the
smallest diff that re-applies easily to future x3270 releases.

**4.5 An engine without the patch is caught before anything is typed.** The engine cannot be asked: `Transfer`
refuses with "Not connected in 3270 mode" before it looks at any keyword, and once connected it silently ignores a
keyword it does not know (§2). An unpatched engine would therefore type a bare `IND$FILE` into the ISPF command line,
ISPF would ignore it, and the transfer would fail 30 seconds later with "Transfer did not start within 30s". Instead,
before an ISPF transfer the backend looks for the patch's marker in the engine binary, the same byte check the CI
gate makes (§5.6), once per session, and without it fails the transfer at once with a sentence (§6.2). Rejected: no
detection, leaving the 30-second timeout for the docs to explain. The first version of this section assumed the
engine refused the keyword as an unknown option; it was corrected on 2026-09-14, while planning, once a stock b3270
was seen to accept it.

**4.6 Other engine installs become a developer feature.** Users run the engine LizTerm builds and ships.
`LIZTERM_B3270_PATH` stays, documented for development only, with the caveat that such an engine lacks LizTerm's
patches (§7).

## 5. The engine patch

### 5.1 The file

`native/patches/b3270-transfer-commandprefix.patch`, a plain unified diff against the unpacked `suite3270-4.5/`
tree, applied with `-p1` (generate it with `diff -ru` from a pristine tree to a modified one). It opens with a short
prose preamble, which `patch` ignores: what the patch does, why (§2), issue #109, and upstream status.

A `.gitattributes` line, `native/patches/*.patch text eol=lf`, keeps a Windows checkout from giving it CRLF endings
that no longer match the tarball's LF sources.

### 5.2 What it changes

`include/ft_private.h`, in `ft_conf_t` beside `other_options`:

- `char *command_prefix;`

`Common/ft.c`:

- `PARM_COMMAND_PREFIX` in `enum ft_parm_name`, straight after `PARM_OTHER_OPTIONS` (which already follows the
  Windows-only `PARM_WINDOWS_CODEPAGE` block), and `{ "CommandPrefix" }` straight after `{ "OtherOptions" }` in the
  `tp[]` table, so the enum and the table stay in step.
- `ft_init_conf` clears it beside `other_options` (`Replace(p->command_prefix, NULL);`), so a prefix never leaks
  into a later transfer.
- `parse_ft_keywords` copies `tp[PARM_COMMAND_PREFIX].value` into `p->command_prefix` beside the `OtherOptions`
  copy.
- The command builder, straight after `vb_init(&r);` and before `vb_appendf(&r, "IND\\e005BFILE ...`, appends the
  prefix followed by one space when `p->command_prefix` is set and non-empty.

### 5.3 Behaviour

With `CommandPrefix` unset or empty, the engine behaves exactly as 4.5ga6 does. With it set, the typed command is
`<prefix> IND$FILE ...`. The keyword is not tied to a host type. The room check (`flen < vb_len(&r) - 1`) includes the
prefix with no further change.

### 5.4 Applying it

`native/build/fetch-source.sh` is the one script all four builds (macOS, Linux, Windows, playback) go through, and
`shared-fetch-tarball.sh` unpacks a fresh tree on every call. The script stops `exec`ing the unpacker and instead:

1. captures the unpacked directory from `shared-fetch-tarball.sh`;
2. applies every `native/patches/*.patch`, in `LC_ALL=C` name order, with `patch -p1 -N -F0 --batch -d "$SRC"`,
   sending `patch`'s output to stderr;
3. prints the directory on stdout, which is all its callers read.

Any patch that does not apply exactly fails the build, so a future x3270 bump that moves the patched code fails
loudly instead of shipping an unpatched engine. The Docker builds mount the whole repository, so `native/patches/`
is visible inside the containers.

`patch` is added to `PACKAGES` in `build-linux-docker.sh:20` and `build-windows-docker.sh:23`; neither image has it
today. Both files already match their engine cache keys' `*linux*.sh` and `*windows*.sh` patterns, and the image tag
is a hash of the package list, so the images and engines rebuild on their own. macOS uses `/usr/bin/patch` (Apple's
2.0-12u11, which accepts these flags).

### 5.5 Cache keys

`native/patches/**` is added to the four engine output cache keys in `.github/workflows/engines.yml`: lines 57 and 64
(`osx-arm64`, `osx-x64`), 252 (Linux) and 403 (`win-x64`). Without it an edit to the patch alone would restore a
cached, unpatched engine, and every gate would still pass it, because the gates run against a cached binary too. The
source-tarball keys (46, 242, 393) cache downloads only and are left alone. `platforms.yml` already runs the engine
jobs for any change under `native/`.

### 5.6 The gate

A new `native/build/shared-verify-patches.sh <binary>` fails unless the binary contains each patch's marker string.
Today there is one marker, `CommandPrefix`, the keyword name the patched `tp[]` table puts into the binary. The
script reads the file directly with `LC_ALL=C grep -a` (no pipe, so no SIGPIPE under `pipefail`), which works on the
arm64 and Windows engines the runners cannot execute. A stock 4.5ga6 b3270 contains `OtherOptions` twice and
`CommandPrefix` zero times, so the marker cannot pass by accident. The backend reads the same marker at run time
(§6.2).

- `verify-macos.sh`, `verify-linux.sh` and `verify-windows.sh` call it as their **last arm**, after the TLS or
  Schannel check and before the final `OK` line, so each existing reject fixture still fails at the arm it was built
  for.
- `verify-bundled-engine.sh` also calls it after the machine-type check, so every packaged archive on all six RIDs,
  win-arm64 included, carries the check in `release.yml`.
- Being `shared-*`, the script is in every engine cache key by construction.
- **Reject cases.** Each platform's gate step in `engines.yml` gains one fixture: a copy of the freshly built engine
  with `CommandPrefix` replaced by a same-length string (`CommandPrefiX`). It clears every earlier arm and must be
  rejected by the patch arm's own message. On macOS the copy is re-signed ad hoc (`codesign -f -s -`), because an
  arm64 binary with an invalid signature is killed when the TLS arm runs it and would be rejected by the wrong arm.
  The byte replacement uses `perl` where the step has it, or `python3` inside the Windows container.

### 5.7 Bumping x3270, and dropping the patch

On a version bump, a patch that no longer applies fails `fetch-source.sh`. Regenerate it against the new tarball,
keep its preamble, and rebuild. When an upstream release carries equivalent behaviour, delete the patch file and its
marker; if upstream spells the keyword differently, `TransferMapper` changes with it.

## 6. LizTerm

### 6.1 Core (`src/LizTerm.Core/Session/FileTransfer.cs`)

- `TransferHostType` gains `Ispf`, **appended** (`{ Tso, Vm, Cics, Ispf }`) so the existing members keep their
  values. Its doc comment says it is TSO reached from an ISPF command line.
- A static `TransferHostTypes` class holds one extension, `IsTso(this TransferHostType)`, true for `Tso` and `Ispf`.
  It is the one answer to "does IND$FILE run under TSO here?", so no caller spells `== Tso || == Ispf`.
- `FileTransferRequest.Validate` computes `tsoSend` with it, so an ISPF send gets TSO's allocation rules.
- The request gains no field. Core records "ISPF"; how the engine gets there is the backend's business.
- `FileTransferResult`'s doc comment, which says the message is "unaltered", names the one exception: a backend may
  fail a request the engine cannot perform, in its own words, without sending it (§6.2).

### 6.2 Backend (`src/LizTerm.Backend.B3270`)

- **`TransferMapper`** uses `IsTso()` wherever it tests for TSO today. `HostKeyword` maps both `Tso` and `Ispf` to
  `tso`. For `Ispf` it adds `commandprefix=TSO` straight after `host=tso`, and every TSO-only keyword (BLKSIZE,
  allocation and space, the Undefined record format) is sent exactly as for TSO. TSO, VM and CICS never send
  `commandprefix`.
- **`EnginePatches`** (new, internal, `Process/EnginePatches.cs`): the marker `CommandPrefix` (the string
  `shared-verify-patches.sh` checks for), the §6.4 sentence, and `Carries(path, marker)`, which reads the file's
  bytes and reports whether the marker's ASCII bytes occur in them.
- **`B3270Session.TransferAsync`**: for an `Ispf` request, before claiming the transfer slot or sending anything, it
  asks whether the engine at `Engine.Path` carries the marker, reading the file once per session (a `Lazy<bool>`). If
  not, it returns a failed `FileTransferResult` whose message is the §6.4 sentence, and nothing reaches the engine. A
  session with no engine path (the tests' fake process) or a file that cannot be read is assumed to carry the patch,
  so the transfer is attempted as it would have been before the check existed. TSO, VM and CICS transfers never read
  the file.
- **`B3270Locator.Find`**: the "not found" message keeps its list of places looked in, and its last line changes from
  `Set LIZTERM_B3270_PATH to a b3270 executable to override.` to `Reinstalling LizTerm restores it.` The message
  reaches users through `StartupErrorWindow`. `EnvironmentOverride` itself is unchanged, and About and the status bar
  keep saying `from LIZTERM_B3270_PATH` when a developer has set it.

### 6.3 App (`src/LizTerm.App`)

- `FileTransferViewModel.HostTypes` becomes `[Tso, Ispf, Vm, Cics]`, putting ISPF (MVS) beside what it really is.
- `IsTso` becomes `HostType.IsTso()`, so `CanSetBlksize`, `CanSetSpace`, `CanSetAverageBlock` and the Undefined
  record format behave for ISPF (MVS) exactly as for TSO. `RecordFormats`, `CanSetRecordFormat` and
  `OnHostTypeChanged` test for VM and CICS only and need no change.
- A new `CursorHint` property, notified from `_hostType` like `IsTso`, replaces the fixed hint `TextBlock` text at
  `FileTransferWindow.axaml:68`.
- `TransferLabels` labels `Ispf` as `ISPF (MVS)`.
- `LocalFileNames.Suggest` treats `Ispf` as `Tso` (member name, else last qualifier).
- The Host type `ComboBox` is 120 px wide. If `ISPF (MVS)` is clipped, it is widened to the smallest width that
  shows the label whole (checked in the in-app pass, §8.5).
- `SessionViewModel.LastTransferRequest` carries ISPF (MVS) like any other host type; nothing goes to profiles.

### 6.4 User-visible text

| Where | Text |
|---|---|
| Host type drop-down | `ISPF (MVS)` |
| Hint, ISPF (MVS) | The cursor must be on an ISPF Command ===> or Option ===> line before you start. |
| Hint, TSO, VM and CICS | The cursor must be at a TSO READY prompt or a command line before you start. (unchanged) |
| Unpatched engine, ISPF (MVS) | This engine can't transfer from ISPF: it wasn't built with LizTerm's patch. The engine that ships with LizTerm can. |
| Engine not found, last line | Reinstalling LizTerm restores it. |

## 7. Documentation

Each change goes in the file that owns the fact.

**For users**

- `docs/user-guide.md`, "File transfer (IND$FILE)": the "before you start" sentence becomes per host type (TSO at
  `READY` or a command line; ISPF (MVS) on an ISPF `Command ===>` or `Option ===>` line). A short paragraph says ISPF
  (MVS) types `TSO` ahead of the command and that the TSO options apply as for TSO, and one sentence says ISPF's
  option 6 (Command) also accepts a transfer with host type TSO. Then regenerate
  `src/LizTerm.App/Assets/Docs/user-guide.html` with
  `LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`.
- `README.md`, "Where LizTerm is right now": keeps "not new code", and adds that LizTerm builds b3270 from the
  upstream source with one small addition of its own, for transfers from inside ISPF. The README never mentions the
  override, so there is nothing to remove.
- `THIRD-PARTY-NOTICES.txt`: one line under x3270 saying LizTerm's build of b3270 carries a small modification,
  published in the LizTerm repository under `native/patches/`. BSD-3-Clause does not require it; About shows these
  notices, so it is the honest place to say so.

**For developers**

- `docs/development.md`, "Getting an engine": building an engine and downloading a CI-built one stay first, and both
  give the patched engine. The `LIZTERM_B3270_PATH` bullet becomes a development shortcut: it can point at another
  b3270 4.2 or later (Homebrew's, for instance), it lacks LizTerm's patches so ISPF (MVS) transfers fail with it, and
  it is never what users run. The environment-variable table row says the same in a few words.
- `docs/engines.md`: the intro reads "pinned at 4.5ga6, with LizTerm's patches applied", and the override sentence
  near line 27 gets the same caveat. A new **Patches** section after "Pinned sources" records: the policy (only what
  LizTerm's engine uses, maintained whether or not upstream adopts it, no changes to x3270's other front ends); what
  `CommandPrefix` does and why; how patches are applied (§5.4) and the `.gitattributes` line; the cache keys and
  `shared-verify-patches.sh`, including how a future patch adds its marker; bumping and dropping (§5.7).
- `docs/ci-and-release.md`, "Caches": adds `native/patches/**` to the engine keys. The paragraph describes only the
  macOS and Linux keys today, so the Windows key is added to it too.

**Notes for Claude**

- Root `CLAUDE.md`: the fresh-worktree note ("a Homebrew b3270 works") adds "except for ISPF (MVS) transfers; build
  an engine for those".
- `src/LizTerm.Core/CLAUDE.md`: `TransferHostType.Ispf` and `IsTso()`, and the one exception to "Message is the
  engine's or host's final text verbatim".
- `src/LizTerm.Backend.B3270/CLAUDE.md`: the ISPF mapping, and the engine marker check with the reason it exists
  (unknown keywords are silently ignored), under "File transfer"; the reworded not-found line under the locator.
- `src/LizTerm.App/CLAUDE.md`, "File transfer": the ISPF (MVS) entry and `CursorHint`.
- `tests/CLAUDE.md`: the `TsoNavigator` rule (§8.4) and the ISPF live test.
- `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`: the new fixture (§8.3).
- Unchanged: `docs/architecture.md` never claims the engine is unmodified, and `native/CLAUDE.md` imports
  `docs/engines.md`, so it picks up the Patches section. Earlier specs that say "no patches" stay as written; they are
  a record.

## 8. Testing

### 8.1 Unit tests, written before the code

- **Core, `FileTransferRequestTests`**: `IsTso()` is true for TSO and ISPF and false for VM and CICS; an ISPF send
  requires primary space once allocation units are set, and an average block for AVBLOCK, as a TSO send does.
- **Backend, `TransferMapperTests`**: ISPF maps to `host=tso` followed by `commandprefix=TSO`, and keeps BLKSIZE,
  allocation and the Undefined record format; TSO, VM and CICS never send `commandprefix`;
  `Every_host_type_has_a_keyword` gains an ISPF → `host=tso` row.
- **Backend, `EnginePatchesTests`**: `Carries` finds the marker in a temp file that holds it among other bytes, and
  does not find it in one without it.
- **Backend, `B3270SessionTransferTests`** (fake process, with a temp file as the engine's location): on an engine
  file without the marker, an ISPF transfer fails at once with the §6.4 sentence and no `Transfer` line is written,
  while a TSO transfer on the same session is sent as usual; on a file with the marker, and on a session with no
  engine path, an ISPF request's `Transfer` line carries `host=tso` followed by `commandprefix=TSO`.
- **Backend, `B3270LocatorTests`**: the not-found message ends `Reinstalling LizTerm restores it.` and no longer
  names `LIZTERM_B3270_PATH`.
- **App, `FileTransferViewModelTests`**: the `HostTypes` order; ISPF (MVS) enables the TSO-only fields and offers
  Undefined; an initial ISPF request restores `HostType` as ISPF; `CursorHint` follows the host type and is notified
  when it changes.
- **App, `FileTransferWindowTests`**: the label theory gains `ISPF (MVS)`. **`LocalFileNamesTests`**: ISPF rows that
  match the TSO ones.
- **`UserGuideAssetTests`** passes again once the HTML is regenerated.

### 8.2 Engine

- `shared-verify-patches.sh` as each platform gate's last arm, with the defaced-copy reject case per platform
  (§5.6).
- Locally: `native/build/build-macos.sh` on Robert's Mac applies the patch cleanly, `verify-macos.sh` passes, and the
  resulting `native/out/osx-arm64/b3270` is what the live lane runs.

### 8.3 Replay fixture

`tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-ispf-roundtrip.jsonl`: a `LIZTERM_WIRE_LOG` of a LizTerm session
with the bundled engine against MVS/CE, sending and receiving from the ISPF primary menu (the live test in §8.4, run on
its own with the variable set, as `LiveHostTests` already notes for recording), converted with `tools/wirelog-to-fixture.sh`, with the logon, logoff and host address cut, and
documented in the fixtures README beside `indfile-tso-roundtrip.jsonl`.

- An `IndicationParserTests` case mirrors the TSO fixture's: `ft` states begin `awaiting`, include `running` with
  bytes, and end in two successful completes carrying "Transfer complete".
- A `ReplayTests` case asserts the last screen in the fixture is an ISPF panel (`===>`) and not a `***` pause.

A fixture replays b3270's output into `FakeB3270Process`; it proves LizTerm's handling of a real ISPF transfer, not
that the engine typed `TSO`. That proof is §8.2's gate and §8.4's live test.

### 8.4 Live test

`LiveHostTests.Indfile_round_trip_from_ispf_matches`, beside `Indfile_round_trip_matches` and skipping on the same
missing variables:

1. log on to `READY`; type `ISPF` and wait for a `===>` panel. If `READY` comes back instead, skip: the host has no
   ISPF.
2. send and receive `LIZTERM.ISPFTEST` (the TSO test uses `LIZTERM.ITEST`) with `HostType = Ispf`, and compare the
   files.
3. leave ISPF through `TsoNavigator.ReachReadyAsync`, then delete the dataset and log off through the existing
   cleanup steps.

The lane resolves the bundled engine (`BundledEngine.Require`), so this is the test that proves the real patch
against a real ISPF.

`TsoNavigator` learns one rule for Wally ISPF's leftover panel (§3). Today a screen still showing `===>` gets PF3,
and a `READY` written over row 1 is not the last non-blank line, so `ReachReadyAsync` would press PF3 until it gives
up. The rule: **a screen with a line that trims to exactly `READY` while `===>` is still on it** is TSO after a
full-screen program has ended. The navigator presses Clear, which is safe once ISPF has ended, then Enter if the
screen stays blank, and then expects `READY`. It never presses Clear on a screen without such a line, because
Clear on a live Wally ISPF panel locks the keyboard for good. The rule is verified against MVS/CE while it is
written.

### 8.5 Before calling it done

- `dotnet test LizTerm.slnx` and the zero-warning `dotnet build LizTerm.slnx --no-incremental` check.
- The local engine build passing its gate (§8.2), and the live lane (both transfer tests) against MVS/CE.
- An in-app look at the dialog with the DevTools MCP, or handed to Robert: ISPF (MVS) is in the drop-down and not
  clipped, the hint changes with the host type, and a transfer from the ISPF primary menu succeeds.

### 8.6 Not proposed

A CI test driving the engine against a fake TN3270 host to watch it type `TSO IND$FILE`. The tests have no fake
TN3270 host today (only a TLS loopback), and the marker gate plus the live test cover the same failure for far less.

## 9. Out of scope

- The parts of an upstream-complete change that LizTerm does not use: `Transfer` help text and interactive prompts
  in `icmd.c`, an `ftCommandPrefix` resource, and a field in x3270's Motif dialog.
- Detecting ISPF from the screen and choosing the host type automatically.
- A prefix for other hosts in LizTerm's UI, such as `CMS` under VM's XEDIT. The engine keyword would allow it; nobody
  has asked, and it is untested.
- Making ISPF (MVS) work with an engine set through `LIZTERM_B3270_PATH`.
- Per-profile transfer defaults and a transfer history (#21).
- z/OSMF file transfer (#17).
- Fixing upstream's silent acceptance of unknown `Transfer` keywords (§2) in our patch. It would not help here: the
  engine that would need the fix is the one without our patch. It can be reported upstream alongside the submission.

## 10. Where it lands

- `native/patches/b3270-transfer-commandprefix.patch` (new); `.gitattributes` (new line).
- `native/build/fetch-source.sh`, `build-linux-docker.sh`, `build-windows-docker.sh`, `verify-macos.sh`,
  `verify-linux.sh`, `verify-windows.sh`, `verify-bundled-engine.sh`; `shared-verify-patches.sh` (new, with the
  licence header `RepositoryHeadersTests` requires of `.sh` files under `native/build`).
- `.github/workflows/engines.yml` (cache keys, reject cases).
- `src/LizTerm.Core/Session/FileTransfer.cs`.
- `src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs`, `B3270Session.cs`, `Process/B3270Locator.cs`,
  `Process/EnginePatches.cs` (new).
- `src/LizTerm.App/ViewModels/FileTransferViewModel.cs`, `Views/FileTransferWindow.axaml`, `Views/TransferLabels.cs`,
  `Files/LocalFileNames.cs`, `Assets/Docs/user-guide.html` (regenerated).
- Tests: `FileTransferRequestTests`, `TransferMapperTests`, `B3270SessionTransferTests`, `B3270LocatorTests`,
  `EnginePatchesTests`, `IndicationParserTests`, `ReplayTests`, `FileTransferViewModelTests`, `FileTransferWindowTests`,
  `LocalFileNamesTests`, `LiveHostTests`, `TsoNavigator`; `Fixtures/indfile-ispf-roundtrip.jsonl` (new) and the
  fixtures README.
- Docs: `README.md`, `THIRD-PARTY-NOTICES.txt`, `docs/user-guide.md`, `docs/development.md`, `docs/engines.md`,
  `docs/ci-and-release.md`, root `CLAUDE.md`, `src/LizTerm.Core/CLAUDE.md`, `src/LizTerm.Backend.B3270/CLAUDE.md`,
  `src/LizTerm.App/CLAUDE.md`, `tests/CLAUDE.md`.
