# LizTerm Milestone 3, plan 3c: Linux engines

Date: 2026-09-07. Parent spec: `2026-09-03-lizterm-v1-design.md` (section 8.1 names all six targets for v1;
section 8.2 asks for one build script per OS and makes the dependency check the CI gate). Predecessors:
`2026-09-06-lizterm-m3-ci-design.md` (plan 3a, whose `platforms.yml` this plan adds a job to) and
`2026-09-06-lizterm-m3-trust-design.md` (plan 3b, which is why a statically linked engine can verify a
certificate at all). Status: approved in discussion on 2026-09-07; awaiting review of this text.

## 1. Purpose

`b3270` exists for one target, `osx-arm64`, built by `native/build/build-macos.sh` and proven by the
`engine-macos` job. This plan produces `linux-x64` and `linux-arm64` the same way: a build script, a
verification gate that is the build's pass/fail, and a CI job that builds, proves LizTerm can spawn what it
built, and only then uploads the binary.

Milestone 3 after this plan: **3d** Windows engines, **3e** publish and release, **3f** the scheduled
integration lane.

Decisions taken in the brainstorm on 2026-09-07:

- **The glibc floor is 2.28**, which is AlmaLinux 8 / RHEL 8. That is also .NET 10's own floor, so the engine
  never becomes the thing that decides where LizTerm runs. A floor above it — Debian 12's 2.36 is the tempting
  one, because the toolchain is current and the image is small — would ship a binary that cannot start on
  Ubuntu 22.04 (2.35) or RHEL 9 (2.34), both supported for years yet, and a binary that cannot start is exactly
  the failure this plan's gate exists to prevent.
- **OpenSSL comes from pinned 3.5.x LTS source, built inside the container**, not from the base image's
  package. AlmaLinux 8 ships 1.1.1, which has been end-of-life upstream since September 2023, and because the
  engine links statically the distribution's security updates would never reach our binary anyway. Pinning it
  the way `fetch-source.sh` pins x3270 makes every bump a deliberate PR that reruns the gate, and it keeps
  macOS and Linux on the same OpenSSL major.
- **Both architectures land in this plan**, each on its own native runner. Cross-compiling both from one x64
  container was the fallback while arm64 runners looked unavailable on a private repository; that is no longer
  true (section 2), and cross-building trades fiddly autotools host triplets for a binary the job cannot
  execute to test.

## 2. Facts that shape the design

- GitHub made arm64 standard runners available in private repositories on 2026-01-29. They are free-tier
  eligible and carry two vCPUs on a private repository against four on a public one. The label is
  `ubuntu-24.04-arm`; there is no `ubuntu-latest-arm`.
- .NET 10 supports Ubuntu 18.04 and later and RHEL 8 and later — glibc 2.27 and 2.28. Building the engine at
  2.28 puts it at or below the floor of everything else LizTerm ships.
- AlmaLinux 8 is glibc 2.28 on both `x86_64` and `aarch64`, publishes both images, and is supported until 2029.
- OpenSSL 3.5 is the current LTS, released 2025-04-08 and supported until 2030-04-08. The next LTS is 4.2 in
  April 2027, which is when this pin next wants a look.
- `build-macos.sh` has the shape this plan copies: resolve the RID from the host architecture, fetch the pinned
  source, stage a directory holding only static archives so the linker cannot reach a shared object, configure
  with `--enable-b3270` and every other component disabled, make, copy to `native/out/<rid>/b3270`, then run
  the verification script as the last step. `fetch-source.sh` echoes the extracted source directory on stdout
  and caches the tarball under `native/cache`; a Linux fetcher follows the same contract.
- The App and integration test csproj files copy `native/out/$(NETCoreSdkRuntimeIdentifier)/b3270*` into
  `runtimes/<rid>/native/`, and `B3270Locator` looks for it at `runtimes/<RuntimeInformation.RuntimeIdentifier>/native/`.
  On these runners both resolve to `linux-x64` and `linux-arm64`, so **no .NET source changes at all**. Alpine
  is a different RID (`linux-musl-x64`) and a glibc binary will not run there; musl is out of scope.
- Plan 3b writes an explicit `caFile` on every connect. That matters here more than anywhere: a statically
  linked OpenSSL carries a compiled-in trust directory that, for these binaries, is a path inside a build
  container that exists on no user's machine. Without 3b this plan would ship a client that can verify nothing.
