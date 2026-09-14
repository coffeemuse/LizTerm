# The b3270 engine

LizTerm does not implement the 3270 data stream. Every build bundles `b3270`, the headless member of the x3270
suite, pinned at 4.5ga6 with [LizTerm's patches](#patches) applied, and runs it as a child process. This page covers
how the app finds its engine, how each platform's engine is built, and the checks an engine must pass before anything
ships it. How CI runs these builds is in [CI and release](ci-and-release.md).

## How the app finds the engine

At build time, `LizTerm.App.csproj` copies `native/out/<engine-rid>/b3270*` into its output as
`runtimes/<target-rid>/native/<filename>` — `b3270`, or `b3270.exe` on Windows, since the copy item links by
`%(Filename)%(Extension)` rather than a fixed name — but only if that directory exists **when the project builds**.
Two properties choose the paths:

- `LizTermTargetRid` picks where the copy lands: `$(RuntimeIdentifier)` when publishing for one, else the SDK's own
  `$(NETCoreSdkRuntimeIdentifier)` for `dotnet run`, the test projects, and other RID-less builds.
- `LizTermEngineRid` picks which `native/out/` directory it reads: the same, except that `win-arm64` maps to
  `win-x64`, so a `win-arm64` publish ships the x64 engine under Windows 11's emulation.
  `native/build/verify-bundled-engine.sh` encodes the same mapping, and the two must agree.

The App test project inherits the copy through its project reference to the App. The integration test project
keys both ends on the host RID alone, which is correct because it is never published for a target RID.

At run time `B3270Locator` looks at `LIZTERM_B3270_PATH`, then `runtimes/<rid>/native/`, then beside the app. It
reports a file that is present but not executable separately from one that is missing — a lost executable bit is
the classic result of unpacking an archive. With no engine at all, connecting throws `BackendUnavailableException`.
For development only, `LIZTERM_B3270_PATH` accepts any b3270 4.2 or later, such as a Homebrew `x3270` install. Such an
engine lacks LizTerm's [patches](#patches), so the backend refuses ISPF (MVS) transfers on it.

After building an engine, rebuild the .NET projects so the copy happens. `native/cache`, `native/build-tmp` and
`native/out` are gitignored.

## Pinned sources

Every downloaded source is a pin: a `fetch-*.sh` script holding a version, a checksum and a URL, which delegates
downloading, verifying and extracting to `shared-fetch-tarball.sh`. `shared-sha256.sh` is the one place that knows
the Linux container has `sha256sum` and macOS has `shasum`, and `shared-verify-tls.sh` is the banner check every
platform's gate makes. Anything named `shared-*.sh` is in every engine's CI cache key by construction.

OpenSSL (3.5.8) and expat (2.8.4) are built from pinned tarballs (`fetch-openssl.sh`, `fetch-expat.sh`) and linked
statically wherever they are needed, rather than taken from a package manager or a container image. A statically
linked library never receives a distribution's security updates, so its version has to be ours to bump
deliberately.

Each static prefix under `native/build-tmp` carries a `.pin` stamp holding the SHA-256 of its fetch script **and**
of the build script that configures it, and a prefix is reused only when both the archive and a matching stamp are
present. Without the stamp, bumping a pin and rebuilding without clearing `build-tmp` would link the old library
and skip the new checksum. Both files are hashed because a prefix has two owners — the fetch script holds the
version and checksum, the build script holds the configure flags — and a change to either has to rebuild. The build
script is hashed whole, so even a comment edit rebuilds both prefixes; over-rebuilding is the safe direction for a
guard whose other failure mode has no symptom.

## Patches

LizTerm carries its own changes to b3270 as unified diffs in `native/patches/`, applied to the pinned source on
every build. The policy:

- **Only what LizTerm's engine uses.** A patch changes what b3270 needs and nothing else: no help text, interactive
  prompts, X resources or Motif dialogs for x3270's other front ends. The smaller the diff, the more easily it
  re-applies to the next x3270 release.
- **Maintained whether or not upstream adopts it.** A patch is dropped only when an upstream release carries
  equivalent behaviour.

There is one patch today. `b3270-transfer-commandprefix.patch` adds a `CommandPrefix` keyword to the `Transfer`
action: text typed, with a space, ahead of the IND$FILE command. The ISPF (MVS) host type sends `CommandPrefix=TSO`,
which makes ISPF hand the command to TSO (#109). It has to live in the engine, because `Transfer` blanks the input
field (`kybd_prime`) before it types the command, so `TSO` typed at the cursor first would be erased.

**An engine without it does not refuse the keyword.** 4.5ga6 silently ignores a `Transfer` keyword it does not
know: the "Unknown option" check in `parse_ft_keywords` sits inside the loop over known keywords and never fires.
So the backend never relies on the engine to object. Before an ISPF transfer, `B3270Session` looks for the marker
below in the engine binary (`EnginePatches`), once per session, and fails the transfer at once without it.

**How patches are applied.** `fetch-source.sh` extracts the tarball through `shared-fetch-tarball.sh`, which starts
from a fresh tree every time, then applies every `native/patches/*.patch` in name order with
`patch -p1 -N -F0 --batch`, sending `patch`'s output to stderr because its callers read stdout as the source
directory. A patch that no longer applies fails the build rather than producing an engine without it. `-F0` allows
no fuzz, so a hunk is never applied against context that no longer matches; a hunk whose code merely moved still
applies at its new line. The Linux and Windows build images install `patch`; macOS has it. `.gitattributes` keeps
patch files LF on every checkout, because the tarball's sources are LF.

**How a build proves it.** `native/patches/**` is in all four engine output cache keys in `engines.yml`, so an edit
to a patch alone rebuilds the engines instead of restoring cached ones. `shared-verify-patches.sh` fails unless the
binary contains each patch's marker, a string only the patched source puts there (`CommandPrefix`; a stock 4.5ga6
b3270 does not contain it). It reads bytes and never runs the binary, so it covers the engines a runner cannot
execute. `verify-macos.sh`, `verify-linux.sh` and `verify-windows.sh` call it as their last arm, each gate's step in
`engines.yml` rejects a copy of the freshly built engine with its marker defaced (re-signed ad hoc on macOS, so the
TLS arm can still run it), and `verify-bundled-engine.sh` calls it for every packaged archive. A new patch adds its
marker to `MARKERS` in that script; the backend's `EnginePatchesTests` holds `EnginePatches.CommandPrefixMarker` to
the same name.

**Writing or regenerating a patch.** Extract the tarball twice, rename the trees `a` and `b`, edit `b`, and from
their parent directory run `diff -u` on each changed file (`diff -u a/Common/ft.c b/Common/ft.c`), stripping the
timestamps from the `---` and `+++` lines. Keep the prose preamble at the top of the file; `patch` skips it. Check
the result against a fresh extract with the same `patch` command `fetch-source.sh` uses.

**Bumping x3270.** A patch that no longer applies fails `fetch-source.sh`: regenerate it against the new tarball as
above. When an upstream release carries the same behaviour, delete the patch, its marker and `EnginePatches`' check;
if upstream spells the keyword differently, change `TransferMapper` with it.

## macOS

```bash
native/build/build-macos.sh [osx-arm64|osx-x64]
```

Defaults to the host's architecture; `osx-x64` cross-builds on Apple Silicon. Needs the Xcode command line tools
and nothing from Homebrew. It builds OpenSSL from its pin into a per-architecture static prefix under
`native/build-tmp/<rid>`, downloads and checksums x3270 4.5ga6, links OpenSSL statically, and writes
`native/out/<rid>/b3270`.

The build ends with `verify-macos.sh`, which fails it if:

- the binary's machine type disagrees with the RID it was built for. This catches a `--host`-only cross build
  whose `configure` sanity check silently fell back to cross-compilation defaults instead of running a binary it
  could not execute;
- `otool -L` shows any dependency outside `/usr/lib` or `/System/Library`;
- the banner reports no OpenSSL TLS provider;
- the binary does not carry LizTerm's patch marker, checked last ([Patches](#patches)).

The TLS check executes the finished binary, so verifying an `osx-x64` build on Apple Silicon needs Rosetta.

## Linux

```bash
native/build/build-linux-docker.sh
```

Needs only Docker. Produces `native/out/linux-x64/b3270` or `native/out/linux-arm64/b3270` — whichever architecture
the **container** is, which is the host's unless Docker has been pointed elsewhere.

**The glibc floor.** The build runs in `almalinux:8`, pinned by digest in `native/build/linux-image.sh`. Its glibc
2.28 is the oldest LizTerm supports and is also .NET 10's own floor, so on every glibc distribution .NET supports,
the engine is never what decides where the app can run. It is pinned by digest because a floating tag could raise
the base layer silently. The digest pins the base layer, not the toolchain: gcc,
binutils and python3 still come from live AlmaLinux 8 repositories, and glibc can move within the 2.28 stream. What
actually holds the floor is RHEL 8's frozen glibc ABI, with the gate's symbol check as the backstop. When bumping
the digest, use the multi-architecture **index** digest; a platform manifest digest breaks one leg of the CI
matrix.

Those packages go into an image derived from the pinned base, tagged with the base digest and the package list, and
built once rather than `dnf install`ed into a fresh container on every run. Changing either input builds a new
image instead of reusing the old one.

**The RID comes back from the container.** `build-linux.sh` records the RID it resolved in `native/build-tmp/rid`,
and the wrapper reads that back rather than deriving one from the host's `uname -m`. The two disagree whenever
Docker's platform is not the host's — with `DOCKER_DEFAULT_PLATFORM=linux/amd64`, an arm64 Mac builds `linux-x64` —
and a host-derived path would hand the start check a binary the build never wrote.

**OpenSSL and expat** are both built from their pins and linked statically. expat is not optional: b3270's
`configure` hard-errors without it and offers no `--without-expat`, and AlmaLinux 8 packages no static expat, so
linking the image's would put `libexpat.so.1` on the gate's rejection list.

**Do not remove `LIBS="-ldl -pthread"`** from `build-linux.sh`'s configure line. x3270's `configure` probes for
OpenSSL by linking, and static libcrypto cannot link without those. Drop them and `configure` quietly disables TLS;
the resulting `TLS provider: None` binary is smaller and links *fewer* libraries, so it passes the dependency and
floor checks more comfortably than the real engine does. This was measured, not guessed.

**The gate** is five checks. `verify-linux.sh`, inside the container, rejects:

1. any dynamic dependency outside the glibc runtime;
2. any imported glibc symbol newer than 2.28 — `ldd` alone passes a binary built on Ubuntu 24.04 that cannot start
   on RHEL 9;
3. a banner that reports no OpenSSL TLS provider (through `shared-verify-tls.sh`);
4. a binary without LizTerm's patch marker (through `shared-verify-patches.sh`; see [Patches](#patches)).

Then `verify-linux-start.sh` runs the result in a bare container from the same image (5). It runs on the host,
because a script already inside a container cannot start another one.

CI also proves the gate can still *reject*. One container invocation runs `verify-linux.sh` against the built
binary and then against a fixture for three of its arms: `/usr/bin/bash`, which links `libtinfo`, for the dependency
allowlist; a `/bin/true` copied out of a digest-pinned `debian:12-slim` for the glibc floor (it needs no compiler, so
the same fixture works on a Mac); and a copy of the built engine with its patch marker defaced, for the patch arm.
Each rejection is asserted **by the message its own arm prints**, not by a non-zero exit, because the wrapper also
exits non-zero for a Docker Hub rate limit or a failed `dnf install`.

Alpine and other musl distributions are a different RID and out of scope.

## Windows

```bash
native/build/build-windows-docker.sh
```

Needs only Docker, and always produces `native/out/win-x64/b3270.exe`, whatever the host's or container's
architecture: x3270 knows exactly one 64-bit Windows host, so unlike Linux there is no RID for the container to
resolve.

It cross-builds with mingw-w64 inside `debian:12-slim`, pinned by digest in `native/build/windows-image.sh`. That
image is a build host, not a floor: a PE binary shares no ABI with the machine that built it, so none of the image's
properties reach a Windows user, and any distribution with the mingw-w64 toolchain would do. Its packages
(`gcc-mingw-w64-x86-64`, `binutils-mingw-w64-x86-64`, `make`, `python3`, `curl`, `ca-certificates`, `patch`) go into a
derived image the same way the Linux ones do.

Only the x3270 source is pinned. TLS is Schannel, reached through the ordinary Windows import libraries
(`-lcrypt32 -lsecur32`) rather than a linked OpenSSL — `wb3270/Makefile.obj.in`'s `LIBS` line references an
`SSLLIB` that is never defined anywhere in the tree — and expat is bundled upstream at `extern/libexpat` and built
by the suite's own target. So there is no OpenSSL or expat pin here, and no `.pin` stamp to guard.

**The gate is split across two machines**, because a PE binary cannot run on the Linux box that built it:

- `verify-windows.sh`, inside the container, fails the build if `objdump -f` does not report `i386:x86-64`, or if
  the import table names anything outside eleven Windows system DLLs: `advapi32.dll`, `comdlg32.dll`,
  `crypt32.dll`, `gdi32.dll`, `kernel32.dll`, `msvcrt.dll`, `secur32.dll`, `shell32.dll`, `user32.dll`,
  `winspool.drv` and `ws2_32.dll`. The architecture check is there because a 32-bit `i686-w64-mingw32` accident
  would import the same DLLs, so the import check alone cannot see it. A `libgcc_s_seh-1.dll` or
  `libwinpthread-1.dll` in the imports would mean the engine cannot start without the MinGW runtime beside it. Its
  last arm, as on macOS and Linux, fails an engine without LizTerm's patch marker ([Patches](#patches)).
- `shared-verify-tls.sh`, on a real Windows machine in CI, proves the binary starts at all and reports
  `Windows Schannel`.

**`win-arm64` ships this same `win-x64` binary.** 4.5ga6's `Common/dirnames` defines only two Windows hosts,
`win64=x86_64-w64-mingw32` and `win32=i686-w64-mingw32`, and no aarch64 one, so Windows 11's x64 emulation is the
only route until upstream's host detection is patched.

**Schannel cannot pin certificates.** The Windows engine does not offer x3270's `caFile` option, so LizTerm cannot
pin a certificate there: it verifies against the Windows certificate store, and a profile carrying a pin refuses to
connect rather than silently ignoring the pin.

## The TLS banner check

`shared-verify-tls.sh <binary> [provider]` runs the engine and checks that its banner reports the expected TLS
provider, `OpenSSL` by default; the Windows CI job passes `Windows Schannel`. The failure it catches is x3270's
rather than any one platform's — a build that silently lost TLS — and it reports an engine that could not run at
all as its own fault rather than as a missing provider.

## Using a CI-built engine

The Platforms workflow publishes `b3270-osx-arm64`, `b3270-osx-x64`, `b3270-linux-x64`, `b3270-linux-arm64` and
`b3270-win-x64` artifacts, each one gated and started before upload. Drop one into the matching `native/out/<rid>/`
and rebuild. A downloaded macOS or Linux artifact loses its executable bit, so run
`chmod +x native/out/<rid>/b3270` first; `b3270.exe` needs no such fix. Ignore `b3270-win-x64-unverified`: it is
the same bytes before a Windows machine has started them.

## Other tools

`native/build/build-playback.sh` builds x3270's `playback` tool, which `tools/record-fixture.sh` uses to record
replay fixtures from host traces (see [Development](development.md#replay-fixtures)).
