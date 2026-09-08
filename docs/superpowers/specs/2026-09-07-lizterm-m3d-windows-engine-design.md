# LizTerm Milestone 3, plan 3d: the Windows engine

Date: 2026-09-07. Parent spec: `2026-09-03-lizterm-v1-design.md` (section 8.1 names all six targets for v1;
section 8.2 asks for one build script per OS, names the MinGW cross build for Windows, and makes the
dependency check the CI gate). Predecessors: `2026-09-06-lizterm-m3-ci-design.md` (plan 3a, whose
`platforms.yml` this plan adds a job to and whose `test-windows` it changes) and
`2026-09-07-lizterm-m3c-linux-engines-design.md` (plan 3c, whose script split, gate shape and
prove-the-gate-rejects discipline this plan follows). Status: approved in discussion on 2026-09-07; awaiting
review of this text.

## 1. Purpose

`b3270` exists for three targets — `osx-arm64`, `linux-x64`, `linux-arm64` — each built by a script whose
verification gate is the build's own pass/fail, and each proven in CI by starting it through `B3270Session`.
Windows has none. `test-windows` runs the suite with no engine at all, so a Windows user cannot connect to a
host without supplying their own b3270, and both Windows targets of v1's six have nothing to ship. (`osx-x64`
is unbuilt too, but it is a runner away rather than a toolchain away, and no plan owns it yet.)

This plan produces `win-x64` the same way the others were produced, and settles what `win-arm64` will ship.

Milestone 3 after this plan: **3e** publish and release, **3f** the scheduled integration lane.

Decisions taken in the brainstorm on 2026-09-07, after the feasibility spike in section 2:

- **The build is a cross build on Linux; the gate is split across two machines.** Every other engine is
  verified where it is built, because a Linux container can run a Linux binary. A PE binary cannot run on the
  Linux builder, and the check plan 3c proved to be load-bearing — that the engine actually reports a TLS
  provider — requires running it. So `engine-windows` checks what the binary *imports*, and `test-windows`
  checks that it *starts and has Schannel*. Building on the Windows runner through MSYS2 instead would keep
  the gate in one place, at the cost of a slower, less reproducible toolchain than a pinned container, and of
  abandoning the one-script-per-OS-in-a-pinned-environment shape the other two engines share.
- **The builder image is `debian:12-slim`, pinned by digest, not the AlmaLinux 8 image plan 3c pinned.** The
  reason that image exists — glibc 2.28 as the floor LizTerm's users inherit — has no meaning for a PE output
  that shares nothing with the container it was built in. Reusing it would mean an older EPEL mingw-w64
  toolchain chosen for a property this build does not have.
- **The artifact is published in two stages.** Plan 3c holds the rule that a binary which links but cannot be
  spawned is never published. Cross-building strains it, because the artifact upload is how the binary reaches
  the machine that can spawn it. `engine-windows` therefore uploads `b3270-win-x64-unverified`, and
  `test-windows` re-uploads it as `b3270-win-x64` only after starting it. The extra hop is the price of the
  rule staying literally true rather than becoming a convention.
- **`win-arm64` ships the x64 binary.** Section 2 records why this is the only option at 4.5ga6, not a
  shortcut.

## 2. Facts that shape the design

Established by a throwaway spike on 2026-09-07 against the pinned source, `suite3270-4.5ga6`, cross-built in
`debian:12-slim` on an arm64 host. Everything in this section was observed, not assumed.

- **The cross build works unmodified.** `./configure --host=x86_64-w64-mingw32` with the same
  `--enable-b3270 --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 --disable-pr3287
  --disable-x3270if --disable-mitm --disable-playback` set that `build-linux.sh` passes, then `make`, produces
  `obj/x86_64-w64-mingw32/b3270/b3270.exe` (3,647,145 bytes). No patches. Note the output directory is
  `b3270`, not the `wb3270` the source directory is called.
- **Disabling `x3270if` and `pr3287` does not break the b3270 target**, despite `Makefile.windows.in` carrying
  `wc3270 s3270 b3270: x3270if` and `wc3270 s3270 b3270: pr3287` dependency lines. Those targets are simply
  not reached when configure has not created their subdirectories.
- **There is no OpenSSL on Windows and nothing to pin.** `SSLLIB` is referenced by `wb3270/Makefile.obj.in`'s
  `LIBS` line and is never defined anywhere in the tree, so it expands to empty. TLS is Schannel, reached
  through the ordinary system import libraries on that same line:
  `-lws2_32 -lcomdlg32 -lgdi32 -lwinspool -lcrypt32 -lsecur32`. `Common/Win32/sio_schannel.c` is the
  implementation and its `sio_provider()` returns the string `Windows Schannel`.
