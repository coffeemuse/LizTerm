# LizTerm Milestone 3, plan 3c: Linux engines — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `b3270` for `linux-x64` and `linux-arm64` on a glibc 2.28 floor, gate it so a binary that cannot start on a supported Linux never ships, and prove both in CI.

**Architecture:** A digest-pinned `almalinux:8` container is the build environment; a host-side wrapper (`build-linux-docker.sh`) is the only thing anyone calls. Inside it, OpenSSL 3.5 LTS is built from pinned source with `no-shared`, then x3270's `b3270` is configured against that staging prefix. The gate is three checks — a dependency allowlist, a maximum imported glibc symbol version, and actually starting the binary in a bare container from the same image — and CI proves the gate rejects bad binaries as well as accepting good ones.

**Tech Stack:** Bash, Docker, GNU binutils (`readelf`), autotools (x3270 4.5ga6), OpenSSL 3.5.x, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-07-lizterm-m3c-linux-engines-design.md`

## Global Constraints

- **glibc floor is 2.28.** AlmaLinux 8, which is also .NET 10's own floor. Every produced binary must import no glibc symbol above `GLIBC_2.28`.
- **The base image is pinned by digest**, in `native/build/linux-image.sh` and nowhere else. Two scripts read it; neither hard-codes it.
- **OpenSSL comes from pinned source**, the newest 3.5.x patch, built `no-shared`. Never the base image's package (1.1.1, end-of-life since September 2023).
- **RIDs are `linux-x64` (x86_64) and `linux-arm64` (aarch64).** Output goes to `native/out/<rid>/b3270`, which is what the App and integration csproj copy rules already read.
- **No .NET source changes are expected.** If one turns out to be needed, it belongs to this plan — see Task 6, step 6.
- **Every script starts `set -euo pipefail`** and follows the existing `native/build` scripts in shape and size. `linux-image.sh` is the exception: it is sourced, not executed, and sets one variable.
- **Commit messages follow house style**: an imperative sentence subject, no `feat:`/`fix:` prefixes, and a `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` trailer.
- **CI runners are `ubuntu-24.04` and `ubuntu-24.04-arm`**, never `ubuntu-latest`, so the two matrix legs differ only in architecture.

---

### Task 1: Pin the base image and prove the pin covers both architectures

**Files:**
- Create: `native/build/linux-image.sh`

**Interfaces:**
- Consumes: nothing.
- Produces: `LIZTERM_LINUX_IMAGE`, a shell variable holding the pinned image reference (`almalinux:8@sha256:<digest>`), sourced by `build-linux-docker.sh` (Task 3) and `verify-linux-start.sh` (Task 6).

- [ ] **Step 1: Resolve the multi-architecture index digest**

The digest must be the *manifest index* digest, not one architecture's manifest, or the arm64 leg will fail to pull.

```bash
docker buildx imagetools inspect almalinux:8 --format '{{.Manifest.Digest}}'
```

If `buildx` is unavailable, the fallback is `docker pull almalinux:8 && docker inspect --format='{{index .RepoDigests 0}}' almalinux:8`. Record the digest; Step 2 writes it into the file.

- [ ] **Step 2: Write the file**

```bash
#!/usr/bin/env bash
# The pinned Linux build base, sourced (not executed) by build-linux-docker.sh and verify-linux-start.sh.
#
# AlmaLinux 8 is glibc 2.28, which is also .NET 10's own floor, so the engine is never the thing that decides
# where LizTerm can run. Pinned by digest because the digest IS the floor: a floating tag could raise it
# silently, and the first anyone would know is a user's binary refusing to start. The digest lives here alone
# so the two scripts that need it cannot drift apart and quietly stop testing the same floor.
#
# It is a *.sh under native/build, so hashFiles('native/build/*.sh') covers it: changing the image invalidates
# the CI engine cache and forces a real build and a fresh gate run.
LIZTERM_LINUX_IMAGE=almalinux:8@sha256:<digest from step 1>
```

- [ ] **Step 3: Prove the pin resolves on both architectures**

This is the test for this task: an architecture-specific digest passes the first command and fails the second.

```bash
. native/build/linux-image.sh
docker run --rm --platform linux/amd64 "$LIZTERM_LINUX_IMAGE" uname -m
docker run --rm --platform linux/arm64 "$LIZTERM_LINUX_IMAGE" uname -m
```

Expected: `x86_64` then `aarch64`. On an Apple Silicon Mac the amd64 run goes through emulation and is slow but works.

- [ ] **Step 4: Prove the floor is what we think it is**

```bash
. native/build/linux-image.sh
docker run --rm "$LIZTERM_LINUX_IMAGE" ldd --version | head -1
```

Expected: a version reading `2.28`. If it does not, stop — the whole plan's floor claim is wrong and the spec needs revisiting, not the script.

- [ ] **Step 5: Commit**

```bash
git add native/build/linux-image.sh
git commit -m "Pin the Linux build base by digest" \
  -m "AlmaLinux 8 is glibc 2.28, .NET 10's own floor. The digest is the floor, so it is pinned rather than tracked by tag, and it lives in one sourced file because the wrapper and the start check must not drift onto different images." \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Fetch pinned OpenSSL source, and make the x3270 fetcher portable

