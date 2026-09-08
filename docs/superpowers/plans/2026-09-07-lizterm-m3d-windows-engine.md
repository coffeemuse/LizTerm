# LizTerm Milestone 3, plan 3d: the Windows engine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cross-build `b3270.exe` for `win-x64` in a digest-pinned Linux container, gate it so a binary that imports a non-system DLL or lacks Schannel never ships, and prove it starts on a real Windows runner in CI.

**Architecture:** A digest-pinned `debian:12-slim` container holds the mingw-w64 toolchain; a host-side wrapper (`build-windows-docker.sh`) is the only thing anyone calls. Inside it, x3270's `b3270` is configured with `--host=x86_64-w64-mingw32` — no OpenSSL and no expat to pin, because Windows uses Schannel and x3270 bundles expat. The gate is split across two machines because a PE binary cannot run on its Linux builder: `engine-windows` checks what the binary *imports*, and `test-windows` downloads it, checks that it *starts and reports Schannel*, runs the suite against it, and only then republishes it under its real name.

**Tech Stack:** Bash, Docker, mingw-w64 (GCC cross toolchain, `objdump`), autotools (x3270 4.5ga6), Windows Schannel, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-07-lizterm-m3d-windows-engine-design.md`

## Global Constraints

- **The target RID is `win-x64`, always**, whatever architecture the builder container is. Output goes to `native/out/win-x64/b3270.exe`, which is where the App csproj's `Exists(...)` condition and copy rule already look. There is no RID handshake and nothing writes `native/build-tmp/rid`.
- **`win-arm64` is not built.** x3270 4.5ga6's `Common/dirnames` knows only `x86_64-w64-mingw32` and `i686-w64-mingw32`; an aarch64 Windows host falls through configure's *unix* branch. It ships the x64 binary under emulation, and *placing* it is plan 3e's job, not this one.
- **The base image is pinned by digest**, in `native/build/windows-image.sh` and nowhere else. It is `debian:12-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171`, resolved as the multi-architecture *index* digest on 2026-09-07.
- **Nothing external is pinned beyond the x3270 source.** No OpenSSL, no expat, no `.pin` stamps, no static prefixes. `SSLLIB` is never defined in the x3270 tree, so TLS is Schannel through `-lcrypt32 -lsecur32`, and expat is bundled at `extern/libexpat`. Do not add plan 3c's stamp machinery here: it guards static prefixes, and there are none.
- **`fetch-source.sh` is reused unchanged**, at the same pin and the same checksum.
- **The engine must report `TLS provider: Windows Schannel`.** A binary reporting anything else — `None` above all — is a failed build, not a variant.
- **The allowed PE imports are exactly these eleven**, matched case-insensitively: `advapi32.dll`, `crypt32.dll`, `gdi32.dll`, `kernel32.dll`, `shell32.dll`, `secur32.dll`, `user32.dll`, `winspool.drv`, `ws2_32.dll`, `comdlg32.dll`, `msvcrt.dll`.
- **No .NET source changes are expected.** `B3270Locator.FileName` is already `b3270.exe` on Windows and the executable-bit arm is already skipped there. If a change turns out to be needed, it belongs to this plan and to the spec's section 9.
- **Every script starts `set -euo pipefail`** and follows the existing `native/build` scripts in shape and commentary. `windows-image.sh` is the exception: it is sourced, not executed, and sets one variable.
- **Commit messages follow house style**: an imperative sentence subject, no `feat:`/`fix:` prefixes, and a `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` trailer.
- **The CI runner is `ubuntu-24.04`**, never `ubuntu-latest`, matching the Linux legs.

---

### Task 1: Pin the builder image

**Files:**
- Create: `native/build/windows-image.sh`

**Interfaces:**
- Consumes: nothing.
- Produces: `LIZTERM_WINDOWS_IMAGE`, a shell variable holding the pinned image reference, sourced by `build-windows-docker.sh` (Task 2).

- [ ] **Step 1: Confirm the recorded digest still resolves on both architectures**

The digest in the spec is the multi-architecture index digest. Pinning means we want *that* digest even if the `12-slim` tag has since moved; the check here is only that it still pulls on both platforms, because an arm64 Mac and an x64 runner must both be able to build.

```bash
docker pull --platform linux/amd64 debian:12-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171
docker pull --platform linux/arm64 debian:12-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171
```

Expected: both succeed. If either reports `no matching manifest`, the digest is a platform manifest rather than the index; re-resolve with `docker buildx imagetools inspect debian:12-slim --format '{{.Manifest.Digest}}'`, use that value everywhere in this plan instead, and note the change in the spec's section 9.

- [ ] **Step 2: Write the file**

```bash
cat > native/build/windows-image.sh <<'EOF'
#!/usr/bin/env bash
# The pinned Windows cross-build base, sourced (not executed) by build-windows-docker.sh.
#
# Unlike linux-image.sh, this image is a *build host* and nothing more. AlmaLinux 8 was chosen because its
# glibc 2.28 is the floor LizTerm's Linux users inherit -- the image and the product share an ABI. A PE binary
# shares nothing with the container it was cross-compiled in, so no property of this image reaches a user, and
# the choice is free to be "the distribution with the mingw-w64 toolchain we want". Reusing the AlmaLinux pin
# here would mean an older EPEL toolchain picked for a reason that does not apply.
#
# Pinned by digest anyway, for the reason linux-image.sh is: a floating tag can raise the toolchain silently,
# and the first anyone would know is a binary that imports a DLL the gate rejects -- or, worse, one it does not.
#
# It must be the multi-architecture *index* digest, not one platform manifest's, so this builds on an arm64 Mac
# and an x64 runner alike. `docker buildx imagetools inspect debian:12-slim --format '{{.Manifest.Digest}}'`
# gives the index digest; this one was resolved on 2026-09-07.
#
# It is a *.sh under native/build, so hashFiles('native/build/*windows*.sh') covers it: changing the image
# invalidates the CI engine cache and forces a real build and a fresh gate run. It is a separate file from
# linux-image.sh, rather than one shared list, because the two cache keys are separate on purpose -- a Debian
# bump cannot change a Linux or macOS binary, and sharing the file would force cold rebuilds that cannot differ.
LIZTERM_WINDOWS_IMAGE=debian:12-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171
EOF
chmod +x native/build/windows-image.sh
```

- [ ] **Step 3: Verify it sources and names a runnable image**

```bash
. native/build/windows-image.sh && docker run --rm "$LIZTERM_WINDOWS_IMAGE" cat /etc/debian_version
```

Expected: a `12.x` version string.

- [ ] **Step 4: Commit**

```bash
git add native/build/windows-image.sh
git commit -m "Pin the Windows cross-build base by digest" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: The container wrapper

