# LizTerm Milestone 2, plan 3b: hardening (certificate pinning, keymap, review follow-ups)

Date: 2026-09-05. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessor: `2026-09-05-lizterm-m2-polish-design.md`
(plan 3a), whose section 10 is the input to this plan. Status: approved in discussion; awaiting review of this text.

## 1. Purpose

Plan 3a shipped the last of the v1 features. This plan closes Milestone 2 with three kinds of work:

1. Two items the v1 spec promised but never got: a real cross-check of the default keymap against wc3270 and
   Vista TN3270, and a form of "always allow" for a self-signed certificate that means *this* certificate rather
   than "stop checking".
2. The user-visible fixes parked by the plan 1, 2, and 3a reviews.
3. The backend, test, and live-lane cleanups from the same reviews.

Decisions taken in the brainstorm on 2026-09-05:

- Pinning is real pinning: the engine verifies against the pinned certificate on the very connection it uses.
  A changed certificate reopens the prompt, and accepting re-pins.
- The keymap follows Vista TN3270 wholesale, including the rows that change existing behavior and the
  Backspace default. These are *defaults*: the keymap becomes a table so that user remapping can be added later
  without restructuring, but no remapping UI, profile field, or file is built now.
- Clear gets a second home on Ctrl+Escape because Mac keyboards have no Pause key; PA1 to PA3 get a second home
  on Alt+1 to Alt+3 because Mac laptops have no Insert key.

## 2. Facts established by probing on 2026-09-05

Against b3270 4.5ga6 (Homebrew build) and the TLS gateway with its self-signed certificate:

- `Set` accepts `caFile`, `caDir`, `acceptHostname`, and `verifyHostCert` at runtime; `Set` with no arguments
  lists them.
- `Set(caFile,<pem>,acceptHostname,any,verifyHostCert,true)` followed by `Connect(L:host:port)` succeeds
  against the gateway when the PEM is the gateway's own certificate, and the `tls` indication reports
  `verified:true`. With a decoy self-signed PEM the Connect run fails with the lines `Connection failed:`,
  `TLS: Host certificate verification failed:`, `self-signed certificate (18)`.
- `Set(caFile,"",acceptHostname,"")` clears both for the next connect in the same engine; a verify-on connect
  then fails as self-signed again, and a verify-off connect succeeds. Settings are therefore per attempt as long
  as every attempt sets all three explicitly.
- `acceptHostname` accepts `any` or `*` (skip the name check), `DNS:name`, or a bare name; `IP:` is rejected
  (`Common/sio_openssl.c`). With a pinned certificate the name check adds nothing, so `any` is used.
- x3270 builds its TLS context and loads `caFile` in `sio_init`, once per connection, so the file must exist
  while the Connect run is pending and may go once it has answered.
- b3270's `tls` indication and `Query(TlsCertInfo)` carry only the public key size, subject, issuer, and
  alternate names. The certificate itself is not available from the engine, so LizTerm must read it.
- Keymaps: x3270's `keymap.base.3270` maps `BackSpace` to `Erase()` (`x3270/fb-x3270`), and wc3270's `base`
  maps `BACK` to `Erase()` (`Common/fb-c3270`). Vista's manual (v1.27, "Keyboard Defaults") lists no Backspace
  override, which on a PC means erase. The v1 spec's belief that x3270 defaults to a cursor-left Backspace was
  wrong; it came from the `BackSpace()` action's name.

## 3. Core changes (`LizTerm.Core`)

### 3.1 Certificate pin

```csharp
namespace LizTerm.Core.Session;
/// <summary>A host certificate the user chose to trust for a profile. Pem holds every certificate the host
/// presented, leaf first, as concatenated PEM blocks; Sha256 is the leaf's fingerprint as colon-separated
/// upper-case hex pairs; Subject is the leaf's subject for display.</summary>
public sealed record CertificatePin(string Sha256, string Subject, string Pem);
```

`SessionProfile` gains `CertificatePin? PinnedCertificate` (JSON `pinnedCertificate`, written as `null` when absent
because the source-generated context writes every property; a file without the field reads the same). It is
independent of `VerifyCertificate`: a pinned profile has verification on. Profiles saved by plan 3a with
`VerifyCertificate=false` are unchanged and never prompt.

`ConnectOptions` becomes `record ConnectOptions(bool? VerifyCertificate = null, CertificatePin? Pin = null)`. The
effective TLS settings for one attempt are:

1. `verify = options.VerifyCertificate ?? Profile.VerifyCertificate`.
2. `pin = options.Pin ?? Profile.PinnedCertificate`.
3. `verify` false: verification off, no pin (the editor's verify checkbox therefore disables a pin without
   removing it).
4. `verify` true and `pin` set: verification on against the pin only.
5. `verify` true and no pin: verification on against the engine's default trust store, as today.

### 3.2 Backspace default

`SessionProfile.DestructiveBackspace` defaults to `true`. The profile editor's checkbox reads "Backspace erases
the previous character (off: Backspace only moves the cursor left)". A saved profile keeps whatever value its
file holds, because the editor writes the field explicitly; only a profile without the field, and every new
profile, gets the new default.

## 4. Backend changes (`LizTerm.Backend.B3270`)

### 4.1 Pin file and the Set run

`ConnectAsync` replaces its `Set verifyHostCert` run with one `Set` carrying all three values, computed by the
rules in 3.1:

- verification off: `verifyHostCert false`, `caFile ""`, `acceptHostname ""`;
- pinned: `verifyHostCert true`, `caFile <path>`, `acceptHostname any`;
- default trust: `verifyHostCert true`, `caFile ""`, `acceptHostname ""`.

For the pinned case the PEM is written just before that run to `Path.GetTempPath()/lizterm-pin-<guid>.pem`,
created owner-read-write on Unix (`UnixCreateMode` in `FileStreamOptions`; skipped on Windows, where the temp
directory is per user). The path is deleted in the same `finally` that awaits the cancel's Disconnect, so it is
gone once the Connect run has answered, was cancelled, or the engine died; a delete failure is ignored. The path
appears in the wire log; the PEM holds nothing secret. The backend never touches the config directory and Core
still never names b3270.

### 4.2 Fixture

The live pinned connect (section 8) is run once with a wire log, and `tools/wirelog-to-fixture.sh` trims it to
`gateway-pinned-login.jsonl`: the same shape as `gateway-login-tls.jsonl` with `verified:true`. `ReplayTests`
asserts the `TlsInfo.Verified` value it produces, and the Fixtures README describes it.

## 5. Certificate pinning in the app (`LizTerm.App`)

### 5.1 Reading the certificate

`src/LizTerm.App/Certificates/`:

```csharp
public sealed record PresentedCertificate(string Sha256, string Subject, string Pem, bool Pinnable, string? NotPinnableReason);

public interface ICertificateFetcher
{
    /// <summary>Performs one TLS handshake to read what the host presents, then closes. Throws on any failure
    /// (refused, timed out, not TLS); the caller treats every exception the same way.</summary>
    Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token);
}
```

`SslStreamCertificateFetcher`: `TcpClient` connect and `SslStream.AuthenticateAsClientAsync` under a 10 s
timeout linked to the caller's token; the validation callback captures the certificate and the chain elements
and returns true; the stream is closed at once. `Pem` is every chain element leaf first. `Pinnable` is the result
of building an `X509Chain` with `TrustMode = CustomRootTrust`, `CustomTrustStore` = those same certificates,
`RevocationMode = NoCheck`, and no verification flags relaxed: true for a self-signed leaf and for a private CA
whose root the host sends; false, with the chain status text as the reason, for a chain missing its root or an
expired certificate, which are the cases OpenSSL would also reject and pinning could not fix. Refusing to pin
there prevents an accept-fail-prompt loop.

The fetcher is injected into `SessionViewModel` after the certificate prompt; `App.OpenSession` passes the real
one and tests pass `FakeCertificateFetcher` (`Result`, `Exception`, `Calls` recording `fetch:<host>:<port>`).

### 5.2 Prompt contract

```csharp
public sealed record CertificatePromptRequest(
    string Host,
    IReadOnlyList<string> Reason,          // engine lines minus "Connection failed:"
    PresentedCertificate? Presented,       // null when the fetch failed or was not attempted
    string? FetchError,                    // the fetch exception's message when Presented is null
    CertificatePin? Previous,              // the pin in force when the failure happened
    bool CanPin,                           // whether "Trust this certificate for this profile" is offered
    string? CannotPinReason);              // shown when a saved TLS profile cannot pin

public interface ICertificatePrompt
{
    Task<CertificateDecision> AskAsync(CertificatePromptRequest request);
}
public sealed record CertificateDecision(bool ConnectAnyway, bool Remember);   // Remember now means pin
```