- glibc 2.34 merged `libdl`, `libpthread` and `librt` into `libc`. A binary built against 2.28 references them
  as separate sonames and still resolves on newer systems through the stub libraries left behind for exactly
  that reason. Compatibility runs old-to-new only, which is the whole argument for building on the floor.
- On glibc 2.34 and later, `__libc_start_main` is versioned `GLIBC_2.34`. Any trivial C program compiled on the
  runner therefore imports a symbol above our floor while linking nothing but `libc` — a ready-made negative
  fixture for section 4's floor check (`/usr/bin/curl` inside the container is the fixture for the other arm,
  since it links OpenSSL and zlib).
- The `ubuntu-24.04` runner images ship Docker and gcc. The arm64 image is the same family; the first run
  confirms it.
- `SystemTrustAnchorsTests` asserts the OS store yields at least five certificates, deliberately with no skip
  (plan 3b, deviation 5). A bare container with no `ca-certificates` would fail it. The suite therefore runs on
  the host runner, which has a root store, and never inside the build container.

## 3. The build scripts

Four new scripts under `native/build`, all `set -euo pipefail`, matching the existing ones in shape and size;
the two verification scripts are section 4. A fifth file, `native/build/linux-image.sh`, is not a script but a
sourced one-liner holding the pinned base image (section 3.3).

### 3.1 `fetch-openssl.sh`

`fetch-openssl.sh <dest-dir>`: the newest OpenSSL 3.5.x patch as of implementation, and its SHA-256, as shell
constants, downloaded from
`https://github.com/openssl/openssl/releases/download/openssl-<version>/openssl-<version>.tar.gz` into
`native/cache`, checksummed, extracted into the destination, extracted path echoed on stdout. Deliberately a
copy of `fetch-source.sh`'s structure rather than a generalisation of it: two ten-line scripts that each read
top to bottom beat one parameterised fetcher, and the pin is the point — it should be visible in the file that
owns it. Bumping either version is a one-line edit that invalidates the CI cache on its own, because the cache
key hashes the scripts.

`shasum` is macOS-flavoured; `sha256sum` is what AlmaLinux has. The Linux fetcher uses `sha256sum`.

### 3.2 `build-linux.sh`

Runs **inside** the container, assuming its toolchain the way `build-macos.sh` assumes Xcode command line tools
and Homebrew. Steps:

1. Map `uname -m` to the RID: `x86_64` → `linux-x64`, `aarch64` → `linux-arm64`, anything else is an error.
2. Fetch OpenSSL, then build it static into a staging prefix:
   `./config no-shared no-tests no-docs --prefix=$STAGE --libdir=lib`, `make`, `make install_sw`. `--libdir=lib`
   is not cosmetic: OpenSSL installs to `lib64` on x86_64 by default, and pinning it to `lib` makes the staged
   layout identical on both architectures and matches what x3270's `--with-openssl` expects. `no-shared` means
   the staging prefix holds no `.so` at all, so the macOS script's "copy only the archives" trick is unnecessary
   here — there is nothing else to pick up.
3. Fetch x3270, configure with the macOS script's flag set (`--enable-b3270`, everything else disabled) plus
   `--with-openssl="$STAGE"`, and make. Static libcrypto wants `-ldl -pthread` at link time; if x3270's
   configure does not supply them, `LIBS` on the configure line does. Whether that is needed is discovery work
   for the plan's first task, not a decision for this spec.
4. Copy to `native/out/<rid>/b3270`, `chmod +x`, and run `verify-linux.sh` on it as the final step, so the gate
   is part of the build rather than a thing CI remembers to call.

`configure` and `make` output goes to `$BUILD/{configure,make}.log` as on macOS, which is why `platforms.yml`
already has a failure step that dumps those logs.

### 3.3 `build-linux-docker.sh`

The wrapper, and the only script CI or a developer calls directly to build. It requires Docker on the host and
nothing else:

- Sources `native/build/linux-image.sh`, which holds the base image **pinned by digest**
  (`almalinux:8@sha256:...`) and nothing else. The digest is where the glibc floor actually comes from, and two
  scripts need it — this wrapper and `verify-linux-start.sh` — so it lives in one file they both read rather
  than in both of them, where the two copies could drift and quietly stop testing the same floor. It is a `.sh`
  under `native/build`, so `hashFiles('native/build/*.sh')` covers it: changing the image forces a rebuild and a
  fresh gate run.