- **expat is bundled.** `extern/libexpat` is in the source tree and `Makefile.windows.in` builds it as the
  `libexpat` library target. Plan 3c had to pin and statically build expat because AlmaLinux 8 packages no
  static one and no distribution-independent libexpat exists; Windows has the problem solved upstream.
- **The binary is already dependency-clean.** Its complete import list is `ADVAPI32.dll`, `CRYPT32.dll`,
  `GDI32.dll`, `KERNEL32.dll`, `SHELL32.dll`, `Secur32.dll`, `USER32.dll`, `WINSPOOL.DRV`, `WS2_32.dll`,
  `comdlg32.dll`, `msvcrt.dll` — all Windows system DLLs. In particular there is no `libgcc_s_seh-1.dll` and
  no `libwinpthread-1.dll`, the two MinGW runtime DLLs a naive cross build often drags in. No `-static` or
  `-static-libgcc` is needed on the link line. The gate in section 4 still asserts this, because it is a
  property of the toolchain rather than of our configure flags, and nothing else would notice it changing.
- **`win-arm64` cannot be cross-built at 4.5ga6.** `Common/dirnames` defines exactly two Windows hosts,
  `win64=x86_64-w64-mingw32` and `win32=i686-w64-mingw32`, and `configure.in`'s `case "$host"` matches only
  those two (plus their `-pc-msys` equivalents). An `aarch64-w64-mingw32` host falls through to the *unix*
  branch, where it would try to build the X11 programs. Making a native arm64 Windows engine would mean
  patching upstream's host detection and build machinery, which is a different plan with a different risk
  profile. Windows 11 on ARM runs x64 binaries under emulation, and b3270 is a short-lived console child
  process whose work is I/O, so emulation is an acceptable cost for v1.
- **The toolchain requirement is small.** configure looks for `${host}-gcc`, `${host}-gcc-ar` and
  `${host}-windres`, and separately requires a `python3` on the build host for `mkversion.py` and `mkfb.py`.
  On `debian:12-slim` that is `gcc-mingw-w64-x86-64`, `binutils-mingw-w64-x86-64`, `make` and `python3`.
- **It is cheap.** A cold run — `apt-get install`, configure, and `make -j` on an arm64 container — took
  **95 seconds**. Plan 3c's legs were 271s and 210s cold. There are no library prefixes to build, which is
  almost all of the difference.
- **The source pin needs no change.** `fetch-source.sh` already fetches and verifies exactly this tarball;
  the spike's download matched its recorded SHA-256 byte for byte. A second consumer of that script is free.
- **The .NET side is already Windows-ready.** `B3270Locator.FileName` is `b3270.exe` on Windows and the
  executable-bit arm is skipped there, and the App csproj's copy item links by `%(Filename)%(Extension)`, so
  `native/out/win-x64/b3270.exe` lands at `runtimes/win-x64/native/b3270.exe` with no project change. This
  plan is expected to touch no C#.

## 3. The build scripts

Three new files under `native/build`, mirroring the Linux split so a reader who knows one knows the other.
Everything named `shared-*.sh` is reused rather than re-implemented.

### 3.1 `windows-image.sh`

The pinned builder base, sourced rather than executed, exactly as `linux-image.sh` is:

```
LIZTERM_WINDOWS_IMAGE=debian:12-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171
```

Its digest was resolved on 2026-09-07 with the `docker buildx imagetools inspect` incantation `linux-image.sh`
documents, so it is the multi-architecture index digest rather than one platform manifest's.

Its header carries the reasoning a reader will want: that this image is a *build host* and not a floor, so
unlike `linux-image.sh` nothing about the running user's system is being promised by the choice; that the
digest must be the multi-architecture **index** digest, so the build works on an arm64 Mac and an x64 runner
alike; and that it is a `*.sh` under `native/build` and so falls inside the CI cache key by construction.

The two image scripts stay separate files for the reason plan 3c's cache keys are separate: a Debian bump
cannot change the macOS or Linux binaries, and a shared file would force cold rebuilds that cannot differ.

### 3.2 `build-windows.sh`

Runs inside the container. Assumes that container's toolchain the way `build-linux.sh` assumes its own.