**Files:**
- Create: `native/build/build-windows-docker.sh`

**Interfaces:**
- Consumes: `LIZTERM_WINDOWS_IMAGE` from `windows-image.sh` (Task 1); `sha256_stdin` from `shared-sha256.sh`.
- Produces: `native/build/build-windows-docker.sh`. With no arguments it runs `native/build/build-windows.sh` (Task 5) inside the pinned image. With arguments it runs those inside the same image instead, which is how CI runs the gate on a cache hit and how Task 3 tests the gate before a build exists.

- [ ] **Step 1: Write the wrapper**

Note the package list. `curl` and `ca-certificates` are for `shared-fetch-tarball.sh`, which runs inside this container and is not needed by the spike's host-side extraction — they are easy to forget and the failure is a bare `curl: not found`. `python3` is x3270's: its configure refuses to run without one. `binutils-mingw-w64-x86-64` supplies the `objdump` the gate needs, and is listed explicitly rather than relied on as a dependency of the compiler package.

```bash
cat > native/build/build-windows-docker.sh <<'EOF'
#!/usr/bin/env bash
# Cross-builds b3270.exe for win-x64 inside the pinned container. This is the only script CI or a developer
# calls; it needs Docker on the host and nothing else.
#
# With no arguments it runs the build. With arguments it runs those inside the same image instead, which is how
# CI exercises the gate -- against the built binary and against a fixture -- without a second wrapper.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
. "$(dirname "$0")/windows-image.sh"
. "$(dirname "$0")/shared-sha256.sh"

# gcc-mingw-w64-x86-64 is the cross compiler configure looks for as x86_64-w64-mingw32-gcc, and
# binutils-mingw-w64-x86-64 supplies the matching windres it also requires and the objdump verify-windows.sh
# reads the import table with -- named explicitly rather than left to come in as a dependency, because the gate
# breaking on a dependency change would be a confusing failure. curl and ca-certificates are
# shared-fetch-tarball.sh's, which runs in here: debian:12-slim ships neither. python3 is x3270's -- its
# configure refuses to run without one ("Can't find Python using 'python3'") because the build generates
# several C sources with Python scripts.
PACKAGES="gcc-mingw-w64-x86-64 binutils-mingw-w64-x86-64 make python3 curl ca-certificates"

if [ "$#" -eq 0 ]; then
  COMMAND="native/build/build-windows.sh"
else
  COMMAND=$(printf '%q ' "$@")
fi

# The container writes as root. Root-owned files under native/ break the actions/cache save and the artifact
# upload that follow it in CI, and a later local build. The trap covers the failure paths too, so a broken
# build does not leave a tree only root can rebuild in.
HOST_UID=$(id -u)
HOST_GID=$(id -g)

# The toolchain is a layer, not a step, for the reason build-linux-docker.sh gives: apt-get install inside a
# fresh container on every invocation is pure waste on the critical path. The tag carries the base digest and
# the package list, so bumping either builds a new image instead of silently reusing the old one.
IMAGE_TAG="lizterm-windows-build:$(printf '%s\n%s\n' "$LIZTERM_WINDOWS_IMAGE" "$PACKAGES" | sha256_stdin | cut -c1-16)"
if ! docker image inspect "$IMAGE_TAG" >/dev/null 2>&1; then
  echo "Building $IMAGE_TAG from $LIZTERM_WINDOWS_IMAGE" >&2
  # A Dockerfile on stdin with "-" as the context: nothing from the repo is sent to the daemon.
  docker build -t "$IMAGE_TAG" - <<DOCKERFILE
FROM $LIZTERM_WINDOWS_IMAGE
RUN apt-get update \
 && apt-get install -y --no-install-recommends $PACKAGES \
 && rm -rf /var/lib/apt/lists/*
DOCKERFILE
fi

docker run --rm -v "$ROOT:/src" -w /src "$IMAGE_TAG" bash -c "
set -euo pipefail
trap 'chown -R $HOST_UID:$HOST_GID native/out native/build-tmp native/cache 2>/dev/null || true' EXIT
$COMMAND
"

# There is deliberately no post-run step here, and its absence is the one real difference from
# build-linux-docker.sh. That script reads back the RID the container resolved and then runs a host-side start
# check, because Docker's platform and the host's can disagree and because a Linux host can run a Linux binary.
# Neither applies: the target here is win-x64 whatever the container is, so the path is known before the
# container starts; and nothing on this machine can run a PE binary, so the start check lives in the
# test-windows CI job instead. See section 4.2 of the spec.
EOF
chmod +x native/build/build-windows-docker.sh
```

- [ ] **Step 2: Verify the image builds and the toolchain is present**

The build script does not exist yet, so use the argument form. This is the same seam CI uses on a cache hit.

```bash
native/build/build-windows-docker.sh bash -c 'x86_64-w64-mingw32-gcc --version | head -1; x86_64-w64-mingw32-windres --version | head -1; x86_64-w64-mingw32-objdump --version | head -1; python3 --version; curl --version | head -1'
```

Expected: a GCC version line, a windres line, an objdump line, a Python 3 line and a curl line. The first run prints `Building lizterm-windows-build:...` first; a second run of the same command must not, which is the derived-image layer doing its job.

- [ ] **Step 3: Verify the no-argument form fails cleanly for the right reason**

```bash
native/build/build-windows-docker.sh; echo "exit=$?"
```

Expected: a non-zero exit complaining that `native/build/build-windows.sh` does not exist. That is Task 5's job; this confirms the default command is wired to the right path and not silently doing nothing.

- [ ] **Step 4: Commit**

