# LizTerm

[![CI](https://github.com/coffeemuse/LizTerm/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/coffeemuse/LizTerm/actions/workflows/ci.yml?query=branch%3Amain)
[![Platforms](https://github.com/coffeemuse/LizTerm/actions/workflows/platforms.yml/badge.svg?branch=main)](https://github.com/coffeemuse/LizTerm/actions/workflows/platforms.yml?query=branch%3Amain)

A cross-platform TN3270 client for retro mainframe hobbyists: macOS, Linux, and Windows, one UI, no install
dependencies. Built with .NET 10 and Avalonia on top of the b3270 engine from the x3270 suite.

Design: `docs/superpowers/specs/2026-09-03-lizterm-v1-design.md`.

## Developer setup (macOS)

1. .NET 10 SDK, 10.0.4xx band (`global.json` pins it, and rolls forward only within that band), Xcode command
   line tools, Homebrew `openssl@3`.
2. Build the emulator engine once: `native/build/build-macos.sh` (produces `native/out/osx-<arch>/b3270`,
   statically linked against OpenSSL; the app project copies it into its output).
3. `dotnet test LizTerm.slnx`
4. `dotnet run --project src/LizTerm.App` (or `-- profile-name`, or `-- host:port`).

Environment variables:

- `LIZTERM_B3270_PATH`: use this b3270 instead of the bundled one. The app and the integration
  test project use the bundled b3270 when built after `native/build/build-macos.sh` (or, on Linux,
  `native/build/build-linux-docker.sh`, or on Windows, `native/build/build-windows-docker.sh`) has run;
  otherwise set this.
- `LIZTERM_WIRE_LOG`: append every protocol line in both directions to this file (attach to bug reports).
- `LIZTERM_TEST_HOST`: `host[:port]` for the opt-in integration tests. Add `LIZTERM_TEST_TLS=1` for a TLS host
  and `LIZTERM_TEST_VERIFY_CERT=0` to accept a self-signed certificate.
- `LIZTERM_REQUIRE_ENGINE`: any non-blank value makes the engine smoke test fail instead of skip when no bundled
  b3270 is in the test output. CI sets it on the macOS and Linux engine jobs and on `test-windows`; leave it unset
  locally. A b3270 that *is* in the test output but is not executable fails the test either way, so a forgotten
  `chmod +x` cannot pass as a skip.

`native/build/build-playback.sh` builds x3270's `playback` tool, for replaying a captured host
trace against a live b3270 during local development.

## Building the Linux engines

`native/build/build-linux-docker.sh` produces `native/out/linux-x64/b3270` or `native/out/linux-arm64/b3270`,
whichever the container resolves to (the host's architecture, unless Docker has been pointed elsewhere), and needs
only Docker. The build runs inside `almalinux:8`, pinned by digest in
`native/build/linux-image.sh`: that image's glibc 2.28 is the oldest release LizTerm supports and is also .NET 10's
own floor, so on every glibc distribution .NET supports the engine is never the thing that decides where the app
can run. The digest pins the base layer rather than the toolchain — the build still installs gcc and friends from
live AlmaLinux 8 repositories, into an image derived from the pinned base and built once rather than on every run —
so the floor rests on RHEL 8's frozen glibc ABI and, as the backstop, on the gate's symbol check. OpenSSL and expat are built from pinned source and linked statically, so the binary asks the target
system for nothing but glibc.

The gate is the last part of the build, not a separate step to remember: `verify-linux.sh` fails it if the binary
has any dynamic dependency outside the glibc runtime, imports a glibc symbol newer than 2.28, or reports no
OpenSSL TLS provider, and `verify-linux-start.sh` then runs the result in a bare container from the same image. The
TLS check is `shared-verify-tls.sh`, which the macOS gate calls too. Alpine and other musl distributions are a
different runtime identifier and are not built here.

## Building the Windows engine

`native/build/build-windows-docker.sh` needs only Docker and always produces `native/out/win-x64/b3270.exe`,
whatever architecture the host or the container is — x3270 knows only one 64-bit Windows host, so unlike Linux
there is no runtime identifier to resolve. The cross build runs inside `debian:12-slim`, pinned by digest in
`native/build/windows-image.sh`: chosen as a build host rather than a floor, because a PE binary shares no ABI
with the Linux container that cross-compiled it, so none of the image's own properties reach a Windows user the
way AlmaLinux's glibc floor reaches a Linux one. Nothing but the x3270 source itself needs pinning: TLS is
Schannel, reached through the ordinary Windows import libraries rather than a linked OpenSSL (the source
references an `SSLLIB` that is never defined), and expat is bundled in the source tree and built by the suite's
own target, so there is no OpenSSL or expat prefix to fetch, pin, or stamp the way Linux needs.

The gate is split across the two machines that can each check half of it, because a PE binary cannot run on the
Linux box that built it: `verify-windows.sh`, inside the container, fails the build if the binary is not a
64-bit PE (`i386:x86-64`, so a plausible `i686-w64-mingw32` accident cannot pass as `win-x64`) or if it imports
any DLL outside eleven Windows system DLLs — a `libwinpthread-1.dll` or `libgcc_s_seh-1.dll` here would mean the
engine cannot start without the MinGW runtime beside it. `shared-verify-tls.sh` then runs on a real Windows
machine in CI and asserts the banner reports `Windows Schannel`, the same check the macOS and Linux gates make of
their own OpenSSL builds. `win-arm64` ships this same `win-x64` binary rather than a native build: x3270 4.5ga6
knows no aarch64 Windows host, so Windows 11's x64 emulation is the only route until upstream is patched.

## Continuous integration

Two GitHub Actions workflows under `.github/workflows`:

- `ci.yml` runs on pushes to `main`, on every pull request, and on manual dispatch: one Linux job, `test`, that
  builds the solution in Release with warnings as errors and runs the full suite. No engine is needed; the live
  host tests and the engine smoke test skip themselves.
- `platforms.yml` runs on pushes to `main`, on manual dispatch, and on pull requests touching the workflow,
  `native/`, `src/`, `tests/`, `global.json`, or the `Directory.*.props` files: `engine-macos` builds b3270 with
  `native/build/build-macos.sh` (whose `verify-macos.sh` fails the job on any non-system dynamic dependency, or on a
  binary built without TLS), runs the suite with `LIZTERM_REQUIRE_ENGINE=1` so the engine smoke test must start the
  freshly built binary, and only then uploads it as the `b3270-osx-arm64` artifact; `engine-linux` does the same on
  two legs, `ubuntu-24.04` and `ubuntu-24.04-arm`, uploading `b3270-linux-x64` and `b3270-linux-arm64`, and also runs
  the gate against the binary it just built plus two binaries it must reject — each rejection checked against the
  message that arm of the gate prints — so a gate that has stopped rejecting anything fails the job instead of
  passing everything. The arm64 Linux leg runs the whole solution; the x64 one runs only
  `tests/LizTerm.Integration.Tests`, since `ci.yml`'s `test` already covers that solution on the same OS and
  architecture. `engine-windows` cross-builds `win-x64` inside a pinned Debian container and proves only the half
  of the gate a Linux runner can check about a binary it cannot start — machine type and DLL imports; `test-windows`
  now depends on it (`needs: engine-windows`), downloads that unverified engine, proves on a real Windows machine
  that it starts and reports `Windows Schannel`, runs the suite with `LIZTERM_REQUIRE_ENGINE=1`, and republishes it
  as the verified `b3270-win-x64` artifact.

To run the platform jobs by hand: Actions, Platforms, "Run workflow", or `gh workflow run platforms.yml`. A run that
fails or is cancelled uploads `test-results-<os>` with its `.trx` files and any hang dump. The `b3270-osx-arm64`,
`b3270-linux-x64`, `b3270-linux-arm64` and `b3270-win-x64` artifacts are CI-built engines that have been started by
the smoke test before being published (the last one twice over: `engine-windows` uploads it first as
`b3270-win-x64-unverified`, before anything has run it, and `test-windows` republishes the same bytes as
`b3270-win-x64` only once a Windows machine has); you can download one and drop it into the matching
`native/out/<rid>/`, but a downloaded macOS or Linux artifact loses its executable bit, so run
`chmod +x native/out/<rid>/b3270` and rebuild — `native/out/win-x64/b3270.exe` needs no such fix, since Windows has
no executable bit to lose.

## Recording protocol fixtures

`tools/record-fixture.sh <trace.trc> <out.jsonl>` replays an x3270 host trace through b3270 and saves its output, and
`tools/wirelog-to-fixture.sh <wire.log> <out.jsonl>` trims a `LIZTERM_WIRE_LOG` file from a real session to the same format;
see `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`.

## Licenses of bundled components

- x3270 / b3270: BSD-3-Clause, Copyright Paul Mattes and others.
- IBM 3270 font by Ricardo Banffy: SIL Open Font License 1.1 (`src/LizTerm.App/Assets/Fonts/LICENSE-3270font.txt`).
- LizTerm's own license: to be decided.