1. `fetch-source.sh` into `native/build-tmp/win-x64/src`, unchanged and at the same pin.
2. `./configure --host=x86_64-w64-mingw32` with the component flags from section 2, logging to
   `native/build-tmp/win-x64/configure.log`.
3. `make -j$(nproc)`, logging to `make.log`.
4. Copy `obj/x86_64-w64-mingw32/b3270/b3270.exe` to `native/out/win-x64/b3270.exe`.
5. Run `verify-windows.sh` on the result. The gate is the last part of the build, not a separate step to
   remember.

There are deliberately **no static prefixes, no `fetch-*.sh` pins of our own, and no `.pin` stamps**. Plan
3c's stamp machinery exists because a prefix has two owners and a stale one has no tell; here there is no
prefix. The script should say so, so that a later reader restoring symmetry with `build-linux.sh` does not add
guards that guard nothing.

Nor is there a `LIBS=` hedge. `build-linux.sh` carries `LIBS="-ldl -pthread"` because static libcrypto will
not link without it and configure silently disables TLS when its probe fails. Windows links no static
cryptographic library at all, and section 4's TLS check is what would catch it if that ever changed.

### 3.3 `build-windows-docker.sh`

The wrapper and the entry point, needing only Docker. It sources `windows-image.sh`, builds a derived image
tagged from the base digest and the package list — `gcc-mingw-w64-x86-64`, `binutils-mingw-w64-x86-64`,
`make`, `python3` — so `apt-get install` is a layer built once rather than a cost paid on every invocation,
and bumping either input builds a new image instead of reusing the old one. Then it runs `build-windows.sh`
inside it.

One deliberate difference from `build-linux-docker.sh`, worth a comment in the file because its absence is
otherwise surprising: **there is no RID handshake.** `build-linux.sh` writes the RID the container resolved to
`native/build-tmp/rid` for the wrapper to read back, because with `DOCKER_DEFAULT_PLATFORM=linux/amd64` the
container's architecture and the host's disagree and a host-derived path would name a directory the build
never wrote. Here the *target* is `win-x64` whatever the container is, so the path is known before the
container starts and there is nothing to hand back.

## 4. The gate

Two halves, on two machines, because no one machine can run both.

### 4.1 `verify-windows.sh` — imports, on the Linux side

Runs inside the build container, where `x86_64-w64-mingw32-objdump` is. It reads the PE import table
(`objdump -p | grep 'DLL Name'`) and fails if anything appears outside an allowlist of the eleven system DLLs
in section 2. Matching is **case-insensitive**: mingw emits `WINSPOOL.DRV`, `Secur32.dll` and `comdlg32.dll`
in one binary, and a case-sensitive list would be a trap for whoever extends it.

This is `verify-linux.sh`'s check 1 spelled for PE, and it catches the same class of failure: a
`libwinpthread-1.dll` or `libgcc_s_seh-1.dll` that would make the engine refuse to start on a user's machine,
or an `libssl` that would mean the build had quietly found a cryptographic library it should not have.

There is deliberately **no analogue of `verify-linux.sh`'s check 2**. That check exists because `ldd` alone is
not a portability claim on glibc — the highest imported symbol version is what decides. PE imports carry no
equivalent versioning, and the Windows API surface b3270 uses has been stable since long before any version
LizTerm supports.

### 4.2 Start and TLS, on the Windows side

`shared-verify-tls.sh` runs `b3270.exe --version` on the Windows runner and asserts the reported provider, then
`EngineSmokeTests` starts the same binary through `B3270Session` with `LIZTERM_REQUIRE_ENGINE=1` so it
must run rather than skip. Between them these are the checks plan 3c's `verify-linux-start.sh` and its
`LIZTERM_REQUIRE_ENGINE=1` suite run make, on the only machine that can make them here. The script runs
under `shell: bash`; GitHub's `windows-latest` image provides that through Git Bash, so the one bash gate
shared by all three platforms needs no Windows-specific rewrite.

**`shared-verify-tls.sh` must be generalized.** As written it asserts `*"TLS provider: OpenSSL"*` and would
reject a correct Windows engine. Its own header already claims the failure it catches "is x3270's, not either
platform's"; Windows makes that literally true while showing the assertion was written too narrowly. It gains
an optional second argument naming the expected provider, defaulting to `OpenSSL` so both existing callers are
unchanged, and its remedy text — which currently names only the `build-linux.sh` `LIBS` line and the macOS
static archives — becomes conditional on which provider was expected. Its first arm, which separates "the
engine did not run at all" from "the engine has no TLS", is untouched and matters more here than anywhere
else: on Windows that arm is what a wrong-architecture or truncated artifact will trip.