```bash
git add native/build/build-windows-docker.sh
git commit -m "Run the Windows cross build inside the pinned container" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: The gate, before the thing it gates

**Files:**
- Create: `native/build/verify-windows.sh`

**Interfaces:**
- Consumes: `build-windows-docker.sh`'s argument form (Task 2) to run inside the container, where the mingw `objdump` lives.
- Produces: `native/build/verify-windows.sh <binary.exe>`. Exits 0 and prints an `OK:` line plus the import list when the binary is a PE32+ x86-64 executable importing only allowed DLLs; exits 1 with `ERROR:` on stderr otherwise. `build-windows.sh` (Task 5) calls it as its last step, and the CI job (Task 6) calls it directly.

Writing the gate before the build is deliberate, as plan 3c did: it can be tested against fixtures that take seconds to produce, and a gate written after the binary it must judge tends to be written to accept that binary.

- [ ] **Step 1: Write the two fixtures the gate will be tested against**

The bad fixture imports a DLL of our own rather than `libwinpthread-1.dll`. That is what a real bad build imports, but whether a mingw program picks up libwinpthread depends on which thread model the toolchain defaults to, and a fixture whose failure mode depends on a package default is not a test. A DLL we build ourselves exercises the identical gate arm with no assumptions.

```bash
mkdir -p native/build-tmp/fixtures
cat > native/build-tmp/fixtures/clean.c <<'EOF'
int main(void) { return 0; }
EOF
cat > native/build-tmp/fixtures/dll.c <<'EOF'
__declspec(dllexport) int fixture(void) { return 0; }
EOF
cat > native/build-tmp/fixtures/uses-dll.c <<'EOF'
__declspec(dllimport) int fixture(void);
int main(void) { return fixture(); }
EOF
native/build/build-windows-docker.sh bash -c '
  cd native/build-tmp/fixtures
  x86_64-w64-mingw32-gcc -o clean.exe clean.c
  x86_64-w64-mingw32-gcc -shared -o libfixture.dll dll.c -Wl,--out-implib,libfixture.dll.a
  x86_64-w64-mingw32-gcc -o dirty.exe uses-dll.c -L. -lfixture
  x86_64-w64-mingw32-objdump -p clean.exe | grep "DLL Name"
  echo ---
  x86_64-w64-mingw32-objdump -p dirty.exe | grep "DLL Name"
'
```

Expected: `clean.exe` names only DLLs from the allowlist (`msvcrt.dll` and `kernel32.dll`), and `dirty.exe` additionally names `libfixture.dll`. If `clean.exe` names something outside the eleven, stop: the allowlist in the Global Constraints is wrong for this toolchain and the spec's section 9 needs the correction before continuing.

- [ ] **Step 2: Write the failing test — run the not-yet-existing gate against both fixtures**

```bash
native/build/build-windows-docker.sh bash -c '
  native/build/verify-windows.sh native/build-tmp/fixtures/clean.exe
'; echo "exit=$?"
```

Expected: FAIL — `native/build/verify-windows.sh: No such file or directory`, non-zero exit.

- [ ] **Step 3: Write the gate**

```bash
cat > native/build/verify-windows.sh <<'EOF'
#!/usr/bin/env bash
# Fails if the binary is not a 64-bit PE executable, or if it imports a DLL outside the Windows system set.
# Runs INSIDE the build container (it needs that container's mingw objdump).
set -euo pipefail
BIN=${1:?usage: verify-windows.sh <binary.exe>}
OBJDUMP=${OBJDUMP:-x86_64-w64-mingw32-objdump}

# 1. The right machine. A 32-bit build is a plausible accident -- i686-w64-mingw32 is a target the same source
# tree supports and the same configure would accept -- and it would produce a binary that runs on the developer's
# Windows box and refuses to load in a win-x64 .NET process. Nothing below would notice: a 32-bit build imports
# the same system DLLs.
ARCH=$("$OBJDUMP" -f "$BIN" | sed -n 's/^architecture: *\([^,]*\).*/\1/p')
if [ "$ARCH" != "i386:x86-64" ]; then
  echo "ERROR: $BIN is $ARCH, not i386:x86-64. Was --host=x86_64-w64-mingw32 passed to configure?" >&2
  exit 1
fi

# 2. Every imported DLL must be one Windows ships. This is verify-linux.sh's check 1 spelled for PE: a
# libwinpthread-1.dll or libgcc_s_seh-1.dll here means the engine will not start on a machine without the
# MinGW runtime beside it, and anything cryptographic would mean the build found a library it should not have.
# Matched case-insensitively because mingw emits WINSPOOL.DRV, Secur32.dll and comdlg32.dll in one binary, and
# a case-sensitive list is a trap for whoever extends it. winspool.drv is why the pattern is not just "\.dll".
ALLOWED='^(advapi32\.dll|crypt32\.dll|gdi32\.dll|kernel32\.dll|shell32\.dll|secur32\.dll|user32\.dll|winspool\.drv|ws2_32\.dll|comdlg32\.dll|msvcrt\.dll)$'
IMPORTS=$("$OBJDUMP" -p "$BIN" | awk '/DLL Name:/ {print tolower($3)}' | sort -u)
if [ -z "$IMPORTS" ]; then
  echo "ERROR: $BIN imports no DLLs at all. Is it a PE executable with an import table?" >&2
  exit 1
fi
BAD=$(printf '%s\n' "$IMPORTS" | grep -v -E "$ALLOWED" || true)
if [ -n "$BAD" ]; then
  echo "ERROR: $BIN imports DLLs outside the Windows system set:" >&2
  printf '%s\n' "$BAD" >&2
  exit 1
fi

