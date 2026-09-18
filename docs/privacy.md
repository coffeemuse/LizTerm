# What LizTerm records, and the rules that follow

The home for one question: **what does LizTerm know about a session, where does it put it, and who else can
read it?** Individual decisions — whether wire logs can default to on, what may travel in an export bundle,
what an error message may quote — are settled here rather than argued again each time.

Written 2026-09-18, prompted by [#139](https://github.com/coffeemuse/LizTerm/issues/139).

## Who the user is

Most LizTerm users are hobbyists running MVS 3.8j, VM/370 or MVS/CE on a machine under their desk or on a LAN,
and nothing on those hosts matters much if it leaks.

**That assumption is not load-bearing, and must never become so.** Nothing in LizTerm prevents — or should
prevent — someone pointing it at a production z/OS or z/VM system carrying real data. LizTerm cannot tell the
two apart, and never asks. So defaults are set by the worst case, not the common one: the hobbyist loses
nothing to a conservative default, and the one person on a production LPAR loses a great deal to a permissive
one.

Corollary: "our users are hobbyists" is never a reason to record more, keep it longer, or make it easier to
share. It is a reason to keep the UI simple, which is a different argument.

## What LizTerm writes

Everything lives under one root — `AppPaths.ConfigRoot()`, which is
`~/Library/Application Support/LizTerm` on macOS, `%APPDATA%\LizTerm` on Windows, and
`$XDG_CONFIG_HOME/LizTerm` (or `~/.config/LizTerm`) elsewhere.

| What | Where | Holds | Unix mode |
|---|---|---|---|
| Wire logs | `logs/wire-<profile>-<timestamp>.log` | **The whole session, both directions** | file `0600`, directory `0700` |
| Profiles | `profiles/<name>.json` | Host, port, TLS settings, LU name, note, tags, REST URL and userid, pinned certificates | default |
| Settings | `settings.json` | Preferences only | default |
| Tags | `tags.json` | Tag names and colours | default |
| Recent hosts | `recent-hosts.json` | Hosts typed into Quick Connect | default |
| CA file | temp, per session | Public certificates | `0600` |

**LizTerm never writes a password anywhere.** The only one it handles is the mvsMF REST sign-in: `SignInHolder`
asks for it once, trades it for a session token, and drops it; only the token is held, and it is forgotten when the
window closes. Profiles store a *userid*, never a password.

**Outbound network traffic** is the host connection itself, the optional mvsMF REST calls, and — when the user
leaves the update check on — an unauthenticated GET to
`https://api.github.com/repos/coffeemuse/LizTerm/releases/latest`. Nothing else leaves the machine. LizTerm has
no telemetry and should never acquire any.

## What a wire log contains

A wire log is **a full session recording**, not a debug trace. Every JSON line between LizTerm and b3270, both
directions, timestamped.

- **Outbound**: every keystroke, as `String` actions. Passwords included. A password typed as part of a longer
  command — `LOGON JOHN/SECRET`, a `/password` operand, a dataset password — is recorded in full, because it is
  simply text the user typed.
- **Inbound**: every screen the host painted, as `screen` indications. Whatever data the session displayed.

The inbound half is the bigger exposure and **cannot be redacted**, because the screens are the entire
diagnostic value of the log. Strip them and there is nothing left worth keeping. Any redaction work is therefore
capped at the outbound half, and a redacted log is still a recording of everything the user looked at.

### What the 3270 non-display attribute does and does not buy

b3270 replaces every character in a non-display field with a space before the screen indication is written
(`Common/b3270/screen.c`, the `FA_IS_ZERO` branch), and none of its twelve `gr` flags marks the field as hidden.
Two consequences, and they are both worth remembering before anyone proposes this again:

1. **The inbound half is already safe for non-display fields.** A wire log never contains a password typed into
   a proper password field, because b3270 never sends it.
2. **LizTerm cannot detect such a field**, because the information is destroyed upstream. Reading it would mean
   a `ReadBuffer` round trip per keystroke, or patching the engine — which would make the wire log's privacy
   depend on *which* engine is running, silently weaker under `LIZTERM_B3270_PATH`. Bad property for a privacy
   feature.

So field-based redaction would protect the case that is already half-safe, and miss the visible-field case
(`LOGON JOHN/SECRET`) entirely, where the password is in **both** directions.

## What protects a log today

- **Off by default, per session, non-persistent.** A log exists only because someone deliberately turned it on
  in the window it applies to. `LIZTERM_WIRE_LOG` is the developer route and is documented as such.
- **A modal warning before every start**, saying what a log records and where it lands. Not a checkbox in
  Preferences: `AppSettings.WarnBeforeWireLog` exists for someone recording fixtures all day, and is hidden on
  purpose so the reminder is not one click from being silenced forever.
- **Owner-only on Unix**: the file is `0600` and the directory `0700`.
- **Nothing is auto-shared.** The user finds the file and attaches it themselves.

## Rules this settles

- **#79 must not ship a global always-on default.** An always-on log turns a deliberate, short-lived artifact
  into a persistent recording of a production session with nothing pruning it. Per-profile at most, and retention
  lands before any of it.
- **#55 must never put a wire log in an export bundle.** Already stated there; this is why.
- **An error message may name the *shape* of what was sent, never its content.** `RunOperation.Describe` prints
  action names and argument *sizes*. No action is exempted for having harmless-looking arguments today — a
  safe-list is a privacy landmine the first time someone adds to it.
- **The user guide's warning stays**, whatever redaction lands. A scrubbed log is still a session recording.

## Known gaps

- **`profiles/`, `settings.json`, `tags.json` and `recent-hosts.json` are created with default permissions**
  (`0755` directories, `0644` files). They hold no passwords, but they do hold host names, LU names, userids and
  notes. Lower severity than a wire log, and not yet fixed — `AppPaths.EnsureDirectory` is there to build on.
- **A password echoed into a *visible* field is in the inbound screen lines**, and no redaction reaches it
  without destroying the log.
- **Nothing prunes `logs/`.** A log runs until it is turned off; old ones stay until deleted by hand. This is
  #79's retention question and is a precondition for any always-on default.