Weakening the assertion to "any provider that is not `None`" was considered and rejected. It would pass a
Windows binary that had somehow linked OpenSSL, which is exactly the silent substitution these gates exist to
catch.

### 4.3 Proving the gate rejects

Plan 3c established that a gate must be shown to reject, and by the message its own arm prints rather than by
a bare non-zero exit, because the wrapper exits non-zero for a Docker Hub rate limit too. The same discipline
applies here, and the fixture is free: a two-line C file cross-compiled with `-shared-libgcc -pthread`
produces an executable importing `libwinpthread-1.dll`, which is precisely the failure section 4.1 guards. It is
compiled from a here-doc inside that CI step rather than committed as a repository file: it exists only to be
rejected, and a checked-in .c file invites someone to maintain it. It needs no download and no second image,
and it runs in the same container invocation as the positive check —
one step that runs `verify-windows.sh` against the built binary and then against the fixture, asserting each
outcome by its message.

The TLS arm's rejection is not fixtured. Producing a Schannel-less b3270.exe would mean patching upstream, and
the arm being reused is the one plan 3c already proved rejects on two platforms.

## 5. `engine-windows` in `platforms.yml`

A new job on `ubuntu-24.04` — pinned rather than `ubuntu-latest` for the reason the Linux legs are, and with
no matrix, because there is one target.