# There is deliberately no third check here, and no TLS check. verify-linux.sh's check 2 (the highest imported
# glibc symbol version) has no PE analogue: Windows import tables carry no symbol versioning, and the API
# surface b3270 uses predates every Windows version LizTerm supports. The TLS check is the one that cannot run
# on this machine at all -- a PE binary will not execute on the Linux builder -- so it happens on the Windows
# runner instead, through shared-verify-tls.sh in the test-windows job. See section 4 of the plan 3d spec.
echo "OK: $BIN is a 64-bit PE importing only Windows system DLLs"
printf '%s\n' "$IMPORTS"
EOF
chmod +x native/build/verify-windows.sh
```

- [ ] **Step 4: Run the gate against both fixtures and verify accept and reject**

```bash
native/build/build-windows-docker.sh bash -c '
  set -u
  native/build/verify-windows.sh native/build-tmp/fixtures/clean.exe || { echo "FAIL: clean fixture rejected"; exit 1; }
  if OUT=$(native/build/verify-windows.sh native/build-tmp/fixtures/dirty.exe 2>&1); then
    echo "FAIL: dirty fixture accepted"; exit 1
  fi
  case "$OUT" in
    *"imports DLLs outside the Windows system set"*) echo "PASS: rejected by the right arm" ;;
    *) echo "FAIL: rejected, but by the wrong arm:"; printf "%s\n" "$OUT"; exit 1 ;;
  esac
'
```

Expected: an `OK:` line for `clean.exe`, then `PASS: rejected by the right arm`.

Asserting the *message* rather than a bare non-zero exit is the point plan 3c had to learn twice: `build-windows-docker.sh` also exits non-zero for a Docker Hub rate limit or a failed `apt-get install`, and an `if ...; then fail; fi` shape reports every one of those as a successful rejection.

- [ ] **Step 5: Verify the architecture arm rejects too**

```bash
native/build/build-windows-docker.sh bash -c '
  if OUT=$(native/build/verify-windows.sh /bin/true 2>&1); then
    echo "FAIL: an ELF binary was accepted"; exit 1
  fi
  case "$OUT" in
    *"not i386:x86-64"*) echo "PASS: ELF rejected by the architecture arm" ;;
    *) echo "FAIL: wrong arm:"; printf "%s\n" "$OUT"; exit 1 ;;
  esac
'
```

Expected: `PASS: ELF rejected by the architecture arm`. This fixture is free — an ELF binary is the thing most likely to arrive here by mistake, from a copy rule that picked the wrong path.

- [ ] **Step 6: Clean up the fixtures and commit**

`native/build-tmp` is gitignored, so nothing to remove from the index; delete the directory so a later task starts clean.

```bash
rm -rf native/build-tmp/fixtures
git add native/build/verify-windows.sh
git commit -m "Gate the Windows engine on its machine type and its DLL imports" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: Generalize the shared TLS check

**Files:**
- Modify: `native/build/shared-verify-tls.sh`

**Interfaces:**
- Consumes: nothing new.
- Produces: `shared-verify-tls.sh <binary> [expected-provider]`. The second argument defaults to `OpenSSL`, so `verify-linux.sh` and `verify-macos.sh` — which pass one argument — are unchanged in behaviour. The Windows CI job passes `Windows Schannel`.

As written the script asserts `*"TLS provider: OpenSSL"*` and would reject a correct Windows engine. Its own header already claims the failure it catches "is x3270's, not either platform's"; Windows makes that true while showing the assertion was written too narrowly.

- [ ] **Step 1: Write the failing test**

Stub binaries stand in for engines: the script only runs `"$BIN" --version` and reads the output, so an executable shell script is a faithful stand-in and needs no cross-compiler.

```bash
mkdir -p native/build-tmp/tls-fixtures
cd native/build-tmp/tls-fixtures
for p in "OpenSSL" "Windows Schannel" "None"; do
  name=$(printf '%s' "$p" | tr ' ' '-')
  cat > "$name" <<EOF
#!/usr/bin/env bash
echo "b3270 v4.5ga6 stub" >&2
echo "TLS provider: $p" >&2
EOF
  chmod +x "$name"
done
cat > broken <<'EOF'
#!/usr/bin/env bash
echo "dyld: symbol not found" >&2
exit 3
EOF
chmod +x broken
cd - >/dev/null

cat > native/build-tmp/tls-test.sh <<'EOF'
#!/usr/bin/env bash
# Not a committed test: the repo has no shell test harness, and this exists to drive one red/green cycle.
set -u
F=native/build-tmp/tls-fixtures
G=native/build/shared-verify-tls.sh
fails=0
ok()   { echo "PASS: $1"; }
bad()  { echo "FAIL: $1"; fails=$((fails+1)); }

$G "$F/OpenSSL"                  >/dev/null 2>&1 && ok "default accepts OpenSSL"            || bad "default accepts OpenSSL"
$G "$F/None"                     >/dev/null 2>&1 && bad "default rejects None"              || ok "default rejects None"
$G "$F/Windows-Schannel"         >/dev/null 2>&1 && bad "default rejects Schannel"          || ok "default rejects Schannel"
$G "$F/Windows-Schannel" "Windows Schannel" >/dev/null 2>&1 && ok "Schannel expected, accepted" || bad "Schannel expected, accepted"
$G "$F/OpenSSL"          "Windows Schannel" >/dev/null 2>&1 && bad "Schannel expected, OpenSSL rejected" || ok "Schannel expected, OpenSSL rejected"
OUT=$($G "$F/broken" "Windows Schannel" 2>&1)
case "$OUT" in *"did not run"*) ok "a dead binary is not a TLS fault" ;; *) bad "a dead binary is not a TLS fault" ;; esac
echo "failures: $fails"
exit $((fails > 0))
EOF
chmod +x native/build-tmp/tls-test.sh
```

- [ ] **Step 2: Run it and watch the new expectations fail**

```bash
native/build-tmp/tls-test.sh
```

Expected: exactly two failures, `FAIL: Schannel expected, accepted` and `FAIL: Schannel expected, OpenSSL rejected`, with `failures: 2` and a non-zero exit. The other four already pass: the three single-argument cases are the regression guard for the two existing callers, and the dead-binary case exercises an arm that already exists and must survive the change untouched.

- [ ] **Step 3: Make the change**

Replace the usage line, the `case` block, and the remedy text:

```bash
python3 - <<'EOF'
import pathlib, re
p = pathlib.Path("native/build/shared-verify-tls.sh")
s = p.read_text()

s = s.replace(
    'BIN=${1:?usage: shared-verify-tls.sh <binary>}',
    'BIN=${1:?usage: shared-verify-tls.sh <binary> [expected-provider]}\n'
    '# Defaults to OpenSSL so verify-linux.sh and verify-macos.sh, which pass one argument, are unchanged.\n'
    '# Windows is why this is a parameter at all: its provider is Schannel, and the binary is correct.\n'
    'EXPECTED=${2:-OpenSSL}')

old = s[s.index('case "$VERSION" in'):s.index('esac') + 4]
new = '''case "$VERSION" in
  *"TLS provider: $EXPECTED"*) ;;
  *)
    echo "ERROR: $BIN does not report the expected TLS provider ($EXPECTED):" >&2
    printf '%s\\n' "$VERSION" | sed -n '1,3p' >&2
    if [ "$EXPECTED" = "OpenSSL" ]; then
      echo "x3270's configure disables TLS when its -lcrypto link probe fails. On Linux that probe needs the" >&2
      echo "LIBS on build-linux.sh's configure line (see step 3 there); on macOS it needs usable static archives" >&2
      echo "in the prefix build-macos.sh stages from Homebrew's openssl@3." >&2
    else
      echo "The Windows build reaches Schannel through -lcrypt32 -lsecur32 on wb3270's LIBS line, with no" >&2
      echo "configure probe to fail. A provider of None here means the source tree changed shape; a provider of" >&2
      echo "OpenSSL means the build found a cryptographic library it should not have." >&2
    fi
    exit 1 ;;
esac'''
s = s.replace(old, new)
p.write_text(s)
EOF
```

Then update the header comment's first line, which still says "an OpenSSL TLS provider":

```bash
python3 - <<'EOF'
import pathlib
p = pathlib.Path("native/build/shared-verify-tls.sh")
s = p.read_text()
s = s.replace(
    "# Fails if the binary does not report an OpenSSL TLS provider, and prints the first three banner lines when it\n# does. Shared by verify-linux.sh and verify-macos.sh: the failure it catches is x3270's, not either platform's.",
    "# Fails if the binary does not report the expected TLS provider (OpenSSL unless a second argument says\n"
    "# otherwise), and prints the first three banner lines when it does. Shared by verify-linux.sh,\n"
    "# verify-macos.sh and the test-windows CI job: the failure it catches is x3270's, not any one platform's,\n"
    "# which is why Windows joins it by naming its provider rather than by getting a gate of its own.")
p.write_text(s)
EOF
```

- [ ] **Step 4: Run the test again and verify it passes**

```bash
native/build-tmp/tls-test.sh
```

Expected: six `PASS:` lines, `failures: 0`, exit 0.

- [ ] **Step 5: Verify the existing callers are untouched**

Both call it with one argument. Confirm neither file needed editing, and that a syntax check passes on all three:

```bash
grep -n "shared-verify-tls.sh" native/build/verify-linux.sh native/build/verify-macos.sh
bash -n native/build/shared-verify-tls.sh native/build/verify-linux.sh native/build/verify-macos.sh && echo "syntax ok"
```

Expected: two call sites, each passing only `"$BIN"`, and `syntax ok`.

- [ ] **Step 6: Clean up and commit**

```bash
rm -rf native/build-tmp/tls-fixtures native/build-tmp/tls-test.sh
git add native/build/shared-verify-tls.sh
git commit -m "Let the shared TLS check name the provider it expects" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: Build the engine

**Files:**
- Create: `native/build/build-windows.sh`

**Interfaces:**
- Consumes: `fetch-source.sh` (unchanged); `verify-windows.sh` (Task 3); runs inside the image `build-windows-docker.sh` (Task 2) provides.
- Produces: `native/out/win-x64/b3270.exe`, gated. This is the path the App and integration csproj copy rules read, and the path Task 6 caches and uploads.

- [ ] **Step 1: Write the failing test — the wrapper's default path**

```bash
rm -rf native/out/win-x64
native/build/build-windows-docker.sh; echo "exit=$?"
```

Expected: FAIL, `native/build/build-windows.sh: No such file or directory`, non-zero exit.

- [ ] **Step 2: Write the build script**

```bash
cat > native/build/build-windows.sh <<'EOF'
#!/usr/bin/env bash
# Cross-builds b3270.exe for win-x64 with the mingw-w64 toolchain.
# Runs INSIDE the pinned build container (build-windows-docker.sh); assumes that container's toolchain, the way
# build-macos.sh assumes Xcode command line tools and Homebrew.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)

# Unlike build-linux.sh there is no architecture to resolve and nothing to hand back to the wrapper: the target
# is win-x64 whatever this container is. The x3270 source knows only two Windows hosts -- Common/dirnames sets
# win64=x86_64-w64-mingw32 and win32=i686-w64-mingw32 -- and no aarch64 one, which is why win-arm64 ships this
# same binary under Windows 11's x64 emulation rather than a native build of its own.
RID=win-x64
HOST=x86_64-w64-mingw32
BUILD="$ROOT/native/build-tmp/$RID"
mkdir -p "$BUILD"
JOBS=$(nproc)

# There are no static prefixes here, and so no fetch-*.sh of our own and no .pin stamps. That is not an
# omission to be tidied up into symmetry with build-linux.sh later:
#   - OpenSSL: wb3270's LIBS line references SSLLIB, which is never defined anywhere in the x3270 tree and so
#     expands to empty. TLS on Windows is Schannel, reached through -lcrypt32 -lsecur32 on that same line.
#     There is no configure probe that can fail, which is why this script needs no equivalent of
#     build-linux.sh's LIBS="-ldl -pthread".
#   - expat: bundled upstream at extern/libexpat and built by the suite's own libexpat target. Linux needed a
#     pinned static build only because no distribution-independent libexpat exists there.
# The .pin machinery in build-linux.sh guards prefixes whose staleness has no tell. With no prefixes, it would
# guard nothing.
SRC=$("$ROOT/native/build/fetch-source.sh" "$BUILD/src")
cd "$SRC"