**Files:**
- Create: `native/build/fetch-openssl.sh`
- Modify: `native/build/fetch-source.sh` (the checksum line only)

**Interfaces:**
- Consumes: nothing.
- Produces: `fetch-openssl.sh <dest-dir>` — downloads and verifies the pinned tarball into `native/cache`, extracts it into `<dest-dir>`, and echoes the extracted source directory on stdout. Same contract as the existing `fetch-source.sh`. Task 4 calls both.

- [ ] **Step 1: Find the pinned version and its checksum**

Take the newest 3.5.x patch (3.5 is the current LTS, supported to 2030-04-08):

```bash
curl -fsSL https://api.github.com/repos/openssl/openssl/releases | grep -o '"tag_name": "openssl-3\.5\.[0-9]*"' | head -3
```

Then download it once and take its SHA-256, cross-checking against the `.sha256` asset published beside the tarball:

```bash
V=<chosen version>
curl -fsSL -o /tmp/openssl.tar.gz "https://github.com/openssl/openssl/releases/download/openssl-$V/openssl-$V.tar.gz"
shasum -a 256 /tmp/openssl.tar.gz
curl -fsSL "https://github.com/openssl/openssl/releases/download/openssl-$V/openssl-$V.tar.gz.sha256"
```

Expected: the two hashes are identical. If they differ, stop — do not pin a tarball you cannot corroborate.

- [ ] **Step 2: Write `native/build/fetch-openssl.sh`**

```bash
#!/usr/bin/env bash
# Downloads and verifies the pinned OpenSSL source tarball, extracts it into $1.
# Deliberately a sibling of fetch-source.sh rather than a generalisation of it: the pin is the point, and it
# should be visible in the file that owns it. Bumping the version here is a one-line edit that invalidates the
# CI caches on its own, because their keys hash these scripts.
set -euo pipefail
DEST=${1:?usage: fetch-openssl.sh <dest-dir>}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=<version from step 1>
SHA256=<sha256 from step 1>
URL="https://github.com/openssl/openssl/releases/download/openssl-$VERSION/openssl-$VERSION.tar.gz"
CACHE="$ROOT/native/cache"
TGZ="$CACHE/openssl-$VERSION.tar.gz"
# This script runs inside the build container, where shasum (a perl script) may be absent, and on macOS, where
# sha256sum is. Take whichever is there.
checksum() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi; }
mkdir -p "$CACHE"
if [ ! -f "$TGZ" ]; then
  echo "Downloading $URL" >&2
  curl -fsSL -o "$TGZ" "$URL"
fi
echo "$SHA256  $TGZ" | checksum -c - >&2
rm -rf "$DEST"
mkdir -p "$DEST"
tar xzf "$TGZ" -C "$DEST"
echo "$DEST/openssl-$VERSION"
```

- [ ] **Step 3: Make `fetch-source.sh` portable the same way**

`build-linux.sh` calls it inside the container, where `shasum` may not exist. Add the same helper immediately above the `mkdir -p "$CACHE"` line:

```bash
# Runs inside the Linux build container as well as on macOS; take whichever checksum tool is present.
checksum() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi; }
```

and change the verification line from `| shasum -a 256 -c - >&2` to `| checksum -c - >&2`.

- [ ] **Step 4: Prove both fetchers work, and that the checksum is load-bearing**

```bash
chmod +x native/build/fetch-openssl.sh
rm -rf native/build-tmp/fetchtest
native/build/fetch-openssl.sh native/build-tmp/fetchtest
native/build/fetch-source.sh native/build-tmp/fetchtest-x3270
```

Expected: each prints its extracted directory as the last line, and that directory exists and holds a `Configure` (OpenSSL) or a `configure` (x3270).

Now prove the guard is real — corrupt the cached tarball and watch it fail:

```bash
cp native/cache/openssl-*.tar.gz /tmp/openssl-good.tgz
echo corrupt >> native/cache/openssl-*.tar.gz
native/build/fetch-openssl.sh native/build-tmp/fetchtest; echo "exit=$?"
cp /tmp/openssl-good.tgz native/cache/openssl-<version>.tar.gz
```

Expected: a `FAILED` checksum line and a non-zero exit, then the restore puts the good tarball back.

- [ ] **Step 5: Commit**

