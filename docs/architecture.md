# Architecture

LizTerm is three .NET projects around one decision: it does not implement the 3270 data stream. Emulation —
TN3270E, TLS, code pages, extended attributes, IND$FILE — is done by `b3270`, the headless member of the x3270
suite, which LizTerm starts as a child process and talks to over stdin and stdout in newline-delimited JSON.
LizTerm is the client experience around it. The reasoning, and the alternatives that were rejected, are in the
[v1 design spec](superpowers/specs/2026-09-03-lizterm-v1-design.md#2-core-decision-b3270-as-the-emulation-engine).

## Projects

| Project | Role | Depends on |
|---|---|---|
| `src/LizTerm.Core` | The domain model: screen snapshots, connection and keyboard state, profiles, the `IEmulatorSession` interface, certificate utilities. | The BCL only |
| `src/LizTerm.Backend.B3270` | `B3270Session`, the implementation of `IEmulatorSession`: process host, JSON protocol, engine locator, wire log. | Core |
| `src/LizTerm.App` | The Avalonia UI: splash, profile picker and editor, session window, terminal control, dialogs, menus. | Core, and the backend in one file only |

**The dependency rule.** Core never mentions Avalonia or b3270. The backend is the only project that knows b3270
exists. The App names the backend in exactly one file, `src/LizTerm.App/SessionFactory.cs`; everything else talks
to `IEmulatorSession`. That is what lets the App tests run against a fake session, and what keeps a future managed
engine possible. It is enforced in review, not by tooling.

## How a session works

- **One window, one session, one profile.** A session is bound to its profile when it is created; connecting
  takes no host.
- **Snapshots, not shared state.** The backend owns a mutable screen buffer and is its only writer. After each
  update from b3270 it publishes an immutable snapshot that carries the cursor, so screen and cursor never tear.
  The UI only ever sees snapshots.
- **One event thread.** A backend raises every event on one dedicated thread, in order, and knows nothing about UI
  threads; the App marshals onto Avalonia's dispatcher.
- **Tagged commands.** Each command sent to b3270 carries a tag, and b3270's matching `run-result` completes the
  task that sent it.
- **Zero-based coordinates.** Rows and columns are zero-based everywhere in LizTerm. b3270 reports them one-based,
  and the conversion happens in exactly one place in the backend.
- **Engine faults are recoverable.** If the engine process dies, the session reports a fault with the engine's last
  stderr lines, drops to disconnected, and starts a fresh engine on the next connect.

## Where the detail lives

- [Development](development.md): building, testing, conventions.
- [Engines](engines.md): how b3270 is built and verified for each platform.
- [CI and release](ci-and-release.md): the workflows, packaging and signing.
- `docs/superpowers/`: the design spec and implementation plan for each milestone. These are a historical record;
  where they disagree with the code or with these pages, the code and these pages win.
- Each project's `CLAUDE.md` holds the detailed implementation notes and pitfalls for that project.