# --host is what puts configure into MODE=windows: it matches Common/dirnames' win64 and from there the build
# uses Makefile.windows.in, looks for ${host}-gcc, ${host}-gcc-ar and ${host}-windres, and descends into the w*
# directories. The component flags are build-linux.sh's, unchanged. Disabling x3270if and pr3287 is safe
# despite Makefile.windows.in listing them as b3270 prerequisites: configure never creates their
# subdirectories, so those rules are not reached.
./configure --host="$HOST" --enable-b3270 \
  --disable-x3270 --disable-c3270 --disable-s3270 --disable-tcl3270 \
  --disable-pr3287 --disable-x3270if --disable-mitm --disable-playback \
  > "$BUILD/configure.log" 2>&1
make -j"$JOBS" > "$BUILD/make.log" 2>&1

# obj/x86_64-w64-mingw32/b3270/, note: the object directory is b3270 even though the source directory is
# wb3270. sed rather than head for the same SIGPIPE-under-pipefail reason the Linux scripts document.
BIN=$(find obj -type f -name b3270.exe | sort | sed -n 1p)
[ -n "$BIN" ] || { echo "no b3270.exe under obj/ after a successful make" >&2; exit 1; }
OUT="$ROOT/native/out/$RID"
mkdir -p "$OUT"
cp "$BIN" "$OUT/b3270.exe"
# No chmod: Windows has no executable bit, and B3270Locator skips that check there. This is also why the CI
# artifact needs no chmod on the way back in, unlike the macOS and Linux engines.
"$ROOT/native/build/verify-windows.sh" "$OUT/b3270.exe"
EOF
chmod +x native/build/build-windows.sh
```

- [ ] **Step 3: Run the build**

```bash
time native/build/build-windows-docker.sh
```

Expected: an `OK: ... is a 64-bit PE importing only Windows system DLLs` line followed by the import list. Record the wall time; Task 6 step 6 uses it as a sanity check against the runner's. The spike measured 95 s cold on an arm64 Mac.

If configure fails, read `native/build-tmp/win-x64/configure.log`; if make fails, `make.log`. A `Can't find Windows C compiler` means the package list in Task 2 lost `gcc-mingw-w64-x86-64`; a `Can't find Python` means it lost `python3`.

- [ ] **Step 4: Verify the artifact independently of the gate**

```bash
ls -l native/out/win-x64/b3270.exe
native/build/build-windows-docker.sh bash -c '
  x86_64-w64-mingw32-objdump -f native/out/win-x64/b3270.exe | sed -n 1,3p
  strings -a native/out/win-x64/b3270.exe | grep -x "Windows Schannel" && echo "schannel linked"
'
```

Expected: a `pei-x86-64` file format line, and `Windows Schannel` present. The string check is a weak proxy — it proves `sio_schannel.c` was linked, not that the running engine reports it — and it exists only to catch a catastrophic miss here rather than on the Windows runner. The real check is Task 6's.

- [ ] **Step 5: Verify a rebuild is idempotent**

```bash
native/build/build-windows-docker.sh && echo "second build ok"
```

Expected: it rebuilds and gates cleanly. `fetch-source.sh` reuses the cached tarball, and unlike the Linux script there is no prefix to go stale.

- [ ] **Step 6: Commit**

```bash
git add native/build/build-windows.sh
git commit -m "Cross-build b3270.exe for win-x64 against Schannel" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: CI — build on Linux, prove on Windows

**Files:**
- Modify: `.github/workflows/platforms.yml` (add `engine-windows`; change `test-windows`)

**Interfaces:**
- Consumes: `build-windows-docker.sh` (Task 2), `verify-windows.sh` (Task 3), `shared-verify-tls.sh` with its provider argument (Task 4), `build-windows.sh` (Task 5).
- Produces: the `b3270-win-x64` artifact, published only after a Windows runner has started the binary.

- [ ] **Step 1: Add the `engine-windows` job**

Insert between the `engine-linux` job and `test-windows`.

```yaml
  engine-windows:
    runs-on: ubuntu-24.04
    # Provisional. Step 6 lowers it from the first real runs, as engine-linux's was: the spike measured 95s
    # cold on an arm64 Mac, and an x64 runner is a different machine.
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v7

      # The same entry engine-linux uses: source tarballs are architecture- and platform-independent, and this
      # job needs only the x3270 one of them. Whichever job saves first, the other finds what it needs or
      # downloads the rest; a lost save race is a warning, not a failure.
      - name: Cache the pinned source tarballs
        uses: actions/cache@v6
        with:
          path: native/cache
          key: sources-${{ hashFiles('native/build/fetch-*.sh') }}

      # Keyed on the Windows scripts (windows-image.sh included, so changing the base image forces a real
      # build), the x3270 fetcher, and the shared machinery -- but not on the Linux or macOS scripts, which
      # cannot change this binary.
      - name: Cache the built engine
        id: engine
        uses: actions/cache@v6
        with:
          path: native/out/win-x64
          key: b3270-win-x64-${{ hashFiles('native/build/*windows*.sh', 'native/build/fetch-source.sh', 'native/build/shared-*.sh') }}

      - name: Build b3270.exe (verify-windows.sh is the gate)
        if: steps.engine.outputs.cache-hit != 'true'
        run: native/build/build-windows-docker.sh

      # build-windows.sh redirects configure and make into native/build-tmp/, which is gitignored and never
      # uploaded, so without this a build failure leaves nothing in the job log but the exit code. cancelled()
      # as well as failure(): timeout-minutes and cancel-in-progress both cancel rather than fail, and a build
      # wedged past the job timeout is exactly the run whose logs are worth having.
      - name: Show the build logs
        if: ${{ failure() || cancelled() }}
        run: |
          for log in native/build-tmp/win-x64/configure.log native/build-tmp/win-x64/make.log; do
            [ -f "$log" ] || continue
            echo "::group::$log"
            tail -n 200 "$log"
            echo "::endgroup::"
          done

      # Runs whether or not the engine came from the cache: on a cache hit the build step never executes, so
      # this and the Windows-side start check below are all that stand between a stale cached binary and a
      # published artifact -- which is why it runs the gate against the built binary and not only the fixture.
      # Each arm is asserted on the message it prints, never on a bare non-zero exit: the wrapper also exits
      # non-zero for a Docker Hub rate limit or a failed apt-get install, and asserting only "it failed" would
      # report every one of those as a successful rejection.
      - name: The gate accepts the engine and rejects a non-system import
        run: |
          mkdir -p native/build-tmp/fixtures
          cat > native/build-tmp/fixtures/dll.c <<'EOF'
          __declspec(dllexport) int fixture(void) { return 0; }
          EOF
          cat > native/build-tmp/fixtures/uses-dll.c <<'EOF'
          __declspec(dllimport) int fixture(void);
          int main(void) { return fixture(); }
          EOF
          native/build/build-windows-docker.sh bash -c '
            set -u
            native/build/verify-windows.sh native/out/win-x64/b3270.exe || { echo "the gate rejected the engine"; exit 1; }
            cd native/build-tmp/fixtures
            x86_64-w64-mingw32-gcc -shared -o libfixture.dll dll.c -Wl,--out-implib,libfixture.dll.a
            x86_64-w64-mingw32-gcc -o dirty.exe uses-dll.c -L. -lfixture
            cd /src
            if OUT=$(native/build/verify-windows.sh native/build-tmp/fixtures/dirty.exe 2>&1); then
              echo "the gate ACCEPTED a binary importing libfixture.dll"; exit 1
            fi
            case "$OUT" in
              *"imports DLLs outside the Windows system set"*) echo "OK: the import arm rejected its fixture" ;;
              *) echo "the fixture was rejected, but not by the import arm:"; printf "%s\n" "$OUT"; exit 1 ;;
            esac
          '

      # Deliberately named -unverified. Nothing has yet started this binary: a PE file cannot run on this
      # runner, and plan 3c's rule is that a binary which links but cannot be spawned is never published.
      # test-windows re-uploads it under its real name once it has run it.
      - name: Upload b3270.exe (not yet started anywhere)
        uses: actions/upload-artifact@v7
        with:
          name: b3270-win-x64-unverified
          path: native/out/win-x64/b3270.exe
          if-no-files-found: error
          retention-days: 14
```