```bash
git add native/build/fetch-openssl.sh native/build/fetch-source.sh
git commit -m "Fetch a pinned OpenSSL 3.5 LTS source tarball" \
  -m "The Linux engine links OpenSSL statically, so the distribution's security updates would never reach it and the base image's end-of-life 1.1.1 is not an option. Pinned like the x3270 tarball, so a bump is a deliberate PR that reruns the gate. fetch-source.sh gains the same portable checksum helper because it now also runs inside the Linux container, where shasum may be absent." \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: The container wrapper

**Files:**
- Create: `native/build/build-linux-docker.sh`

**Interfaces:**
- Consumes: `LIZTERM_LINUX_IMAGE` (Task 1).
- Produces: `build-linux-docker.sh [command...]` — with no arguments, runs `native/build/build-linux.sh` inside the pinned container (Task 5 adds the start check to this path); with arguments, runs that command inside the same container instead. Task 5's negative fixtures and Task 6's workflow both call the argument form.

- [ ] **Step 1: Write the wrapper**

```bash
#!/usr/bin/env bash
# Builds b3270 for Linux inside the pinned container. This is the only script CI or a developer calls; it needs
# Docker on the host and nothing else.
#
# With no arguments it runs the build. With arguments it runs those inside the same image instead, which is how
# the gate's negative fixtures are checked without a second wrapper.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
. "$(dirname "$0")/linux-image.sh"

# binutils is for the readelf the gate needs; findutils and diffutils are configure's, perl-core is OpenSSL's.
PACKAGES="gcc make perl-core diffutils findutils tar binutils"

if [ "$#" -eq 0 ]; then
  COMMAND="native/build/build-linux.sh"
else
  COMMAND=$(printf '%q ' "$@")
fi

# The container writes as root. Root-owned files under native/ break the actions/cache save and the artifact
# upload that follow it in CI, and a later local build. The trap covers the failure paths too, so a broken
# build does not leave a tree only root can rebuild in. On Docker Desktop the bind mount is already owned by
# the calling user and the chown is a harmless no-op.
HOST_UID=$(id -u)
HOST_GID=$(id -g)

docker run --rm -v "$ROOT:/src" -w /src "$LIZTERM_LINUX_IMAGE" bash -c "
set -euo pipefail
trap 'chown -R $HOST_UID:$HOST_GID native/out native/build-tmp native/cache 2>/dev/null || true' EXIT
dnf install -y $PACKAGES > /tmp/dnf.log 2>&1 || { cat /tmp/dnf.log >&2; exit 1; }
$COMMAND
"
```

- [ ] **Step 2: Prove the wrapper runs a command in the right place**

```bash
chmod +x native/build/build-linux-docker.sh
native/build/build-linux-docker.sh uname -m
native/build/build-linux-docker.sh ldd --version | head -1
```

Expected: `aarch64` on an Apple Silicon Mac (`x86_64` on an Intel one or a CI x64 runner), then a glibc `2.28` line. The first run pulls the image and installs the packages; later runs reinstall the packages each time, about half a minute, which is the accepted cost of not maintaining a published build image.

- [ ] **Step 3: Prove the ownership trap works on the failure path**

```bash
native/build/build-linux-docker.sh bash -c 'mkdir -p native/build-tmp/owner && touch native/build-tmp/owner/f && exit 1'; echo "exit=$?"
ls -l native/build-tmp/owner/f
rm -rf native/build-tmp/owner
```

Expected: a non-zero exit, and the file owned by you rather than root — on a Linux host that is the whole point of the trap; on Docker Desktop it is owned by you regardless, which is why this check must also run on the CI leg (Task 6, where a root-owned tree would break the cache save).

- [ ] **Step 4: Commit**

```bash
git add native/build/build-linux-docker.sh
git commit -m "Run the Linux engine build inside the pinned container" \
  -m "The wrapper is the only entry point: it holds the bind mount, the package preamble, and the chown that keeps a root-owned tree from breaking the cache save and the artifact upload behind it. Passing a command through it is how the gate's own negative fixtures get checked." \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: The gate, before the thing it gates

**Files:**
- Create: `native/build/verify-linux.sh`

**Interfaces:**
- Consumes: `build-linux-docker.sh` (Task 3) to run it.
- Produces: `verify-linux.sh <binary>` — exits 0 and prints the binary's dependencies and version if it links only the glibc runtime and imports no glibc symbol above 2.28; exits non-zero with a message naming the failed check otherwise. Task 5's `build-linux.sh` calls it as its final step.

This task is written before the build script on purpose: the gate is the test, and a gate whose failure path has never been executed is not a gate — the lesson plan 3b recorded in its deviation 5, where mutating `SystemTrustAnchors.ReadStore` to return nothing left every test passing.

- [ ] **Step 1: Write the gate**

