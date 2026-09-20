# Architecture

LizTerm is four .NET projects around one decision: it does not implement the 3270 data stream. Emulation —
TN3270E, TLS, code pages, extended attributes, IND$FILE — is done by `b3270`, the headless member of the x3270
suite, which LizTerm starts as a child process and talks to over stdin and stdout in newline-delimited JSON.
LizTerm is the client experience around it. The reasoning, and the alternatives that were rejected, are in the
[v1 design spec](superpowers/specs/2026-09-03-lizterm-v1-design.md#2-core-decision-b3270-as-the-emulation-engine).

## Projects

| Project | Role | Depends on |
|---|---|---|
| `src/LizTerm.Core` | The domain model: screen snapshots, connection and keyboard state, profiles and settings, the `IEmulatorSession` interface, host-neutral file access (`IHostFileService`), certificate utilities, the release check. | The BCL only |
| `src/LizTerm.Backend.B3270` | `B3270Session`, the implementation of `IEmulatorSession`: process host, JSON protocol, engine locator, wire log. | Core |
| `src/LizTerm.Backend.Mvsmf` | `MvsmfFileService`, the implementation of `IHostFileService`: the HTTP client for mvsMF used by mvsMF Access; the only project that knows mvsMF exists. | Core |
| `src/LizTerm.App` | The Avalonia UI: splash, profile picker and editor, session window, terminal control, dialogs, menus, and mvsMF Access, which reaches its backend through `HostFileServiceFactory`. | Core, and each backend in one file only |

**The dependency rule.** Core never mentions Avalonia, b3270 or mvsMF. Each backend is the only project that knows
its product exists, and the two never reference each other. The App names the b3270 backend in exactly one file,
`src/LizTerm.App/SessionFactory.cs`, and the mvsMF backend in exactly one file,
`src/LizTerm.App/HostFileServiceFactory.cs`; everything else talks to `IEmulatorSession` and `IHostFileService`.
That is what lets the App tests run against fakes, and what keeps a future managed engine possible. It is enforced
in review, not by tooling.

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

## The release check

The release check is the only HTTP request the app makes on its own. mvsMF Access, and the profile editor's
Test button, talk only to the URL a profile names, and only when the user asks. Core's `GitHubReleaseChecker` asks
GitHub's releases API for the latest release
(`GET https://api.github.com/repos/coffeemuse/LizTerm/releases/latest`, with a 10 s timeout and the User-Agent
`LizTerm/<version>`) when the app starts, if Preferences > General > Updates is on, and whenever Help > Check for
Updates... is chosen. The App compares that version with its own and decides whether to say anything. The endpoint
never returns a draft, so installed copies hear about a release only once it is published (see
[CI and release](ci-and-release.md#publishing)).

## Where the detail lives

- [Development](development.md): building, testing, conventions.
- [Engines](engines.md): how b3270 is built and verified for each platform.
- [CI and release](ci-and-release.md): the workflows, packaging and signing.
- `docs/superpowers/`: the design spec and implementation plan for each milestone. These are a historical record;
  where they disagree with the code or with these pages, the code and these pages win.
- Each project's `CLAUDE.md` holds the detailed implementation notes and pitfalls for that project.
