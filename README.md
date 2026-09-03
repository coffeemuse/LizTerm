# LizTerm

A cross-platform TN3270 client for retro mainframe hobbyists: macOS, Linux, and Windows, one UI, no install
dependencies. Built with .NET 10 and Avalonia on top of the b3270 engine from the x3270 suite.

Design: `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md`.

## Developer setup (macOS)

1. .NET 10 SDK, Xcode command line tools, Homebrew `openssl@3`.
2. Build the emulator engine once: `native/build/build-macos.sh` (produces `native/out/osx-<arch>/b3270`,
   statically linked against OpenSSL; the app project copies it into its output).
3. `dotnet test LizTerm.slnx`
4. `dotnet run --project src/LizTerm.App` (or `-- profile-name`, or `-- host:port`).

Environment variables:

- `LIZTERM_B3270_PATH`: use this b3270 instead of the bundled one.
- `LIZTERM_WIRE_LOG`: append every protocol line in both directions to this file (attach to bug reports).
- `LIZTERM_TEST_HOST`: `host[:port]` for the opt-in integration tests.

## Recording protocol fixtures

`tools/record-fixture.sh <trace.trc> <out.jsonl>` replays an x3270 host trace through b3270 and saves its output;
see `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`.

## Licenses of bundled components

- x3270 / b3270: BSD-3-Clause, Copyright Paul Mattes and others.
- IBM 3270 font by Ricardo Banffy: SIL Open Font License 1.1 (`src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt`).
- LizTerm's own license: to be decided.