```bash
#!/usr/bin/env bash
# Fails if the binary links anything outside the glibc runtime, or if it imports a glibc symbol newer than the
# floor. Runs INSIDE the build container (it needs that container's ldd and readelf).
set -euo pipefail
BIN=${1:?usage: verify-linux.sh <binary>}
FLOOR=2.28

# 1. Every dynamic dependency must be part of the glibc runtime, present on any system we support. This is
# verify-macos.sh's check, spelled for glibc: an libssl.so here would mean the engine picked up a shared
# OpenSSL and will not start on a machine without that exact one.
ALLOWED='^(linux-vdso|libc|libm|libdl|libpthread|librt|libresolv|libgcc_s|ld-linux.*)\.so'
BAD=$(ldd "$BIN" | awk '{print $1}' | sed 's|.*/||' | grep -v -E "$ALLOWED" || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN has dynamic dependencies outside the glibc runtime:" >&2
  echo "$BAD" >&2
  exit 1
fi

# 2. ldd alone is not a portability claim: a binary built on Ubuntu 24.04 links only these too, and still
# cannot start on RHEL 9. The highest glibc symbol version it imports is what actually decides.
# The || true is load-bearing: grep exits 1 when it matches nothing, and under pipefail that would abort
# the script here instead of reaching the empty check below, which is the branch with the useful message.
HIGHEST=$(readelf --dyn-syms --wide "$BIN" | grep -o 'GLIBC_[0-9][0-9.]*' | sed 's/GLIBC_//' | sort -V | tail -1 || true)
if [ -z "$HIGHEST" ]; then
  echo "ERROR: $BIN imports no versioned glibc symbols. Is it dynamically linked against glibc at all?" >&2
  exit 1
fi
if [ "$(printf '%s\n%s\n' "$FLOOR" "$HIGHEST" | sort -V | tail -1)" != "$FLOOR" ]; then
  echo "ERROR: $BIN needs glibc $HIGHEST, above the $FLOOR floor." >&2
  echo "Was it built outside the pinned container? See native/build/linux-image.sh." >&2
  exit 1
fi

echo "OK: $BIN links only the glibc runtime and needs no more than glibc $HIGHEST (floor $FLOOR)"
ldd "$BIN"
# sed, not head: head closes the pipe after three lines, GNU coreutils SIGPIPEs the writer for it, and under
# pipefail that 141 becomes this script's exit status — a passing binary reported as a failed gate.
"$BIN" --version | sed -n '1,3p'
```

- [ ] **Step 2: Prove the allowlist arm rejects a binary that links more**

`/usr/bin/bash` is present in every one of these containers by definition and links `libtinfo.so.6`, which is not in the allowlist.

```bash
chmod +x native/build/verify-linux.sh
native/build/build-linux-docker.sh native/build/verify-linux.sh /usr/bin/bash; echo "exit=$?"
```

Expected: `ERROR: ... has dynamic dependencies outside the glibc runtime:` naming `libtinfo.so.6`, and `exit=1`. If this container's bash happens to link only glibc, use `/usr/bin/curl` instead and note the substitution for Task 7's deviation list.

- [ ] **Step 3: Prove the floor arm rejects a binary built on a newer glibc**

Debian 12 is glibc 2.36, and its `/bin/true` links only the glibc runtime — so it passes check 1 and can only be rejected by check 2. Copying a prebuilt binary out beats compiling one: no toolchain to install, and it behaves identically on your Mac and on both CI legs.

```bash
mkdir -p native/build-tmp
docker run --rm -v "$PWD/native/build-tmp:/out" debian:12-slim cp /bin/true /out/newglibc-fixture
native/build/build-linux-docker.sh native/build/verify-linux.sh native/build-tmp/newglibc-fixture; echo "exit=$?"
```

Expected: `ERROR: ... needs glibc 2.34, above the 2.28 floor.` and `exit=1` — 2.34 because that is where `__libc_start_main` is versioned; any number above 2.28 is the pass condition here, so 2.36 is equally fine. Note that check 1 did *not* fire — that is what makes this fixture worth having.

- [ ] **Step 4: Prove the gate accepts a binary that is actually fine**

There is no b3270 yet, so use the floor image's own `/usr/bin/true`: built on glibc 2.28, links only the glibc runtime.

```bash
native/build/build-linux-docker.sh native/build/verify-linux.sh /usr/bin/true; echo "exit=$?"
```