1. Cache `native/cache` (the source tarball, shared with the other jobs' key discipline) and
   `native/out/win-x64`.
2. Build through `build-windows-docker.sh`, whose last act is the import gate.
3. Run the reject-check against the negative fixture. Like plan 3c's, this runs whether or not the engine came
   from the cache: on a cache hit the build step never executes, so it and the Windows-side start check are all
   that stand between a stale cached binary and a published artifact.
4. Upload `b3270-win-x64-unverified`.

`test-windows` gains `needs: engine-windows`, and before its build step downloads that artifact into
`native/out/win-x64/`, which is exactly where the App csproj's `Exists(...)` condition looks. It then builds,
runs `shared-verify-tls.sh` against the binary, runs the suite with `LIZTERM_REQUIRE_ENGINE=1`, and
re-uploads as `b3270-win-x64`.

Plan 3a's engine-artifact step needed a `chmod +x` because artifact downloads lose the executable bit. That
concern does not exist here: Windows has no executable bit and `B3270Locator` skips that check on Windows.

The cache key for the Windows engine hashes the scripts that feed *this* platform — `*windows*.sh` plus
`fetch-source.sh`, with `shared-*.sh` in it as the other two keys have them — so a Debian bump cannot force a
cold macOS rebuild and new shared machinery cannot be added to one key and forgotten in another.

`timeout-minutes` is set from the first real x64 runs rather than guessed, following plan 3c's practice of
tightening it only once a run has measured the thing being bounded. The spike's 95 seconds cold on arm64 is
the only number in hand and is not an x64 runner.

The workflow's path filter already covers `native/**`, `src/**` and `tests/**`, so it needs no change.

**Accepted consequence:** `test-windows` no longer runs when `engine-windows` fails, so an engine breakage
costs the Windows behavioural signal as well. `ci.yml`'s `test` still runs the full suite on every pull
request, so what is lost is Windows-specific behaviour only, and the coupling is what buys a start check on a
real Windows machine. The alternative — a third job on `windows-latest` that does only the engine proof,
leaving `test-windows` independent — costs a runner and splits the `LIZTERM_REQUIRE_ENGINE=1` suite run away
from the ordinary one, and can be adopted later if the lost signal ever actually bites.

Branch protection is unaffected: `main` requires `test` only, and the platform jobs are path-filtered on pull
requests.

## 6. Repository changes

- `CLAUDE.md`, "The b3270 binary": add the Windows story — the wrapper is the entry point, the target is
  always `win-x64` whatever the builder is, there is no OpenSSL or expat to pin because Schannel and a bundled
  expat make both unnecessary, and `win-arm64` runs the x64 binary under emulation. Add `engine-windows` to
  the CI paragraph and record that `test-windows` now depends on it and republishes its artifact.
- `README.md`: add `win-x64` wherever the built engine targets are named, a section on building it, and
  `engine-windows` in the continuous integration section with one line on what it proves.
- No `.gitignore` change: `native/out`, `native/cache` and `native/build-tmp` are already ignored.
- No C# change is expected. If one proves necessary, it is a finding worth recording in section 9 rather than
  a quiet edit.

## 7. Testing and verification

- `native/build/build-windows-docker.sh` produces a gated `native/out/win-x64/b3270.exe` on Robert's Mac from
  a clean `native/out`.
- `verify-windows.sh` accepts that binary and rejects the `libwinpthread` fixture, by the message its arm
  prints.
- On the PR: `test`, `engine-macos`, `engine-linux` (both legs), `engine-windows` and `test-windows` are
  green; `b3270-win-x64` is downloadable from the run; and the `test-windows` log shows
  `TLS provider: Windows Schannel` and an `EngineSmokeTests` that ran rather than skipped.
- `git status` clean, and `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` reports 0.

## 8. Out of scope

`win-arm64` as a *published* layout (this plan settles what it ships; placing it is 3e's packaging job); a
native arm64 Windows engine, which needs upstream host-detection patches; `osx-x64`, still unassigned to any
plan; `dotnet publish`, bundles, version stamping and releases (3e); the integration lane and its container
(3f); code signing; running the live tests in CI; and applying the negative-fixture idea to
`verify-macos.sh`, which remains worth doing and remains not this plan.

## 9. Deviations from this spec (as-built)

Rulings made in planning and execution, recorded here rather than edited into the sections above, as plan 3c's
section 9 was:

1. **The container's package list gained `curl` and `ca-certificates`, which section 3.3 did not anticipate.**
   The feasibility spike in section 2 extracted the pinned `suite3270-4.5ga6` tarball on the host and mounted
   it into the container it timed; `build-windows.sh` instead calls the unmodified `fetch-source.sh`, which
   delegates to `shared-fetch-tarball.sh` — and that script runs *inside* the container on every real build,
   exactly as it does for Linux. `debian:12-slim` is a minimal image and ships neither package, so the first
   build through the actual wrapper failed at the download step, not at anything Windows-specific. Both went
   into `build-windows-docker.sh`'s `PACKAGES` beside the mingw-w64 toolchain, for the same reason the
   toolchain is a layer of a derived image rather than an `apt-get install` on every invocation: the spike's
   escape from the problem — extracting on the host — could not survive contact with the real entry point,
   whose whole point is that `fetch-source.sh` runs identically wherever it is called from.
2. **The negative fixture is a DLL built on the spot, not the `libwinpthread-1.dll` import section 4.3 names.**
   The spec proposed a two-line C file linked with `-shared-libgcc -pthread` to produce an executable that
   imports `libwinpthread-1.dll`. Execution found that this container's `gcc-mingw-w64-x86-64` is GCC 12
   configured for the **win32** thread model, not **posix** (`x86_64-w64-mingw32-gcc --version` reports
   `12-win32`) — no program this toolchain produces links `libwinpthread-1.dll` at all, whatever flags it is
   given. A fixture built the spec's way would have linked cleanly, imported only system DLLs, passed
   `verify-windows.sh`, and the CI step would have reported a rejection that never actually happened: a guard
   whose failure mode depends on a package default it does not control, caught here before the gate shipped
   rather than after, which is exactly plan 3c's "a guard that cannot fail is not a guard" one level earlier.
   The fixture actually used — and the one `platforms.yml`'s `engine-windows` job builds inline from a
   here-doc, never committed, for the same "it exists only to be rejected" reason plan 3c gives — is two
   files: `dll.c` exports a function with `__declspec(dllexport)` and is compiled to `libfixture.dll` with
   `--out-implib` to produce the import library, and `uses-dll.c` declares the same function
   `__declspec(dllimport)` and calls it, linked against that import library. The resulting `dirty.exe` imports
   `libfixture.dll`, a name that can never appear on the allowlist by construction, and the fixture no longer
   depends on a toolchain default this container happens not to set.
3. **`verify-windows.sh` gained a machine-type check the spec did not call for.** Section 4.1 describes only
   the import-table allowlist. Execution added a first check — `objdump -f` must report `i386:x86-64` — because
   a 32-bit build is a plausible accident rather than a hypothetical one: `i686-w64-mingw32` is a host the same
   `suite3270-4.5ga6` source tree and the same `./configure --host=... --enable-b3270 ...` line accept without
   complaint, and its output would import the *same* eleven system DLLs a correct `win-x64` build does — the
   import allowlist has no way to see the difference between a 32-bit and a 64-bit PE. Left unchecked, a
   transcription slip in `build-windows.sh`'s `HOST` variable would produce a binary that passes every check
   section 4.1 describes and then fails to load in a `win-x64` .NET process. The check runs before the import
   scan, so a wrong-architecture binary is rejected by a message naming the mistake rather than by an opaque
   import failure downstream.
4. **The implementation plan's Task 1 step 1 verification method is defective; the pin it was checking is
   not.** That step asked for `docker pull --platform linux/amd64 ...` and
   `docker pull --platform linux/arm64 ...` against the pinned digest, expecting both to succeed as proof the
   digest is the multi-architecture index rather than one platform's manifest. Both calls instead failed on
   the machine that ran them, each along the lines of `cannot overwrite digest ...`: the local Docker
   image store already held one platform under that reference from an earlier pull, and asking it to store a
   second platform under the *same* local reference is a local image-cache conflict, not a registry fact — a
   machine that had never pulled the image at all would need two full platform pulls just to run the check,
   which is a strange thing for a "does this pin resolve" step to require. The authoritative alternative,
   `docker buildx imagetools inspect debian:12-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171`
   (the same incantation `windows-image.sh`'s own header names), lists `linux/386`, `linux/amd64`,
   `linux/arm/v7`, `linux/arm64/v8`, and `linux/ppc64le` under the pinned index digest straight from the
   registry's manifest list, with no local image-store state involved at all — confirming both the arm64 Mac
   this plan was built on and CI's x64 runner resolve the same digest. The pin recorded in `windows-image.sh`
   needed no change; only the plan's own verification recipe was wrong, and a later plan should reach for
   `imagetools inspect` first rather than two `docker pull --platform` calls that can fail on a healthy pin.
5. **Still pending: whether `actions/download-artifact@v7` resolves.** `test-windows`'s download step was
   written to match this repository's existing `actions/upload-artifact@v7`, on the assumption that GitHub
   ships matching majors for the two actions together. Nothing has exercised that assumption: the branch stays
   unpushed pending the user's separate authorization to publish it (a push and a PR are outward-facing side
   effects execution does not take on its own), so `engine-windows` and `test-windows` have not run once. This
   entry stays open until the first CI run confirms the version or shows it needs correcting — and if it does,
   the correction belongs here too, not as a silent edit to the workflow file.
6. **Still pending: `engine-windows`'s `timeout-minutes`.** It is currently the provisional `20` the
   implementation plan proposed, unmeasured against the runner it actually bounds. The only wall-clock numbers
   in hand are local and on the wrong architecture for the question: section 2's spike measured 95 seconds
   cold on an arm64 Mac, and building the real engine measured 48.9 and 53 seconds warm, also on an arm64 Mac.
   `engine-windows` runs on `ubuntu-24.04`, an x64 runner, and plan 3c's own history is the reason not to
   assume the two behave alike — its `dnf install` alone ran several times slower on an x64 leg than on the
   matching arm64 one in that plan's first cold run. Following plan 3c's practice, `timeout-minutes` should be
   tightened only once a real run has measured the thing it bounds, not guessed down from a spike on different
   hardware; a later task must set it from the first real cold- and warm-cache runs on the actual runner and
   record both numbers here.
7. **Still pending: whether no .NET change is actually needed.** Section 6 predicted this and asked that any
   change that did prove necessary be recorded here rather than made quietly. The case for "none needed" is a
   source reading, not yet an execution: `B3270Locator.cs:20` already picks `b3270.exe` when
   `OperatingSystem.IsWindows()`, `:46` already skips the executable-bit check on that platform, and the App
   csproj's copy item already links by `%(Filename)%(Extension)`, so `native/out/win-x64/b3270.exe` lands at
   `runtimes/win-x64/native/b3270.exe` through the existing rule with no project edit. That reading is sound as
   far as it goes, but nothing has exercised it: the branch stays unpushed pending the user's separate
   authorization, so CI has not run `engine-windows` or `test-windows` once, and `EngineSmokeTests` has never
   started the real `win-x64` binary on an actual Windows machine — the same unverified status as entries 5 and
   6 above, not a settled fact ahead of them. This entry stays open until the first green `test-windows` run
   shows `EngineSmokeTests` running rather than skipping under `LIZTERM_REQUIRE_ENGINE=1`; only then does the
   reading above become the confirmation section 6 asked for, rather than an argument for one.