`CanPin` is true only when all hold: the profile is saved (`saveProfile` not null), `Profile.UseTls` is on,
`Presented` is non-null and `Pinnable`, and `Presented.Sha256` differs from `Previous?.Sha256`. The last
condition closes the remaining loop: if the engine rejected a pin that .NET accepted, the prompt says so
(`CannotPinReason` = "The engine rejected the pinned certificate; connecting anyway applies to this attempt
only.") and offers the one-time allow.

### 5.3 Flow in `SessionViewModel`

`OfferConnectAnywayAsync` becomes:

1. On `CertificateVerificationFailed` with no verify-off override active: if `Profile.UseTls`, call the fetcher
   under the connect's token and catch every exception into `FetchError`. A plain profile that the host upgraded
   through STARTTLS skips the fetch (the fetcher only speaks TLS-on-connect).
2. Build the request with `Previous = _pinOverride ?? Profile.PinnedCertificate` and ask.
3. Declined: `ErrorMessage` as today.
4. `ConnectAnyway` without `Remember`: reconnect with `ConnectOptions(VerifyCertificate: false)`, one attempt,
   exactly as plan 3a.
5. `ConnectAnyway` with `Remember`: `pin = new CertificatePin(Presented.Sha256, Presented.Subject, Presented.Pem)`;
   `saveProfile(Profile with { PinnedCertificate = pin, VerifyCertificate = true })`; set `_pinOverride = pin` so
   every later connect from this window passes `ConnectOptions(Pin: pin)` (the session's profile is fixed at
   construction); reconnect with that option under a fresh timeout source. Save and prompt failures reach the
   error banner as in plan 3a, never a faulted command.

`_verifyOverride` from plan 3a is kept for the one-time path; the two overrides are never both set.

### 5.4 Prompt window

`CertificateWindow` (modal, owner = session window) renders the request:

- Title "Certificate not verified", or "Certificate changed" when `Previous` is set.
- First line: "{host} presented a certificate that could not be verified:" or, for a change, "{host} presented a
  certificate that is not the one trusted for this profile."
- When `Presented` is set: "Presented: SHA-256 {fingerprint}" and "Subject: {subject}", and for a change
  "Trusted: SHA-256 {previous fingerprint}" above them. When it is null: "The certificate could not be read:
  {FetchError}".
- The engine's reason lines in the 3270 font, as today.
- Checkbox "Trust this certificate for this profile" when `CanPin`; otherwise, for a saved TLS profile, the
  `CannotPinReason` line, which for a not-pinnable certificate reads "This certificate cannot be pinned:
  {NotPinnableReason}. Connect Anyway applies to this attempt only."
- Buttons "Connect Anyway" and "Cancel", Cancel default and on Escape, unchanged.

### 5.5 Profile editor

Under the verify checkbox, visible only when the profile being edited has a pin: "Pinned certificate: SHA-256
{fingerprint}" and a "Forget" button. Forget clears the pin in the editor's state; Save writes the profile
without it. This is the only way back from a pin to default trust. No other editor change.

## 6. Keymap (`LizTerm.App/Keyboard`)

### 6.1 Shape

```csharp
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers, bool Tap = false);   // Tap: a modifier pressed and released alone

public sealed class Keymap
{
    public IReadOnlyDictionary<KeyChord, TerminalKey> Keys { get; }
    public IReadOnlyDictionary<KeyChord, string> Text { get; }    // chords that type a character
    public bool TryMap(KeyChord chord, out TerminalKey key);
    public bool TryText(KeyChord chord, out string text);
    public Keymap With(IEnumerable<KeyValuePair<KeyChord, TerminalKey>> keys, IEnumerable<KeyValuePair<KeyChord, string>> text);  // later overrides win
}

public static class DefaultKeymap
{
    public static Keymap Create(bool destructiveBackspace);   // two cached instances
}
```

`With` is the seam for user remapping: a future profile field would be parsed into entries and applied over the
default. Nothing else about remapping is built now.

### 6.2 Default table

Cross-checked against Vista TN3270 1.27 ("Keyboard Defaults") and wc3270's compiled-in `base` keymap. The
LizTerm column is what this plan ships; Vista wins every conflict, per the v1 spec's rule that Vista is the
behavioral reference.

| Chord | LizTerm | Vista | wc3270 |
|---|---|---|---|
| Enter, Ctrl+Enter, Right Ctrl tap | Enter | Enter | Enter (Ctrl+M; Right Ctrl in the `rctrl` keymap) |
| Shift+Enter | Newline | NewLine | Ctrl+J |
| Escape | Attn | Attention | (prompt) |
| Shift+Escape | SysReq | SysRequest | Key(0x1d) |
| Left Ctrl tap, Ctrl+R | Reset | Left Ctrl | Ctrl+R, Alt+R |
| Pause, Ctrl+Escape | Clear | Pause | Alt+C |
| F1..F12 | PF1..PF12 | same | same |
| Shift+F1..F12, Ctrl+F1..F12 | PF13..PF24 | same | Shift only |
| Page Up, Page Down | PF7, PF8 | same | scrollback |
| Ctrl+Insert, Ctrl+Home, Ctrl+PageUp; Alt+1, Alt+2, Alt+3 | PA1, PA2, PA3 | Ctrl+Insert/Home/PageUp | Alt+1/2/3 |
| Tab, Shift+Tab | Tab, BackTab | same | same |
| Insert | Insert toggle | same | same |
| Home | Home | same | same |
| End | EraseEOF | Erase End of Field | FieldEnd (Shift+End EraseEOF) |
| Delete | Delete | same | same |
| Backspace | Erase (profile off: Backspace, cursor left) | erase | Erase |
| Up, Down, Left, Right | cursor | same | same |
| Ctrl+[ | types `¬` (U+00AC) | Not key | Alt+^ |
| Ctrl+6 | types `¢` (U+00A2) | Cent Sign | none |

Not adopted from Vista: its Ctrl+letter editing functions (word delete, find, paste variants, undo), its mouse
bindings, and Alt+function-key window switching. Not adopted from wc3270: Ctrl+A Attn, Ctrl+D Dup, Ctrl+F
FieldMark, Ctrl+U DeleteField, Ctrl+H Erase, and the Alt+letter set; Dup and FieldMark remain unreachable from the
keyboard until remapping exists (they were before this plan too).

Ordering in `TerminalScreen.OnKeyDown`: the platform copy, paste, and select-all hotkeys are checked first
(they are not in the table; Cmd on macOS, Ctrl elsewhere), then `TryMap`, then `TryText`, then fall-through to
Avalonia text input as today. Ctrl+R is included beside the Left Ctrl tap as insurance for a platform that
reports no tap.

### 6.3 Modifier taps

`ModifierTapDetector` (pure, in `Keyboard/`): `KeyDown(Key)` records a Left or Right Ctrl press and clears the
record on any other key; `KeyUp(Key)` returns the tapped key when the same Ctrl key goes up with nothing pressed
in between, else null; `Reset()` on focus loss. `TerminalScreen` feeds it from `OnKeyDown`, `OnKeyUp`, and
`OnLostFocus`, and a returned tap is looked up as `new KeyChord(key, KeyModifiers.None, Tap: true)`. Ctrl+C is
therefore never a Reset: the C press clears the record. Risk for the live pass: whether Avalonia on macOS reports
`Key.LeftCtrl` and `Key.RightCtrl` distinctly; if it does not, the taps are simply unreachable there and Ctrl+R
covers Reset.

### 6.4 Record

The v1 spec's section 6.5 gets one line pointing at this table as the cross-checked result. The Keys menu is
unchanged.

## 7. Session and transfer dialog fixes

- **Escape closes the transfer dialog.** The Close button becomes the window's cancel button (`IsCancel`), which
  routes through `Closing` and therefore `FileTransferViewModel.TryClose`: Escape on a running transfer cancels
  it first, exactly as clicking Close does. A comment on the progress path states that send progress can exceed
  `TotalBytes` when CRLF is added.
- **Focus after Dismiss.** After the error bar's Dismiss runs, `SessionWindow` focuses the screen control, so the
  next keystroke reaches the host.
- **Hotkeys respect CanExecute.** `CommandRouting.TryExecute` in `src/LizTerm.App/` runs a relay command only when
  `CanExecute` is true, and the window's copy, paste, select-all, and key-requested handlers go through it,
  because the toolkit's `ExecuteAsync` ignores the guard.
- **Selection property hygiene.** `TerminalScreen` writes its own `Selection` with `SetCurrentValue` so a two-way
  binding survives, and overrides `OnPointerCaptureLost` to reset `SelectionGesture` and release the drag.

## 8. Backend, test, and live-lane cleanup

- **Disconnect waiters.** `WaitForDisconnectedAsync` installs its completion source with `CompareExchange`: the
  first caller creates it, a concurrent caller awaits the same one, and the `finally` clears the slot only if it
  still holds that instance. A test drives `DisconnectAsync` and a cancelled `ConnectAsync` concurrently.
- **Wire-log names.** `wire-<profile>-<timestamp>.log` gains `-2`, `-3`, ... when the name exists.
- **About** uses `SizeToContent="Height"` with the notices box at a fixed height, so the engine path line can
  wrap without clipping.
- **Shared waits.** One `WaitUntilAsync` helper per test project (`Wait.cs` in the backend and App test projects)
  replaces the five copies.
- **Environment isolation.** `SessionFactoryTests` and any other test that sets `LIZTERM_B3270_PATH` join an
  xunit collection with `DisableParallelization = true` and restore the variable in a `finally`.
- **Coverage batch.** Tests for: `TransferMapper` Binary with allocation on a TSO send and exhaustive switches over
  every enum; `FileTransferViewModel` raising `PropertyChanged` for each derived flag; `TransferLabels` with null
  and `ConvertBack`; Cancel disabling after the first click; the `OnFileTransferClick` catch path; triple-click
  via `PressAt(cell, 3)` (which must not extend the selection); `DisposeAsync` stopping the wire log only after
  Quit; an unbracketed ad hoc IPv6 argument, which `StartupArguments` must report as a usage error (more than one
  colon outside brackets is malformed, never a hostname; today a forcing prefix such as `L:` passes it to the engine
  whole); and, for plan 3a tasks 1, 3, 4, 5, 6, 9, 10, 11, 12, and 16, the tests
  the 3a spec section describes that the code lacks, which the plan enumerates by reading each task against the
  test project.
- **Live lane.** `TsoNavigator`'s documentation matches its actual rules (`===>` and "hit ENTER"), the gate
  predicate is re-checked after the quiet wait before trusting READY, `LogoffAsync` reports a timeout in the test
  output instead of returning silently, `ScreenWaiter` waits on one completion source per update with a
  `Stopwatch` deadline instead of abandoning a `Task.Delay` per update, and every live test carries a 10 minute
  xunit timeout.

## 9. Testing

- Core: profile round trip with and without a pin; `DestructiveBackspace` default; `ConnectOptions` defaults.
- Backend (fake process): the Set arguments for the three effective cases; the pin file exists while the Connect
  run is pending and is gone after success, failure, cancel, and engine death; the disconnect-waiter race.
- App view model (`FakeCertificatePrompt`, `FakeCertificateFetcher`, `FakeEmulatorSession`): first pin saves the
  profile with the pin and verify on and reconnects with `connect:pin:<sha256>`; changed certificate prompts with
  `Previous` set and re-pins; decline; fetch failure prompts without a fingerprint and cannot pin; not pinnable
  cannot pin; same fingerprint as the pin cannot pin; TLS-off profile never fetches; ad hoc profile cannot pin;
  one-time allow still passes `connect:noverify`.
- Fetcher: an in-process `SslStream` server with a certificate generated by `CertificateRequest`, asserting
  fingerprint, subject, PEM round trip through `X509Certificate2`, `Pinnable` true for the self-signed case and
  false for an expired one, and a refused port throwing.
- Keymap: a data-driven test over every row of the 6.2 table; `With` overriding an entry; tap detector: tap fires,
  intervening key cancels, Reset clears; headless `TerminalScreen` tests that Escape sends Attn, Shift+Escape
  SysReq, Ctrl+Escape Clear, a Right Ctrl tap Enter, Ctrl+[ types `¬`, and Backspace follows the property.
- Windows and dialog: Escape closes the transfer dialog and cancels a running transfer first; Dismiss refocuses
  the screen; a hotkey with `CanExecute` false does nothing.
- Live (opt-in): pinned connect to the gateway verifies (`TlsInfo.Verified == true`); a decoy pin fails with
  `CertificateVerificationFailed`; the existing tests unchanged.
- Whole suite green with zero warnings; the four integration tests still skip without a host.

### Live UI pass

With the DevTools inspector: the certificate-changed prompt with both fingerprints, the editor's pinned line and
Forget, Escape sending Attn, a Right Ctrl tap sending Enter on macOS, Escape closing the transfer dialog, and
the splash's rendered look, which no session has yet seen.

## 10. Out of scope

The remapping editor, a keymap profile field or file, pinning for hosts that upgrade through STARTTLS on a
plain profile, any other editor change, removal of the five stale worktrees, and the `Dup` and `FieldMark`
keys. Noted for Milestone 3 packaging rather than here: a statically linked b3270 has whatever OpenSSL
directory was compiled in, so on a machine without that directory default trust may reject every certificate;
the bundle will need a CA file and a `caFile` default.