Expected: an `OK:` line and `exit=0`. (`/usr/bin/true --version` prints coreutils' version, which is all the last line of the script asks of it.)

- [ ] **Step 5: Commit**

```bash
git add native/build/verify-linux.sh
git commit -m "Gate the Linux engine on its dependencies and its glibc floor" \
  -m "ldd alone is not a portability claim on Linux: a binary built on Ubuntu 24.04 links only system libraries too and still cannot start on RHEL 9. So the allowlist is joined by the highest glibc symbol version the binary imports, which is the check that catches a build that escaped the pinned container. Both arms are proven to reject before anything is built for them to accept." \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Build the engine, and prove it starts on a bare floor system

**Files:**
- Create: `native/build/build-linux.sh`
- Create: `native/build/verify-linux-start.sh`
- Modify: `native/build/build-linux-docker.sh` (the no-argument path calls the start check)

**Interfaces:**
- Consumes: `fetch-openssl.sh` and `fetch-source.sh` (Task 2), `verify-linux.sh` (Task 4), `LIZTERM_LINUX_IMAGE` (Task 1).
- Produces: `native/out/linux-x64/b3270` or `native/out/linux-arm64/b3270`, which the App and integration csproj copy rules pick up unchanged; and `verify-linux-start.sh <binary>`, which Task 6's workflow calls directly on the cache-hit path.

- [ ] **Step 1: Write `native/build/build-linux.sh`**

```bash
#!/usr/bin/env bash
# Builds b3270 for the container's architecture against a statically built OpenSSL.
# Runs INSIDE the pinned build container (build-linux-docker.sh); assumes that container's toolchain, the way
# build-macos.sh assumes Xcode command line tools and Homebrew.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
ARCH=$(uname -m)
case "$ARCH" in
  x86_64)  RID=linux-x64 ;;
  aarch64) RID=linux-arm64 ;;
  *) echo "unsupported arch $ARCH" >&2; exit 1 ;;
esac
BUILD="$ROOT/native/build-tmp/$RID"
mkdir -p "$BUILD"
JOBS=$(nproc)

# 1. OpenSSL, static only. --libdir=lib is not cosmetic: OpenSSL installs to lib64 on x86_64 by default, and
# pinning it makes the staged layout identical on both architectures and matches what x3270 expects. Because
# no-shared leaves no .so in the prefix at all, the macOS script's "stage only the archives" trick is
# unnecessary here — there is nothing else for the linker to find.
STAGE="$BUILD/openssl-static"
if [ ! -f "$STAGE/lib/libssl.a" ]; then
  SSL_SRC=$("$ROOT/native/build/fetch-openssl.sh" "$BUILD/openssl-src")
  rm -rf "$STAGE"
  ( cd "$SSL_SRC" \
    && ./config no-shared no-tests no-docs --prefix="$STAGE" --libdir=lib \
    && make -j"$JOBS" \
    && make install_sw ) > "$BUILD/openssl.log" 2>&1
fi

# 2. b3270 against it, with the macOS script's component flags.
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
cd "$SRC"
./configure --enable-b3270 \
  --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 \
  --disable-pr3287 --disable-x3270if --disable-mitm --disable-playback \
  --with-openssl="$STAGE" LIBS="-ldl -pthread" > "$BUILD/configure.log" 2>&1