- [ ] **Step 2: Change `test-windows` to consume, prove, and republish the engine**

Replace the whole `test-windows` job with:

```yaml
  test-windows:
    runs-on: windows-latest
    # The engine is built on Linux because x3270 cross-compiles for Windows and this runner has no toolchain.
    # The consequence is accepted deliberately: an engine-windows failure costs the Windows behavioural signal
    # too. ci.yml's `test` still runs the whole suite on every pull request, so what is lost is Windows-specific
    # behaviour only, and the coupling is what buys a start check on a real Windows machine.
    needs: engine-windows
    timeout-minutes: 20
    env:
      LIZTERM_REQUIRE_ENGINE: "1"
    steps:
      - uses: actions/checkout@v7

      # Into native/out/win-x64, which is exactly where the App csproj's Exists() condition looks, so the
      # existing copy rule puts it at runtimes/win-x64/native/b3270.exe with no project change. No chmod on the
      # way in, unlike the macOS and Linux engines: Windows has no executable bit for an artifact to lose.
      - name: Download the engine engine-windows built
        uses: actions/download-artifact@v7
        with:
          name: b3270-win-x64-unverified
          path: native/out/win-x64

      # The half of the gate that cannot run on the builder. shared-verify-tls.sh takes the expected provider
      # as its second argument; without it the script defaults to OpenSSL and would reject a correct engine.
      - name: The engine starts and reports Schannel
        shell: bash
        run: native/build/shared-verify-tls.sh native/out/win-x64/b3270.exe "Windows Schannel"

      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - name: Build (warnings are errors)
        run: dotnet build LizTerm.slnx --configuration Release -warnaserror

      - name: Test (the engine smoke test must run, not skip)
        run: dotnet test LizTerm.slnx --configuration Release --no-build --blame-hang-timeout 5m --logger trx --results-directory TestResults

      # Only now, after a Windows machine has started it and the suite has spawned it through B3270Session,
      # does the binary get published under the name anything downstream would consume.
      - name: Upload b3270.exe
        uses: actions/upload-artifact@v7
        with:
          name: b3270-win-x64
          path: native/out/win-x64/b3270.exe
          if-no-files-found: error
          retention-days: 14

      - name: Upload test results
        if: ${{ failure() || cancelled() }}
        uses: actions/upload-artifact@v7
        with:
          name: test-results-windows
          path: |
            TestResults/**/*.trx
            TestResults/**/*.dmp
            TestResults/**/*Sequence*.xml
          if-no-files-found: ignore
```

- [ ] **Step 3: Check the workflow parses and the path filter still fits**

```bash
python3 -c "import yaml,sys; d=yaml.safe_load(open('.github/workflows/platforms.yml')); print(sorted(d['jobs'])); print(d['jobs']['test-windows']['needs'])"
grep -n "native/\*\*\|src/\*\*\|tests/\*\*" .github/workflows/platforms.yml
```

Expected: four jobs — `engine-linux`, `engine-macos`, `engine-windows`, `test-windows` — `needs` reading `engine-windows`, and the path filter unchanged (it already covers `native/**`, so the new scripts are in scope).

If `yaml` is not installed, `pip install pyyaml` in a throwaway venv or skip to the push; the workflow parse error would surface on the run instead.

- [ ] **Step 4: Commit and push**

```bash
git add .github/workflows/platforms.yml
git commit -m "Build the Windows engine in CI and prove it on a Windows runner" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push
```

- [ ] **Step 5: Open the PR and watch every job**

```bash
gh pr create --title "Milestone 3 plan 3d: the Windows engine" --body "$(cat <<'BODY'
Cross-builds b3270.exe for win-x64 with mingw-w64 inside a digest-pinned debian:12-slim container, gates it on
its machine type and DLL imports, and proves it starts and reports Schannel on a real Windows runner before
publishing it.

No OpenSSL or expat is pinned for this target: SSLLIB is never defined in the x3270 tree, so TLS is Schannel
through -lcrypt32 -lsecur32, and expat is bundled upstream. win-arm64 is not built -- 4.5ga6's configure knows
no aarch64 Windows host -- and ships this binary under emulation, which plan 3e will place.

test-windows now depends on engine-windows, downloads its unverified artifact, runs the suite with
LIZTERM_REQUIRE_ENGINE=1, and republishes it as b3270-win-x64.

Spec: docs/superpowers/specs/2026-09-07-lizterm-m3d-windows-engine-design.md

🤖 Generated with [Claude Code](https://claude.com/claude-code)
BODY
)"
gh run list --branch "$(git branch --show-current)" --limit 6
```