- `docker run --rm -v <repo>:/src -w /src <image>` a small inline preamble that `dnf install`s the toolchain
  (`gcc make perl-core diffutils tar binutils` — `binutils` for the `readelf` the gate needs; the plan's first
  task pins the final list) and then runs the requested command.
- With no arguments it runs `native/build/build-linux.sh`, then `verify-linux-start.sh` on the result, so a
  developer who runs the wrapper gets the whole gate. With arguments it runs those instead, inside the same
  image — which is how section 4's negative fixture invokes the gate without a second wrapper.
- Ends by chowning `native/out`, `native/build-tmp` and `native/cache` back to the calling user's uid/gid from
  inside the container. On a Linux runner the container writes as root, and root-owned files break the
  `actions/cache` save and the artifact upload that follow. On Docker Desktop the ownership is virtualised and
  the chown is a no-op, so the same script serves Robert's Mac (native aarch64; x64 through emulation if he
  ever wants it locally).

## 4. The gate: `verify-linux.sh`

`verify-macos.sh` is one check — no dynamic dependency outside the system paths. Its Linux counterpart needs
three, because on Linux `ldd` alone is not a portability claim: a binary built on Ubuntu 24.04 links only
system libraries too and passes it while being unable to start on RHEL 9.

1. **Dependency allowlist.** Every `ldd` entry must be one of `linux-vdso`, `libc`, `libm`, `libdl`,
   `libpthread`, `librt`, `libresolv`, `libgcc_s`, `ld-linux-*`. A `libssl.so`, `libcrypto.so` or `libtinfo.so`
   fails the build. This is `verify-macos.sh`'s check, spelled for glibc.
2. **Symbol floor.** The highest `GLIBC_2.x` version the binary imports must be `<= 2.28`, read from
   `readelf --dyn-syms` (or `objdump -T`) and compared with `sort -V`. This is the check that actually catches
   base-image drift, and the one that fails if someone builds outside the container.
3. **It starts on the floor.** `b3270 --version` inside a **bare** container from the same pinned image, with
   no build tools installed. The binary starting on the floor OS with nothing else present is the portability
   claim itself, and it costs one `docker run`. This cannot live in `verify-linux.sh`, which itself runs inside
   a container and cannot start another, so it is its own host-side script, `verify-linux-start.sh <binary>`,
   sourcing the same pinned digest (section 3.3). Two callers: the wrapper runs it after a build, and the
   workflow runs it directly, which is what covers the cache-hit path where the wrapper never executes.

Plan 3b's deviation 5 recorded the lesson that a guard which cannot fail is not a guard: mutating
`SystemTrustAnchors.ReadStore` to return nothing left every test passing. So the job also proves this gate
rejects bad binaries, one fixture per arm:

- `native/build/build-linux-docker.sh native/build/verify-linux.sh /usr/bin/curl` — curl inside the container
  links OpenSSL and zlib, so check 1 must reject it.
- A trivial C file compiled on the host runner (`int main(void){return 0;}`), which links only `libc` and so
  passes check 1, but imports `__libc_start_main@GLIBC_2.34` and so check 2 must reject it.

Both steps assert a non-zero exit. If either fixture turns out not to behave as described, the fix is a
different fixture, not a dropped check.

## 5. `engine-linux` in `platforms.yml`

One new job, a two-leg matrix, mirroring `engine-macos` step for step:

```
strategy:
  fail-fast: false
  matrix:
    include:
      - { runner: ubuntu-24.04,     rid: linux-x64 }
      - { runner: ubuntu-24.04-arm, rid: linux-arm64 }
```

`ubuntu-24.04` rather than `ubuntu-latest`: the two legs must differ only in architecture, and `ubuntu-latest`
rolling to 26.04 while the arm label stays at 24.04 would quietly make them incomparable. `fail-fast: false`
because one architecture failing is information about that architecture, and cancelling the other leg throws it
away.

Steps:

1. `actions/checkout`.
2. `actions/cache` on `native/cache`, key `sources-${{ hashFiles('native/build/fetch-*.sh') }}` — now two
   tarballs, and the key follows both fetchers. Deliberately not keyed on the architecture: source tarballs are
   arch-independent, so the two legs share one entry. Both legs missing at once means both download and both
   try to save; one loses the race and logs a warning, which is harmless.
3. `actions/cache` on `native/out/${{ matrix.rid }}`, key
   `b3270-${{ matrix.rid }}-${{ hashFiles('native/build/*.sh') }}`, as macOS does: keyed on every build script,
   so any change to how the engine is built forces a real build and a fresh gate run.