make -j"$JOBS" > "$BUILD/make.log" 2>&1
# GNU find, so -perm -u+x rather than the macOS script's BSD -perm +111. sed rather than head for the same
# SIGPIPE-under-pipefail reason as verify-linux.sh.
BIN=$(find obj -type f -name b3270 -perm -u+x | sort | sed -n 1p)
OUT="$ROOT/native/out/$RID"
mkdir -p "$OUT"
cp "$BIN" "$OUT/b3270"
chmod +x "$OUT/b3270"
"$ROOT/native/build/verify-linux.sh" "$OUT/b3270"
```

- [ ] **Step 2: Run the build**

```bash
chmod +x native/build/build-linux.sh
native/build/build-linux-docker.sh
```

Expected: several minutes (OpenSSL dominates), ending in the gate's `OK:` line, an `ldd` listing with no OpenSSL in it, and `b3270 --version` reporting 4.5.

This is the plan's discovery step. Two failures are anticipated, and neither is a reason to change the design:

- **A missing package.** `configure.log` or `openssl.log` names it; add it to `PACKAGES` in `build-linux-docker.sh` and rerun. OpenSSL's `Configure` is the likely source (`perl-IPC-Cmd` is the usual one on RHEL 8).
- **A link failure mentioning `dlopen`, `pthread_create` or similar.** That is what `LIBS="-ldl -pthread"` is for. If it is already there and the link still fails, read the tail of `make.log` and extend `LIBS`; if it fails *because* of `LIBS`, drop it — x3270's configure may already supply them.

Read the logs directly; the build redirects them:

```bash
tail -40 native/build-tmp/linux-arm64/openssl.log
tail -40 native/build-tmp/linux-arm64/configure.log
tail -40 native/build-tmp/linux-arm64/make.log
```

- [ ] **Step 3: Write `native/build/verify-linux-start.sh`**

```bash
#!/usr/bin/env bash
# Starts the built binary in a bare container from the pinned floor image: no toolchain, no build leftovers,
# nothing the build put there. Starting on the floor OS with nothing else present is the portability claim
# itself, and it is the one check verify-linux.sh cannot make — that script runs inside a container already
# and cannot start another.
set -euo pipefail
BIN=${1:?usage: verify-linux-start.sh <binary>}
DIR=$(cd "$(dirname "$BIN")" && pwd)
FILE=$(basename "$BIN")
. "$(dirname "$0")/linux-image.sh"
# sed, not head: see verify-linux.sh — head plus pipefail turns a passing check into exit 141.
docker run --rm -v "$DIR:/engine:ro" "$LIZTERM_LINUX_IMAGE" "/engine/$FILE" --version | sed -n '1,3p'
echo "OK: $BIN starts on $LIZTERM_LINUX_IMAGE with no build tools present"
```

- [ ] **Step 4: Run it against the binary just built**

```bash
chmod +x native/build/verify-linux-start.sh
native/build/verify-linux-start.sh native/out/linux-arm64/b3270; echo "exit=$?"
```

Expected: b3270's version banner, the `OK:` line, and `exit=0`. On an Intel Mac or a CI x64 runner the path is `native/out/linux-x64/b3270`.

- [ ] **Step 5: Prove the start check can fail**

A binary from a newer glibc cannot start on the floor image, which is exactly what this check exists to catch.

```bash
native/build/verify-linux-start.sh native/build-tmp/newglibc-fixture; echo "exit=$?"
```

Expected: a loader error (`version 'GLIBC_2.34' not found` or similar) and a non-zero exit. The fixture is the one Task 4, step 3 created; recreate it with that command if it is gone.

- [ ] **Step 6: Wire the start check into the wrapper's build path**

In `build-linux-docker.sh`, append after the `docker run` block, so a developer who runs the wrapper gets the whole gate rather than two thirds of it:

```bash
# The build path gets the whole gate. CI calls this script directly as well, because on a cache hit the
# docker run above never happens and this is the only thing between a stale cached binary and an upload.
if [ "$#" -eq 0 ]; then
  case "$(uname -m)" in
    x86_64)         RID=linux-x64 ;;
    arm64|aarch64)  RID=linux-arm64 ;;
    *) echo "unsupported arch $(uname -m)" >&2; exit 1 ;;
  esac
  "$(dirname "$0")/verify-linux-start.sh" "$ROOT/native/out/$RID/b3270"
fi
```

- [ ] **Step 7: Run the whole thing end to end from clean**

```bash
rm -rf native/out/linux-arm64
native/build/build-linux-docker.sh
ls -l native/out/linux-arm64/b3270
```

Expected: the build runs (OpenSSL is still cached under `build-tmp`, so this is quick), the gate passes, and the start check's `OK:` line is the last thing printed.

- [ ] **Step 8: Commit**

```bash
git add native/build/build-linux.sh native/build/verify-linux-start.sh native/build/build-linux-docker.sh
git commit -m "Build b3270 for Linux against a statically linked OpenSSL 3.5" \
  -m "OpenSSL is built no-shared into a staging prefix with --libdir=lib, so the layout is identical on both architectures and the linker has no shared object to find. The gate runs as the build's last step, and the start check runs the result in a bare container from the floor image: a binary that links cleanly and still cannot start is the failure this whole plan exists to prevent." \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: The `engine-linux` CI job

**Files:**
- Modify: `.github/workflows/platforms.yml` (add one job; no trigger changes — `paths` already covers `native/**`)

**Interfaces:**
- Consumes: everything above.
- Produces: artifacts `b3270-linux-x64` and `b3270-linux-arm64`; check names `engine-linux (linux-x64)` and `engine-linux (linux-arm64)`.

- [ ] **Step 1: Add the job**

Insert after `engine-macos`, before `test-windows`:

```yaml
  engine-linux:
    strategy:
      fail-fast: false
      matrix:
        include:
          # ubuntu-24.04 rather than ubuntu-latest: the two legs must differ only in architecture, and
          # ubuntu-latest rolling to 26.04 while the arm label stays at 24.04 would quietly make them
          # incomparable. arm64 standard runners reached private repositories in January 2026.
          - { runner: ubuntu-24.04, rid: linux-x64 }
          - { runner: ubuntu-24.04-arm, rid: linux-arm64 }
    runs-on: ${{ matrix.runner }}
    # 45, not the macOS job's 30: the arm64 runner has two vCPUs on a private repository and this job builds
    # OpenSSL and x3270 from source on a cold cache.
    timeout-minutes: 45
    env:
      LIZTERM_REQUIRE_ENGINE: "1"
    steps:
      - uses: actions/checkout@v7

      # Source tarballs are architecture-independent, so both legs share one entry. Two legs missing at once
      # means both download and one loses the save race with a warning, which is harmless.
      - name: Cache the pinned source tarballs
        uses: actions/cache@v6
        with:
          path: native/cache
          key: sources-${{ hashFiles('native/build/fetch-*.sh') }}

      # Keyed on every build script, linux-image.sh included, so changing the base image or either pin forces a
      # real build and a fresh gate run.
      - name: Cache the built engine
        id: engine
        uses: actions/cache@v6
        with:
          path: native/out/${{ matrix.rid }}
          key: b3270-${{ matrix.rid }}-${{ hashFiles('native/build/*.sh') }}

      - name: Build b3270 (verify-linux.sh is the gate)
        if: steps.engine.outputs.cache-hit != 'true'
        run: native/build/build-linux-docker.sh

      # build-linux.sh redirects configure, make and the OpenSSL build into native/build-tmp/, which is
      # gitignored and never uploaded, so without this a build failure leaves nothing in the job log but the
      # exit code.
      - name: Show the build logs
        if: ${{ failure() }}
        run: |
          for log in native/build-tmp/*/openssl.log native/build-tmp/*/configure.log native/build-tmp/*/make.log; do
            [ -f "$log" ] || continue
            echo "::group::$log"
            tail -n 200 "$log"
            echo "::endgroup::"
          done

      # These three run whether or not the engine came from the cache. On a cache hit the build step never
      # executes, so they are the only thing standing between a stale cached binary and an artifact upload.
      - name: The engine starts on a bare floor system
        run: native/build/verify-linux-start.sh native/out/${{ matrix.rid }}/b3270

      # A guard that cannot fail is not a guard (plan 3b, deviation 5). One fixture per arm of the gate.
      - name: The gate rejects a binary that links more than glibc
        run: |
          if native/build/build-linux-docker.sh native/build/verify-linux.sh /usr/bin/bash; then
            echo "verify-linux.sh accepted /usr/bin/bash; its dependency allowlist is not working" >&2
            exit 1
          fi

      - name: The gate rejects a binary built on a newer glibc
        run: |
          mkdir -p native/build-tmp
          docker run --rm -v "$PWD/native/build-tmp:/out" debian:12-slim cp /bin/true /out/newglibc-fixture
          if native/build/build-linux-docker.sh native/build/verify-linux.sh native/build-tmp/newglibc-fixture; then
            echo "verify-linux.sh accepted a glibc 2.36 binary; its symbol floor check is not working" >&2
            exit 1
          fi

      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - name: Build (warnings are errors)
        run: dotnet build LizTerm.slnx --configuration Release -warnaserror

      - name: Test (the engine smoke test must run, not skip)
        run: dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx --results-directory TestResults

      # After the smoke test, so a binary that links cleanly but cannot be spawned is never published.
      - name: Upload b3270
        uses: actions/upload-artifact@v7
        with:
          name: b3270-${{ matrix.rid }}
          path: native/out/${{ matrix.rid }}/b3270
          if-no-files-found: error
          retention-days: 14

      - name: Upload test results
        if: ${{ failure() || cancelled() }}
        uses: actions/upload-artifact@v7
        with:
          name: test-results-${{ matrix.rid }}
          path: |
            TestResults/**/*.trx
            TestResults/**/*.dmp
            TestResults/**/*Sequence*.xml
          if-no-files-found: ignore
```

- [ ] **Step 2: Validate the YAML before pushing**

A syntax error surfaces only when GitHub tries to run the file.

```bash
command -v actionlint >/dev/null && actionlint .github/workflows/platforms.yml
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/platforms.yml')); print('yaml ok')"
```

Expected: `yaml ok`, and no actionlint output if it is installed.

- [ ] **Step 3: Commit and push**

```bash
git add .github/workflows/platforms.yml
git commit -m "Build and prove the Linux engines in CI" \
  -m "One matrix job on native runners for both architectures, mirroring engine-macos: build in the pinned container, start the result on a bare floor system, prove the gate still rejects both kinds of bad binary, run the suite with LIZTERM_REQUIRE_ENGINE so the smoke test cannot skip, and only then upload." \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push -u origin HEAD
```

- [ ] **Step 4: Open the PR and watch both legs**

```bash
gh pr create --fill
gh pr checks --watch
```

- [ ] **Step 5: Read the first run's timings**

Note the wall-clock of each leg's build step. If either is under 20 minutes on a cold cache, lower `timeout-minutes` to roughly double the observed time and record the number in Task 7's deviation entry; if either is near 45, raise it and say why.

- [ ] **Step 6: Handle a refuted claim, if it happens**

