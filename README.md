# LizTerm

[![CI](https://github.com/coffeemuse/LizTerm/actions/workflows/ci.yml/badge.svg)](https://github.com/coffeemuse/LizTerm/actions/workflows/ci.yml)

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

- `LIZTERM_B3270_PATH`: use this b3270 instead of the bundled one. The app and the integration
  test project use the bundled b3270 when built after `native/build/build-macos.sh` has run;
  otherwise set this.
- `LIZTERM_WIRE_LOG`: append every protocol line in both directions to this file (attach to bug reports).
- `LIZTERM_TEST_HOST`: `host[:port]` for the opt-in integration tests. Add `LIZTERM_TEST_TLS=1` for a TLS host
  and `LIZTERM_TEST_VERIFY_CERT=0` to accept a self-signed certificate.
- `LIZTERM_REQUIRE_ENGINE`: any non-blank value makes the engine smoke test fail instead of skip when no bundled
  b3270 is in the test output. CI sets it on the macOS job; leave it unset locally.

`native/build/build-playback.sh` builds x3270's `playback` tool, for replaying a captured host
trace against a live b3270 during local development.

## Continuous integration

Two GitHub Actions workflows under `.github/workflows`:

- `ci.yml` runs on every push, pull request, and manual dispatch: one Linux job, `test`, that builds the solution in
  Release with warnings as errors and runs the full suite. No engine is needed; the integration lane skips itself.
- `platforms.yml` runs on pushes to `main`, on manual dispatch, and on pull requests that touch the workflow or
  `native/`: `engine-macos` builds b3270 with `native/build/build-macos.sh` (whose `verify-macos.sh` fails the job on
  any non-system dynamic dependency), uploads it as the `b3270-osx-arm64` artifact, then runs the suite with
  `LIZTERM_REQUIRE_ENGINE=1` so the engine smoke test must start the freshly built binary; `test-windows` runs the
  suite on Windows.

To run the platform jobs by hand: Actions, Platforms, "Run workflow", or `gh workflow run platforms.yml`. A failed
run uploads its `.trx` files as `test-results-<os>`. The macOS run's `b3270-osx-arm64` artifact is a CI-built engine
you can download and drop into `native/out/osx-arm64/`; downloaded artifacts lose the executable bit, so run
`chmod +x native/out/osx-arm64/b3270` and rebuild.

## Recording protocol fixtures

`tools/record-fixture.sh <trace.trc> <out.jsonl>` replays an x3270 host trace through b3270 and saves its output, and
`tools/wirelog-to-fixture.sh <wire.log> <out.jsonl>` trims a `LIZTERM_WIRE_LOG` file from a real session to the same format;
see `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`.

## Licenses of bundled components

- x3270 / b3270: BSD-3-Clause, Copyright Paul Mattes and others.
- IBM 3270 font by Ricardo Banffy: SIL Open Font License 1.1 (`src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt`).
- LizTerm's own license: to be decided.