4. `native/build/build-linux-docker.sh`, skipped on a cache hit. The gate runs inside it.
5. `native/build/verify-linux-start.sh` on the built binary, then the two negative-fixture steps. All three run
   **whether or not** the engine came from the cache: on a cache hit the wrapper never executes, so this is the
   only thing standing between a stale cached binary and an artifact upload. The negative fixtures pay the
   wrapper's `dnf install` preamble each time, about half a minute, which is the price of a gate that is proven
   rather than assumed.
6. `actions/setup-dotnet` from `global.json`, `dotnet build LizTerm.slnx --configuration Release -warnaserror`,
   then the suite with `LIZTERM_REQUIRE_ENGINE: "1"` in the job environment, so `EngineSmokeTests` must run
   rather than skip. `native/out/<rid>` exists by now, so the csproj copy rules place the fresh binary in the
   test output with no workflow-side copying.
7. `actions/upload-artifact` of `native/out/<rid>/b3270` as `b3270-<rid>`, after the tests, so a binary that
   links cleanly but cannot be spawned is never published — the rule `engine-macos` already follows.
8. `test-results-linux-<arch>` on `failure() || cancelled()`, with the `.trx`, `*.dmp` and `*Sequence*.xml`
   paths the other jobs use.

`timeout-minutes: 45`. The arm64 private-repository runner has two vCPUs and this job builds OpenSSL and x3270
from source on a cold cache; 30 (the macOS number, on a four-core runner with Homebrew's prebuilt OpenSSL) is
too tight to trust. The first run tells us the real number.

The workflow's `paths` filter already covers `native/**`, `src/**` and `tests/**`, so this job is exercised on
its own PR with no trigger change. Adding scripts under `native/build` changes `hashFiles('native/build/*.sh')`
and so invalidates the macOS engine cache once — one rebuild, no behaviour change.

## 6. Repository changes

- `CLAUDE.md`, "The b3270 binary": it currently names `build-macos.sh` alone. Add the Linux story — the wrapper
  is the entry point, the container and its digest are where the floor comes from, and `LIZTERM_B3270_PATH`
  remains the development override. Add `engine-linux` to the CI paragraph beside `engine-macos` and
  `test-windows`.
- `README.md`: the platform table or equivalent gains Linux x64 and arm64 as built targets, and the CI section
  gains the new job.
- No `.gitignore` change: `native/out`, `native/cache` and `native/build-tmp` are already ignored.

## 7. Testing and verification

There is no new .NET code, so the verification is the scripts and the CI run:

1. `build-linux-docker.sh` locally on Robert's Mac, which produces `linux-arm64` natively under Docker Desktop.
   The gate must pass, and `native/out/linux-arm64/b3270` must exist.
2. Both negative fixtures locally: each must exit non-zero, and the failure message must name which check
   rejected it.
3. `dotnet test tests/LizTerm.Integration.Tests` locally on the Mac afterwards is **not** a proof of this plan —
   the Mac's RID is `osx-arm64` and it will pick up the macOS engine. The Linux engine is proven only in CI.
4. Workflow YAML validated locally (`actionlint` if present, otherwise a YAML parse) before pushing, because a
   syntax error surfaces only when GitHub tries to run the file.
5. Push, open the PR, and watch both legs. Done when `test`, `engine-macos`, `test-windows`,
   `engine-linux (linux-x64)` and `engine-linux (linux-arm64)` are green, both `b3270-linux-*` artifacts are
   downloadable, and the negative-fixture steps show as passing (that is, having rejected their fixtures).

The claim this plan's first CI run either proves or refutes, stated plainly so a refutation is a finding rather
than a surprise: **no .NET source change is needed** for the Linux RIDs (section 2). If the copy rule or the
locator turns out to need one, that is part of this plan.

## 8. Out of scope

Windows engines (3d); `dotnet publish`, bundles, version stamping, releases (3e); the integration lane and its
container (3f); `osx-x64`; Alpine and any other musl target; a Dockerfile or a registry-hosted build image (the
inline `dnf install` in the wrapper is cheaper than an image to publish and version); code signing; running the
live tests in CI; and applying the section 4 negative-fixture idea to `verify-macos.sh`, which is worth doing
and is not this plan.

## 9. Deviations from this spec (as-built)

Filled in during execution.