The spec states a claim this run either proves or refutes: no .NET source change is needed for the Linux RIDs, because `$(NETCoreSdkRuntimeIdentifier)` and `RuntimeInformation.RuntimeIdentifier` both resolve to `linux-x64`/`linux-arm64`. If `EngineSmokeTests` fails with the locator's not-found message while `native/out/<rid>/b3270` exists, the copy rule did not match: print the actual RID with

```bash
dotnet msbuild src/LizTerm.App/LizTerm.App.csproj -getProperty:NETCoreSdkRuntimeIdentifier
```

on the runner, and fix the csproj condition in this plan. Do not work around it in the workflow with a manual copy — that would hide the same bug from `dotnet publish` in plan 3e.

---

### Task 7: Documentation and the as-built record

**Files:**
- Modify: `CLAUDE.md` (the "The b3270 binary" section, and the CI paragraph under Commands)
- Modify: `README.md` (targets and the CI section)
- Modify: `docs/superpowers/specs/2026-09-07-lizterm-m3c-linux-engines-design.md` (section 9)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing code depends on.

- [ ] **Step 1: Update the b3270 section of `CLAUDE.md`**

After the existing macOS sentence ("Build it once with `native/build/build-macos.sh` ..."), add:

```
Linux engines come from `native/build/build-linux-docker.sh`, which needs only Docker: it runs the build inside
`almalinux:8` pinned by digest in `native/build/linux-image.sh`, because that image's glibc 2.28 is the floor
LizTerm supports and is also .NET 10's own. OpenSSL is built from a pinned 3.5 LTS tarball with `no-shared`
rather than taken from the image, whose 1.1.1 has been end-of-life since 2023 and which static linking would
freeze into the binary forever. The gate is three checks, not one: `verify-linux.sh` (inside the container)
rejects a dependency outside the glibc runtime and any imported glibc symbol above 2.28 — `ldd` alone passes a
binary built on Ubuntu 24.04 that cannot start on RHEL 9 — and `verify-linux-start.sh` (on the host) runs the
result in a bare container from the same image. CI proves the gate rejects as well as accepts: `/usr/bin/bash`
for the allowlist arm, a `/bin/true` copied out of `debian:12-slim` for the floor arm. Alpine and any other
musl target is a different RID and out of scope.
```

- [ ] **Step 2: Update the CI paragraph of `CLAUDE.md`**

In the paragraph describing `platforms.yml`, add `engine-linux` beside the other jobs: a two-leg matrix on `ubuntu-24.04` and `ubuntu-24.04-arm` (pinned rather than `ubuntu-latest` so the legs differ only in architecture), `fail-fast: false`, 45 minutes, uploading `b3270-linux-x64` and `b3270-linux-arm64` only after the suite has run with `LIZTERM_REQUIRE_ENGINE=1`.

- [ ] **Step 3: Update `README.md`**

Add `linux-x64` and `linux-arm64` wherever the macOS engine target is named, and add `engine-linux` to the continuous integration section with one line on what it proves.

- [ ] **Step 4: Fill in section 9 of the spec**

Record what actually differed. At minimum, expect entries for:

- The floor-arm negative fixture is a `/bin/true` copied out of `debian:12-slim`, not a C program compiled on the host runner as the spec says. It needs no compiler and behaves identically on a developer's Mac, where a host-compiled binary would be Mach-O and could not be checked at all.
- The allowlist-arm fixture is `/usr/bin/bash` (links `libtinfo`), not `/usr/bin/curl`: bash is present in the image by definition.
- `fetch-source.sh` gained a portable checksum helper, because it now also runs inside the Linux container where `shasum` may be absent.
- Whatever Task 5, step 2 discovered about packages and `LIBS`, and whatever Task 6, step 5 decided about `timeout-minutes`.

- [ ] **Step 5: Verify the docs against the tree**

Every path and script name in the new prose must exist:

```bash
for f in linux-image.sh fetch-openssl.sh build-linux.sh build-linux-docker.sh verify-linux.sh verify-linux-start.sh; do
  test -x "native/build/$f" -o -f "native/build/$f" && echo "ok $f" || echo "MISSING $f"
done
grep -c "engine-linux" .github/workflows/platforms.yml CLAUDE.md README.md
```

Expected: six `ok` lines, and a non-zero count in each of the three files.

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md README.md docs/superpowers/specs/2026-09-07-lizterm-m3c-linux-engines-design.md
git commit -m "Document the Linux engine build and record the plan 3c deviations" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push
```

---

## Done when

`test`, `engine-macos`, `test-windows`, `engine-linux (linux-x64)` and `engine-linux (linux-arm64)` are green on the PR; both `b3270-linux-*` artifacts are downloadable from the run; the two negative-fixture steps pass (that is, they rejected their fixtures); and `native/build/build-linux-docker.sh` produces a gated `linux-arm64` engine on Robert's Mac from a clean `native/out`.