Then for each run id: `gh run watch <run-id> --exit-status`. Expected: `test`, `engine-macos`, `engine-linux` (both legs), `engine-windows` and `test-windows` all green.

Likely findings and their fixes:

- `Unable to resolve action actions/download-artifact@v7`: this repo pins `upload-artifact` at v7, and the download action's major is assumed to match. Read the error for the available major, use it, and record the correction in the spec's section 9.
- The `shared-verify-tls.sh` step failing with a bash error on Windows: confirm the step carries `shell: bash`. Without it the runner uses PowerShell and the script never runs.
- `EngineSmokeTests` failing rather than skipping with "LIZTERM_REQUIRE_ENGINE is set but ...": the download landed somewhere the copy rule does not read. Check that `native/out/win-x64/b3270.exe` exists before the build step — `actions/download-artifact` with `path:` does not nest the artifact name inside it, but verify rather than assume.
- A headless App test failing only here: fix the test's assumption, never widen it to "anything passes".

Commit each fix on its own with a subject that names the finding, push, and watch again.

- [ ] **Step 6: Set `timeout-minutes` from what the run measured**

Read the real duration of `engine-windows` — `gh run view <run-id> --json jobs --jq '.jobs[] | select(.name=="engine-windows") | {startedAt, completedAt}'` — for both a cold run and a cache hit. Follow `engine-linux`'s practice: lower `timeout-minutes` to roughly 3x the slowest observed run, and replace the provisional comment with the measured numbers and their date. Do not tighten it on reasoning alone; if only one run is in hand, say so in the comment.

```bash
git add .github/workflows/platforms.yml
git commit -m "Set engine-windows's timeout-minutes from its first measured runs" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push
```

- [ ] **Step 7: Confirm the published artifact**

With `$SCRATCH` set to the session's scratchpad directory:

```bash
gh run download <platforms-run-id> --name b3270-win-x64 --dir "$SCRATCH/ci-engine-win"
file "$SCRATCH/ci-engine-win/b3270.exe"
```

Expected: `PE32+ executable (console) x86-64, for MS Windows`. Confirm too that `b3270-win-x64-unverified` exists alongside it — that is the intermediate, and its presence is the two-stage publish working rather than a leak.

---

### Task 7: Documentation and the as-built record

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-09-07-lizterm-m3d-windows-engine-design.md` (section 9)

- [ ] **Step 1: Update `CLAUDE.md`**

In "The b3270 binary", after the Linux paragraph, add the Windows one. It must say: `native/build/build-windows-docker.sh` needs only Docker and always produces `win-x64` whatever the host is; the base is `debian:12-slim` pinned by digest in `native/build/windows-image.sh`, chosen as a build host rather than a floor because a PE binary shares no ABI with its builder; nothing but the x3270 source is pinned, because `SSLLIB` is never defined so TLS is Schannel and expat is bundled upstream; the gate is `verify-windows.sh` (machine type and DLL imports, in the container) plus `shared-verify-tls.sh` on the Windows runner, split because a PE binary cannot run on its Linux builder; and `win-arm64` ships the x64 binary because 4.5ga6's `Common/dirnames` knows no aarch64 Windows host.

In the CI paragraph, add `engine-windows` beside the other jobs, and record that `test-windows` now `needs` it, downloads `b3270-win-x64-unverified`, runs the suite with `LIZTERM_REQUIRE_ENGINE=1`, and republishes it as `b3270-win-x64` — with the reason for the two names.

Also update the `shared-verify-tls.sh` description in the Linux paragraph: it now takes the expected provider as an optional second argument, defaulting to OpenSSL.

- [ ] **Step 2: Update `README.md`**

Add `win-x64` wherever the built engine targets are named; add a "Building the Windows engine" section on the same model as the Linux one; and add `engine-windows` to the continuous integration section with one line on what it proves, noting that `test-windows` now depends on it.

- [ ] **Step 3: Fill in section 9 of the spec**

Record what actually differed. At minimum, expect entries for:

- Whatever Task 2 discovered about the container's package list — in particular that `curl` and `ca-certificates` are needed because `shared-fetch-tarball.sh` runs inside the container and `debian:12-slim` ships neither, which the spike did not discover because it extracted on the host.
- The negative fixture is a DLL built on the spot, not the `libwinpthread-1.dll` the spec's section 4.3 names. A program's libwinpthread dependency turns on the toolchain's default thread model, and a fixture whose failure mode depends on a package default does not test the gate.
- `verify-windows.sh` gained a machine-type arm the spec did not call for, because a 32-bit build is a plausible accident the import check cannot see.
- The `actions/download-artifact` major, if step 5 had to correct it.
- Whatever Task 6 step 6 measured, and the `timeout-minutes` it set.
- Any .NET change that proved necessary, contradicting the spec's expectation of none.

- [ ] **Step 4: Verify the docs against the tree**

Every path and script name in the new prose must exist:

```bash
for f in windows-image.sh build-windows.sh build-windows-docker.sh verify-windows.sh; do
  test -f "native/build/$f" && echo "ok $f" || echo "MISSING $f"
done
grep -c "engine-windows" .github/workflows/platforms.yml CLAUDE.md README.md
grep -c "win-x64" CLAUDE.md README.md
```

Expected: four `ok` lines, and a non-zero count in each file.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md README.md docs/superpowers/specs/2026-09-07-lizterm-m3d-windows-engine-design.md
git commit -m "Document the Windows engine build and record the plan 3d deviations" \
  -m "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push
```

---

## Done when

`test`, `engine-macos`, `engine-linux` (both legs), `engine-windows` and `test-windows` are green on the PR;
`b3270-win-x64` is downloadable from the run and `file` reports a PE32+ x86-64 executable; the `test-windows`
log shows `TLS provider: Windows Schannel` and an `EngineSmokeTests` that ran rather than skipped; the gate
step shows both the accept and the reject, the reject by the import arm's own message; `git status` is clean;
and `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` reports 0.
