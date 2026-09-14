# IND$FILE Transfers from Inside ISPF Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An `ISPF (MVS)` host type in the File Transfer dialog that starts an IND$FILE transfer from an ISPF command line, backed by a small b3270 patch LizTerm carries and applies in every engine build.

**Architecture:** b3270's `Transfer` action blanks the input field before typing `IND$FILE`, so the `TSO` prefix has to be typed by the engine. A unified diff in `native/patches/` adds a `CommandPrefix` keyword; `fetch-source.sh` applies it on every build, and a marker check in every engine gate proves it shipped. In LizTerm, Core gains `TransferHostType.Ispf`, the backend maps it to `host=tso` plus `commandprefix=TSO` and refuses it up front on an engine binary without the marker (an unpatched b3270 silently ignores unknown keywords), and the dialog lists it with its own cursor hint.

**Tech Stack:** .NET 10, C# (`Nullable` and `ImplicitUsings` on), Avalonia 12, CommunityToolkit.Mvvm, xunit.v3 (VSTest mode), bash build scripts, GitHub Actions, x3270 suite 4.5ga6 (C).

**Spec:** `docs/superpowers/specs/2026-09-14-lizterm-ispf-transfer-design.md`

## Global Constraints

- **Licence headers.** Every hand-written `.cs`, `.axaml` and `.sh` file under `src/`, `tests/`, `native/build/` and `tools/` starts with `This file is part of LizTerm.`, `Copyright 2026 by CoffeeMuse`, `SPDX-License-Identifier: BSD-3-Clause` in its comment syntax (after the shebang in a script). `RepositoryHeadersTests` fails the suite otherwise. `.patch` files are not scanned.
- **Dependency rule.** Core never mentions b3270. Only `LizTerm.Backend.B3270` knows b3270's keywords and the patch marker. The App talks to `IEmulatorSession` only.
- **Zero warnings.** CI builds with `-warnaserror`. Before calling anything done: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- **Exact strings (spec §6.4), copy verbatim:**
  - drop-down label: `ISPF (MVS)`
  - ISPF hint: `The cursor must be on an ISPF Command ===> or Option ===> line before you start.`
  - other hint (unchanged): `The cursor must be at a TSO READY prompt or a command line before you start.`
  - unpatched engine: `This engine can't transfer from ISPF: it wasn't built with LizTerm's patch. The engine that ships with LizTerm can.`
  - engine not found, last line: `Reinstalling LizTerm restores it.`
- **Engine names.** Patch file `native/patches/b3270-transfer-commandprefix.patch`; b3270 keyword `CommandPrefix` (LizTerm sends `commandprefix=TSO`); marker string `CommandPrefix`; apply flags `patch -p1 -N -F0 --batch`.
- **Secrets and addresses.** Never print `~/.config/lizterm-test.env`, the test password or the test host's address, and never commit them. A wire log with outbound lines holds the password; it stays outside the repository and is deleted after use.
- **Wally ISPF on MVS/CE.** Never press Clear on a live ISPF panel: the host never answers and the keyboard stays locked (recover with Reset, then Enter). An interrupted live run can leave the user logged on (`USERID IN USE`; recover with `LOGON <user> RECONNECT`).
- **Commits** end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. Work stays on branch `claude/issue-109-discussion-8949fc`.
- **Test commands.** Suite: `dotnet test LizTerm.slnx`. One class: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~FileTransferRequestTests"`.
- **Shell blocks.** Run each fenced `bash` block as one command in one shell: variables such as `P`, and the sourced
  test settings, do not survive between separate tool calls.
- **A fresh worktree has no engine.** Tasks 3 to 6 need none. Task 2 builds the macOS engine (`native/build/build-macos.sh`, several minutes the first time because it compiles OpenSSL), which Tasks 7, 8 and 10 run.

## File Structure

**New**

| File | Responsibility |
|---|---|
| `native/patches/b3270-transfer-commandprefix.patch` | The b3270 change: a `CommandPrefix` keyword typed ahead of `IND$FILE` |
| `.gitattributes` | Keeps patch files LF on every checkout |
| `native/build/shared-verify-patches.sh` | Fails unless a binary contains every patch marker |
| `src/LizTerm.Backend.B3270/Process/EnginePatches.cs` | The marker, the unpatched-engine sentence, and a byte search over an engine file |
| `tests/LizTerm.Backend.B3270.Tests/Process/EnginePatchesTests.cs` | The byte search, and that the marker matches the gate script |
| `tests/LizTerm.Integration.Tests/TsoNavigatorTests.cs` | The leftover-panel rule on sample screens |
| `tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-ispf-roundtrip.jsonl` | Inbound b3270 lines of a real ISPF round trip |

**Modified**

| File | Change |
|---|---|
| `native/build/fetch-source.sh` | Applies `native/patches/*.patch` after extracting |
| `native/build/build-linux-docker.sh`, `build-windows-docker.sh` | `patch` in `PACKAGES` |
| `native/build/verify-macos.sh`, `verify-linux.sh`, `verify-windows.sh`, `verify-bundled-engine.sh` | Call `shared-verify-patches.sh` |
| `.github/workflows/engines.yml` | `native/patches/**` in four cache keys; an unpatched-engine reject case per platform |
| `src/LizTerm.Core/Session/FileTransfer.cs` | `TransferHostType.Ispf`, `TransferHostTypes.IsTso()`, `Validate`, result doc |
| `src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs` | ISPF maps to `host=tso` plus `commandprefix=TSO` |
| `src/LizTerm.Backend.B3270/B3270Session.cs` | Refuses ISPF on an engine file without the marker |
| `src/LizTerm.Backend.B3270/Process/B3270Locator.cs` | Not-found message points at reinstalling |
| `src/LizTerm.App/ViewModels/FileTransferViewModel.cs`, `Views/FileTransferWindow.axaml`, `Views/TransferLabels.cs`, `Files/LocalFileNames.cs` | The ISPF (MVS) entry and `CursorHint` |
| `tests/LizTerm.Integration.Tests/TsoNavigator.cs`, `LiveHostTests.cs` | Start ISPF, wait for a panel, leave a leftover panel; the ISPF live test |
| Docs: `README.md`, `THIRD-PARTY-NOTICES.txt`, `docs/user-guide.md` (+ regenerated `src/LizTerm.App/Assets/Docs/user-guide.html`), `docs/development.md`, `docs/engines.md`, `docs/ci-and-release.md`, root and nested `CLAUDE.md` files, fixtures README | Spec §7 |

---

### Task 1: Carry the b3270 patch and apply it in every build

**Files:**
- Create: `native/patches/b3270-transfer-commandprefix.patch`, `.gitattributes`
- Modify: `native/build/fetch-source.sh` (whole file), `native/build/build-linux-docker.sh:17-20`, `native/build/build-windows-docker.sh:16-23`, `.github/workflows/engines.yml:57,64,252,403`

**Interfaces:**
- Consumes: nothing.
- Produces: every engine built from `fetch-source.sh` accepts `Transfer(...,CommandPrefix=<text>)` and types `<text> ` ahead of `IND$FILE`; its binary contains the string `CommandPrefix`. `native/patches/` exists for Task 2's gate and Task 9's docs.

- [ ] **Step 1: Extract two pristine source trees (before `fetch-source.sh` changes)**

```bash
mkdir -p native/cache
[ -f /Users/robert/ClaudeSandbox/LizTerm/native/cache/suite3270-4.5ga6-src.tgz ] && cp /Users/robert/ClaudeSandbox/LizTerm/native/cache/suite3270-4.5ga6-src.tgz native/cache/
P=native/build-tmp/patchgen
rm -rf "$P" && mkdir -p "$P"
native/build/fetch-source.sh "$P/a-root" > /dev/null
native/build/fetch-source.sh "$P/b-root" > /dev/null
mv "$P/a-root/suite3270-4.5" "$P/a" && mv "$P/b-root/suite3270-4.5" "$P/b" && rmdir "$P/a-root" "$P/b-root"
ls "$P/a/Common/ft.c" "$P/b/include/ft_private.h"
```

The copy is optional (it saves a download); `native/build-tmp` and `native/cache` are gitignored. Expected: both paths listed.

- [ ] **Step 2: Make the change in tree `b`**

```bash
python3 - native/build-tmp/patchgen/b <<'EOF'
import sys
root = sys.argv[1]
def edit(rel, pairs):
    p = f"{root}/{rel}"
    s = open(p, encoding="latin-1").read()
    for old, new in pairs:
        n = s.count(old)
        assert n == 1, f"{rel}: expected 1 match, found {n}: {old[:50]!r}"
        s = s.replace(old, new)
    open(p, "w", encoding="latin-1").write(s)
edit("include/ft_private.h", [
    ("    char *other_options;\n", "    char *other_options;\n    char *command_prefix;\n"),
])
edit("Common/ft.c", [
    ("    PARM_OTHER_OPTIONS,\n    N_PARMS\n", "    PARM_OTHER_OPTIONS,\n    PARM_COMMAND_PREFIX,\n    N_PARMS\n"),
    ('    { "OtherOptions" },\n};', '    { "OtherOptions" },\n    { "CommandPrefix" },\n};'),
    ("    Replace(p->other_options, NULL);\n", "    Replace(p->other_options, NULL);\n    Replace(p->command_prefix, NULL);\n"),
    ("    if (tp[PARM_OTHER_OPTIONS].value) {\n\tReplace(p->other_options, NewString(tp[PARM_OTHER_OPTIONS].value));\n    }\n",
     "    if (tp[PARM_OTHER_OPTIONS].value) {\n\tReplace(p->other_options, NewString(tp[PARM_OTHER_OPTIONS].value));\n    }\n    if (tp[PARM_COMMAND_PREFIX].value) {\n\tReplace(p->command_prefix, NewString(tp[PARM_COMMAND_PREFIX].value));\n    }\n"),
    ('    vb_init(&r);\n    vb_appendf(&r, "IND', '    vb_init(&r);\n    if (p->command_prefix != NULL && p->command_prefix[0]) {\n\tvb_appendf(&r, "%s ", p->command_prefix);\n    }\n    vb_appendf(&r, "IND'),
])
print("edits applied")
EOF
```

Expected: `edits applied`. The six edits are spec §5.2: the header field; the enum entry and table entry after `OtherOptions`; clearing it in `ft_init_conf`; copying it in `parse_ft_keywords`; typing it ahead of `IND$FILE` in the command builder.

- [ ] **Step 3: Write the patch file**

```bash
mkdir -p native/patches
{
cat <<'PRE'
LizTerm patch for b3270 (x3270 suite 4.5ga6): a CommandPrefix keyword for the Transfer action.

Transfer(...,CommandPrefix=<text>) types <text> and one space ahead of the IND$FILE command it builds.
LizTerm's ISPF (MVS) host type sends CommandPrefix=TSO, so a transfer can start from an ISPF command
line: ISPF hands "TSO IND$FILE ..." to TSO, which runs IND$FILE as it would at READY. Typing TSO at the
cursor before calling Transfer cannot work, because Transfer blanks the input field (kybd_prime) before
it types the command.

Unset or empty, the engine behaves exactly as upstream. Only what LizTerm's engine uses is changed: no
Transfer help text, interactive prompt, X resource or Motif dialog field.

LizTerm issue #109. Applied by native/build/fetch-source.sh; see docs/engines.md, "Patches".

PRE
( cd native/build-tmp/patchgen && diff -u a/include/ft_private.h b/include/ft_private.h; diff -u a/Common/ft.c b/Common/ft.c ) \
  | sed -E 's/^(---|\+\+\+) ([^[:space:]]+)[[:space:]].*$/\1 \2/'
} > native/patches/b3270-transfer-commandprefix.patch
shasum -a 256 native/patches/b3270-transfer-commandprefix.patch
grep -c '^@@' native/patches/b3270-transfer-commandprefix.patch
```

Expected: SHA-256 `d3b7a1ab62ad1a18625626c8604e22c985e3e93660686a7cfb2276ba81727144` (the file generated and verified while planning), and `6` hunks. A different hash means the preamble or the edits differ; compare with Step 2 before going on. The `sed` drops the timestamps `diff` puts on the `---`/`+++` lines, so regenerating the file does not change it.

- [ ] **Step 4: Prove the patch applies to a fresh tree with the build's flags**

```bash
C=native/build-tmp/patchgen/check
rm -rf "$C" && mkdir -p "$C" && tar xzf native/cache/suite3270-4.5ga6-src.tgz -C "$C"
patch -p1 -N -F0 --batch -d "$C/suite3270-4.5" < native/patches/b3270-transfer-commandprefix.patch; echo "rc $?"
grep -c '{ "CommandPrefix" }' "$C/suite3270-4.5/Common/ft.c"
```

Expected: `patching file 'include/ft_private.h'`, `patching file 'Common/ft.c'`, `rc 0`, `1`.

- [ ] **Step 5: Create `.gitattributes`**

```
# The b3270 patches keep LF line endings on every checkout: the x3270 sources they apply to are LF, and
# native/build/fetch-source.sh applies them with no fuzz.
native/patches/*.patch text eol=lf
```

- [ ] **Step 6: Replace `native/build/fetch-source.sh`**

```bash
#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Downloads and verifies the pinned x3270 source tarball, extracts it into $1, applies LizTerm's patches from
# native/patches, and prints the source directory.
# The pin is this file; shared-fetch-tarball.sh is the machinery.
set -euo pipefail
DEST=${1:?usage: fetch-source.sh <dest-dir>}
VERSION=4.5ga6
SHA256=06faf5ce883852258cc6a2a4da9fe5ce023e97d01e50625ff36f4a01ea703468
URL="https://downloads.sourceforge.net/project/x3270/x3270/$VERSION/suite3270-$VERSION-src.tgz"
# The tarball extracts to suite3270-4.5 (major.minor only), not to its own name.
SRC=$("$(dirname "$0")/shared-fetch-tarball.sh" "$URL" "$SHA256" "$DEST" "suite3270-4.5")

# shared-fetch-tarball.sh starts from a fresh tree every time, so each patch applies exactly once. Name order, in
# the C locale so it cannot vary by machine. -F0 allows no fuzz: a version bump that moves the patched code fails
# here instead of producing an engine without the patch. -N and --batch mean patch never stops to ask. The callers
# read this script's stdout as the source directory, so patch's own output goes to stderr. See docs/engines.md,
# "Patches".
LC_ALL=C
shopt -s nullglob
for PATCH in "$(dirname "$0")"/../patches/*.patch; do
  echo "Applying $(basename "$PATCH")" >&2
  patch -p1 -N -F0 --batch -d "$SRC" < "$PATCH" >&2
done
echo "$SRC"
```

- [ ] **Step 7: Prove the script applies the patch and prints only the directory**

```bash
OUT=$(native/build/fetch-source.sh native/build-tmp/patchgen/fetched)
[ "$OUT" = "native/build-tmp/patchgen/fetched/suite3270-4.5" ] && echo "stdout is the directory"
grep -c '{ "CommandPrefix" }' "$OUT/Common/ft.c"
```

Expected: `Applying b3270-transfer-commandprefix.patch` and the two `patching file` lines on stderr, then `stdout is the directory` and `1`.

- [ ] **Step 8: Prove a patch that no longer applies fails the build**

```bash
T=native/build-tmp/patchgen/broken
rm -rf "$T" && mkdir -p "$T/native/patches"
cp -R native/build "$T/native/build"
ln -s "$PWD/native/cache" "$T/native/cache"
sed 's/^ \{5\}PARM_OTHER_OPTIONS,$/     PARM_OTHER_OPTIONZ,/' native/patches/b3270-transfer-commandprefix.patch > "$T/native/patches/broken.patch"
grep -c PARM_OTHER_OPTIONZ "$T/native/patches/broken.patch"
if "$T/native/build/fetch-source.sh" "$T/src" > /dev/null 2> "$T/stderr"; then echo "FAILED: a patch that does not apply was accepted"; else echo "OK: rejected"; fi
grep -i "hunk" "$T/stderr"
```

Expected: `1` (the context line was altered), `OK: rejected`, and a `Hunk #1 FAILED` line.

- [ ] **Step 9: Install `patch` in the Linux and Windows build images**

In `native/build/build-linux-docker.sh`, replace

```bash
# build generates several C sources with Python scripts.
PACKAGES="gcc make perl-core diffutils findutils tar binutils python3"
```

with

```bash
# build generates several C sources with Python scripts. patch is fetch-source.sh's: it applies native/patches.
PACKAGES="gcc make perl-core diffutils findutils tar binutils python3 patch"
```

In `native/build/build-windows-docker.sh`, replace

```bash
# several C sources with Python scripts.
PACKAGES="gcc-mingw-w64-x86-64 binutils-mingw-w64-x86-64 make python3 curl ca-certificates"
```

with

```bash
# several C sources with Python scripts. patch is fetch-source.sh's: it applies native/patches.
PACKAGES="gcc-mingw-w64-x86-64 binutils-mingw-w64-x86-64 make python3 curl ca-certificates patch"
```

The image tag is a hash of the package list, so both images rebuild on their own.

- [ ] **Step 10: Add `native/patches/**` to the four engine output cache keys**

In `.github/workflows/engines.yml`, append `, 'native/patches/**'` inside each of these four `hashFiles(...)` calls, so they read:

```yaml
          key: b3270-osx-arm64-${{ hashFiles('native/build/*macos*.sh', 'native/build/fetch-source.sh', 'native/build/fetch-openssl.sh', 'native/build/shared-*.sh', 'native/patches/**') }}
```
```yaml
          key: b3270-osx-x64-${{ hashFiles('native/build/*macos*.sh', 'native/build/fetch-source.sh', 'native/build/fetch-openssl.sh', 'native/build/shared-*.sh', 'native/patches/**') }}
```
```yaml
          key: b3270-${{ matrix.rid }}-${{ hashFiles('native/build/*linux*.sh', 'native/build/fetch-*.sh', 'native/build/shared-*.sh', 'native/patches/**') }}
```
```yaml
          key: b3270-win-x64-${{ hashFiles('native/build/*windows*.sh', 'native/build/fetch-source.sh', 'native/build/shared-*.sh', 'native/patches/**') }}
```

Leave the source-tarball keys (`sources-macos-`, `sources-`, `x3270-src-`) alone: they cache downloads only.

Run: `grep -c "native/patches/\*\*" .github/workflows/engines.yml`
Expected: `4`

- [ ] **Step 11: Commit**

```bash
git add native/patches/b3270-transfer-commandprefix.patch .gitattributes native/build/fetch-source.sh native/build/build-linux-docker.sh native/build/build-windows-docker.sh .github/workflows/engines.yml
git commit -m "Carry a b3270 patch adding a CommandPrefix Transfer keyword

Applied by fetch-source.sh to every engine build, with patch installed in
the Linux and Windows build images and native/patches in the engine cache
keys. Issue #109.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 2: Gate every engine on the patch marker

**Files:**
- Create: `native/build/shared-verify-patches.sh`
- Modify: `native/build/verify-macos.sh` (last lines), `native/build/verify-linux.sh` (last lines), `native/build/verify-windows.sh` (last lines), `native/build/verify-bundled-engine.sh` (last lines), `.github/workflows/engines.yml` (the three gate steps)

**Interfaces:**
- Consumes: Task 1's patched build (`CommandPrefix` in the binary).
- Produces: `native/build/shared-verify-patches.sh <binary>` exits 0 printing `OK: <binary> carries LizTerm's patches (CommandPrefix)`, or exits 1 printing `ERROR: <binary> does not carry LizTerm's patch marker 'CommandPrefix'; it was built without native/patches applied.` Its marker list is the line `MARKERS=(CommandPrefix)`, which Task 5's `EnginePatchesTests` reads. A built `native/out/osx-arm64/b3270` for Tasks 7, 8 and 10.

- [ ] **Step 1: Write `native/build/shared-verify-patches.sh`**

```bash
#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails unless the binary carries every patch in native/patches, each recognised by its marker: a string only the
# patched source puts into the binary. A new patch adds its marker to MARKERS. The backend reads the same marker at
# run time (src/LizTerm.Backend.B3270/Process/EnginePatches.cs), and a test holds the two to the same name.
#
# It reads the file's bytes and never runs it, so it covers the arm64 and Windows engines a runner cannot execute.
# grep reads the file itself rather than a pipe: grep -q exits on its first match, and a writer killed by that
# SIGPIPE would fail the script under pipefail.
set -euo pipefail
BIN=${1:?usage: shared-verify-patches.sh <binary>}
[ -f "$BIN" ] || { echo "ERROR: $BIN does not exist" >&2; exit 1; }
# b3270-transfer-commandprefix.patch: the Transfer action's new keyword. A stock 4.5ga6 b3270 contains
# OtherOptions twice and CommandPrefix not at all.
MARKERS=(CommandPrefix)
for MARKER in "${MARKERS[@]}"; do
  if ! LC_ALL=C grep -a -q -e "$MARKER" "$BIN"; then
    echo "ERROR: $BIN does not carry LizTerm's patch marker '$MARKER'; it was built without native/patches applied." >&2
    exit 1
  fi
done
echo "OK: $BIN carries LizTerm's patches (${MARKERS[*]})"
```

Run: `chmod +x native/build/shared-verify-patches.sh`

- [ ] **Step 2: Prove it rejects an engine without the patch**

```bash
STOCK=$(command -v b3270 || echo /bin/ls)
if native/build/shared-verify-patches.sh "$STOCK"; then echo "FAILED: accepted $STOCK"; else echo "OK: rejected $STOCK"; fi
```

Expected: `ERROR: ... does not carry LizTerm's patch marker 'CommandPrefix'; ...` then `OK: rejected ...`. (A Homebrew b3270 is a stock 4.5ga6; `/bin/ls` stands in when there is none.)

- [ ] **Step 3: Call it from the three platform gates, as their last arm**

In `native/build/verify-macos.sh`, replace

```bash
BANNER=$("$(dirname "$0")/shared-verify-tls.sh" "$BIN")
echo "OK: $BIN is $ACTUAL, links only system libraries and has TLS"
```

with

```bash
BANNER=$("$(dirname "$0")/shared-verify-tls.sh" "$BIN")
# LizTerm's patches (native/patches) must be in the binary. Last, so every arm above keeps the reject fixture it was
# built for: engines.yml proves this one with a copy of the real engine whose marker has been defaced.
"$(dirname "$0")/shared-verify-patches.sh" "$BIN"
echo "OK: $BIN is $ACTUAL, links only system libraries, has TLS and carries LizTerm's patches"
```

In `native/build/verify-linux.sh`, replace

```bash
BANNER=$("$(dirname "$0")/shared-verify-tls.sh" "$BIN")

echo "OK: $BIN links only the glibc runtime, needs no more than glibc $HIGHEST (floor $FLOOR), and has TLS"
```

with

```bash
BANNER=$("$(dirname "$0")/shared-verify-tls.sh" "$BIN")

# 4. The engine must carry LizTerm's patches (native/patches). Last, so every arm above keeps the reject fixture it
# was built for: engines.yml proves this one with a copy of the real engine whose marker has been defaced.
"$(dirname "$0")/shared-verify-patches.sh" "$BIN"

echo "OK: $BIN links only the glibc runtime, needs no more than glibc $HIGHEST (floor $FLOOR), has TLS, and carries LizTerm's patches"
```

In `native/build/verify-windows.sh`, replace

```bash
echo "OK: $BIN is a 64-bit PE importing only Windows system DLLs, Schannel included"
```

with

```bash
# 4. The engine must carry LizTerm's patches (native/patches). Last, so every arm above keeps the reject fixture it
# was built for: engines.yml proves this one with a copy of the real engine whose marker has been defaced.
"$(dirname "$0")/shared-verify-patches.sh" "$BIN"

echo "OK: $BIN is a 64-bit PE importing only Windows system DLLs, Schannel included, carrying LizTerm's patches"
```

- [ ] **Step 4: Call it from the packaged-archive gate**

In `native/build/verify-bundled-engine.sh`, replace

```bash
if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "ERROR: $BIN is $ACTUAL but $RID needs $EXPECTED" >&2
  exit 1
fi
echo "OK: $BIN is $ACTUAL, correct for $RID"
```

with

```bash
if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "ERROR: $BIN is $ACTUAL but $RID needs $EXPECTED" >&2
  exit 1
fi

# The engine inside the archive must still be the patched one. engines.yml's gates check each engine as it is
# built; this checks the copy every archive carries, on all six RIDs, win-arm64 included.
"$(dirname "$0")/shared-verify-patches.sh" "$BIN"
echo "OK: $BIN is $ACTUAL, correct for $RID, and carries LizTerm's patches"
```

- [ ] **Step 5: Build the macOS engine; its own gate must pass**

Run (several minutes; the first run compiles OpenSSL into `native/build-tmp`): `native/build/build-macos.sh`

Expected, at the end: `OK: native/out/osx-arm64/b3270 carries LizTerm's patches (CommandPrefix)` followed by `OK: native/out/osx-arm64/b3270 is arm64, links only system libraries, has TLS and carries LizTerm's patches`. A failure writes its log under `native/build-tmp/osx-arm64/` (`configure.log`, `make.log`).

- [ ] **Step 6: Prove the macOS gate rejects an unpatched copy by the patch arm (the CI reject case, locally)**

```bash
FIX=native/build-tmp/fixtures/arm64 && mkdir -p "$FIX"
perl -0777 -pe 's/CommandPrefix/CommandPrefiX/g' native/out/osx-arm64/b3270 > "$FIX/unpatched"
chmod +x "$FIX/unpatched" && codesign -f -s - "$FIX/unpatched"
if out=$(native/build/verify-macos.sh "$FIX/unpatched" arm64 2>&1); then echo "FAILED: accepted"; fi
case "$out" in *"does not carry LizTerm"*) echo "OK: rejected by the patch marker arm" ;; *) printf '%s\n' "$out"; echo "FAILED: wrong arm" ;; esac
```

Expected: `OK: rejected by the patch marker arm`.

- [ ] **Step 7: Prove the packaged-archive gate accepts the engine and rejects the copy**

```bash
PUB=native/build-tmp/pubcheck
rm -rf "$PUB" && mkdir -p "$PUB/good/runtimes/osx-arm64/native" "$PUB/bad/runtimes/osx-arm64/native"
cp native/out/osx-arm64/b3270 "$PUB/good/runtimes/osx-arm64/native/b3270"
cp native/build-tmp/fixtures/arm64/unpatched "$PUB/bad/runtimes/osx-arm64/native/b3270"
native/build/verify-bundled-engine.sh "$PUB/good" osx-arm64
if native/build/verify-bundled-engine.sh "$PUB/bad" osx-arm64; then echo "FAILED: accepted"; else echo "OK: rejected"; fi
```

Expected: the `good` run ends `... correct for osx-arm64, and carries LizTerm's patches`; the `bad` run prints the marker error and `OK: rejected`.

- [ ] **Step 8: Add a reject case to the macOS gate step in `engines.yml`**

Rename the step `The gate accepts each engine and rejects both fixtures for each architecture` to `The gate accepts each engine and rejects every fixture for each architecture`. Then replace

```yaml
            cc -arch "$ARCH" -o "$FIX/dirty" "$FIX/uses-lib.c" -L"$FIX" -lfixture

            echo "--- the gate must accept the engine it is gating"
            native/build/verify-macos.sh "native/out/$RID/b3270" "$ARCH"

            reject "dependency allowlist" "$FIX/dirty" "non-system dynamic dependencies"
            reject "TLS provider check"   "$FIX/clean" "does not report the expected TLS provider"
```

with

```yaml
            cc -arch "$ARCH" -o "$FIX/dirty" "$FIX/uses-lib.c" -L"$FIX" -lfixture
            # unpatched: the real engine with its patch marker defaced -- clears every other arm, dies at the patch
            # marker. Re-signed ad hoc: an arm64 binary whose signature no longer matches its bytes is killed when
            # the TLS arm runs it, and would be rejected by the wrong arm.
            perl -0777 -pe 's/CommandPrefix/CommandPrefiX/g' "native/out/$RID/b3270" > "$FIX/unpatched"
            chmod +x "$FIX/unpatched"
            codesign -f -s - "$FIX/unpatched"

            echo "--- the gate must accept the engine it is gating"
            native/build/verify-macos.sh "native/out/$RID/b3270" "$ARCH"

            reject "dependency allowlist" "$FIX/dirty"     "non-system dynamic dependencies"
            reject "TLS provider check"   "$FIX/clean"     "does not report the expected TLS provider"
            reject "patch marker check"   "$FIX/unpatched" "does not carry LizTerm"
```

- [ ] **Step 9: Add a reject case to the Linux gate step**

The step's script runs inside `bash -c '...'`, so nothing added may contain a single quote. In the comment above the step, replace `three runs of the gate: one accept,` / `# two rejects, instead of three invocations` with `four runs of the gate: one accept,` / `# three rejects, instead of four invocations`, and rename the step `The gate accepts the engine and rejects both fixtures` to `The gate accepts the engine and rejects every fixture`. Then replace

```yaml
          reject "dependency allowlist" /usr/bin/bash "dynamic dependencies outside the glibc runtime"
          reject "glibc symbol floor" native/build-tmp/newglibc-fixture "above the 2.28 floor"
          '
```

with

```yaml
          reject "dependency allowlist" /usr/bin/bash "dynamic dependencies outside the glibc runtime"
          reject "glibc symbol floor" native/build-tmp/newglibc-fixture "above the 2.28 floor"

          # The real engine with its patch marker defaced: it clears every other arm and dies at the patch marker.
          perl -0777 -pe "s/CommandPrefix/CommandPrefiX/g" native/out/${{ matrix.rid }}/b3270 > native/build-tmp/unpatched-fixture
          chmod +x native/build-tmp/unpatched-fixture
          reject "patch marker check" native/build-tmp/unpatched-fixture "does not carry LizTerm"
          '
```

- [ ] **Step 10: Add a reject case to the Windows gate step**

Also inside `bash -c '...'`. Rename the step `The gate accepts the engine and rejects a non-system import` to `The gate accepts the engine and rejects a non-system import and an unpatched engine`. Then replace

```yaml
            case "$OUT" in
              *"imports DLLs outside the Windows system set"*) echo "OK: the import arm rejected its fixture" ;;
              *) echo "the fixture was rejected, but not by the import arm:"; printf "%s\n" "$OUT"; exit 1 ;;
            esac
          '
```

with

```yaml
            case "$OUT" in
              *"imports DLLs outside the Windows system set"*) echo "OK: the import arm rejected its fixture" ;;
              *) echo "the fixture was rejected, but not by the import arm:"; printf "%s\n" "$OUT"; exit 1 ;;
            esac
            # The real engine with its patch marker defaced: it clears every other arm and dies at the patch marker.
            # debian:12-slim ships perl as an essential package.
            perl -0777 -pe "s/CommandPrefix/CommandPrefiX/g" native/out/win-x64/b3270.exe > native/build-tmp/fixtures/unpatched.exe
            if OUT=$(native/build/verify-windows.sh native/build-tmp/fixtures/unpatched.exe 2>&1); then
              echo "the gate ACCEPTED an engine without the patch marker"; exit 1
            fi
            case "$OUT" in
              *"does not carry LizTerm"*) echo "OK: the patch marker arm rejected its fixture" ;;
              *) echo "the fixture was rejected, but not by the patch marker arm:"; printf "%s\n" "$OUT"; exit 1 ;;
            esac
          '
```

Run: `grep -rn "rejects both fixtures\|rejects a non-system import" docs .github | grep -v "and an unpatched engine"`
Expected: no output. If a doc quotes an old step name, update it to the new one.

- [ ] **Step 11: Rebuild the .NET projects so the engine is copied, and run the engine and header tests**

```bash
dotnet build LizTerm.slnx
dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~EngineSmokeTests"
dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"
```

Expected: the smoke test **passes** (not skipped: the bundled engine now exists), and the header tests pass with the new script included.

- [ ] **Step 12: Commit**

```bash
git add native/build/shared-verify-patches.sh native/build/verify-macos.sh native/build/verify-linux.sh native/build/verify-windows.sh native/build/verify-bundled-engine.sh .github/workflows/engines.yml
git commit -m "Gate every engine on LizTerm's patch marker

shared-verify-patches.sh is the last arm of the macOS, Linux and Windows
gates and runs on every packaged archive; each engines.yml gate step
rejects a copy of the built engine with the marker defaced.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 3: Core — the ISPF host type

**Files:**
- Modify: `src/LizTerm.Core/Session/FileTransfer.cs:9` (enum), `:62` (`Validate`), `:71-73` (result doc)
- Test: `tests/LizTerm.Core.Tests/Session/FileTransferRequestTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public enum TransferHostType { Tso, Vm, Cics, Ispf }` and `public static class TransferHostTypes { public static bool IsTso(this TransferHostType type); }` in namespace `LizTerm.Core.Session`. Tasks 4 to 7 use both.

- [ ] **Step 1: Write the failing tests**

Add to `FileTransferRequestTests`, after `Allocation_rules_apply_only_when_sending_to_tso`:

```csharp
    [Theory]
    [InlineData(TransferHostType.Tso, true)]
    [InlineData(TransferHostType.Ispf, true)]
    [InlineData(TransferHostType.Vm, false)]
    [InlineData(TransferHostType.Cics, false)]
    public void Tso_and_ispf_run_under_tso(TransferHostType type, bool expected) =>
        Assert.Equal(expected, type.IsTso());

    [Fact]
    public void An_ispf_send_follows_the_tso_allocation_rules()
    {
        var r = Send() with { HostType = TransferHostType.Ispf, AllocationUnits = AllocationUnits.AvBlock };
        Assert.Equal("Primary space is required when allocation units are set.", r.Validate());
        Assert.Equal("Average block size is required for AVBLOCK allocation.", (r with { PrimarySpace = 5 }).Validate());
        Assert.Null((r with { PrimarySpace = 5, AverageBlock = 4096 }).Validate());
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~FileTransferRequestTests"`
Expected: build error `CS0117` (`TransferHostType` has no `Ispf`) and no `IsTso`.

- [ ] **Step 3: Implement**

In `FileTransfer.cs`, replace `public enum TransferHostType { Tso, Vm, Cics }` with:

```csharp
/// <summary>The host a transfer talks to. <see cref="Ispf"/> is TSO reached from an ISPF command line: the command
/// goes to TSO through ISPF, so everything TSO allows applies. It is last so the other members keep their values.</summary>
public enum TransferHostType { Tso, Vm, Cics, Ispf }

public static class TransferHostTypes
{
    /// <summary>True where IND$FILE runs under TSO: TSO itself, and ISPF, which hands the command to TSO. The one
    /// place that says so, so no caller spells out both members.</summary>
    public static bool IsTso(this TransferHostType type) => type is TransferHostType.Tso or TransferHostType.Ispf;
}
```

In `Validate`, replace

```csharp
        var tsoSend = Direction == TransferDirection.Send && HostType == TransferHostType.Tso;
```

with

```csharp
        var tsoSend = Direction == TransferDirection.Send && HostType.IsTso();
```

Replace the `FileTransferResult` doc comment

```csharp
/// <summary>Outcome of one transfer. Message is the engine's or host's final text, unaltered, on success and on
/// failure (a cancel included). The byte count travels through the progress callback only.</summary>
```

with

```csharp
/// <summary>Outcome of one transfer. Message is the engine's or host's final text, unaltered, on success and on
/// failure (a cancel included), with one exception: a backend may fail a request the engine cannot perform, in its
/// own words, without sending it. The byte count travels through the progress callback only.</summary>
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~FileTransferRequestTests"`
Expected: PASS.

- [ ] **Step 5: Build the solution (no other project may break)**

Run: `dotnet build LizTerm.slnx 2>&1 | grep -E " error | warning " | head`
Expected: no output. (`TransferLabels` and `TransferMapper` have discard arms; nothing else switches over the enum.)

- [ ] **Step 6: Commit**

```bash
git add src/LizTerm.Core/Session/FileTransfer.cs tests/LizTerm.Core.Tests/Session/FileTransferRequestTests.cs
git commit -m "Add an ISPF host type that runs under TSO

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 4: Backend — map ISPF onto the patched Transfer

**Files:**
- Modify: `src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs`
- Test: `tests/LizTerm.Backend.B3270.Tests/Protocol/TransferMapperTests.cs`

**Interfaces:**
- Consumes: `TransferHostType.Ispf`, `TransferHostTypes.IsTso()` (Task 3).
- Produces: for an `Ispf` request, `TransferMapper.ToAction` returns args `direction=…`, `hostfile=…`, `localfile=…`, `host=tso`, `commandprefix=TSO`, `mode=…`, then exactly what a TSO request of the same shape gets. Task 5's session test expects the JSON fragment `"host=tso","commandprefix=TSO","mode=ascii"`.

- [ ] **Step 1: Write the failing tests**

Add to `TransferMapperTests`:

```csharp
    [Fact]
    public void Ispf_is_tso_with_the_command_prefix() =>
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=tso", "commandprefix=TSO", "mode=ascii", "cr=remove", "remap=yes"], Args(Send(TransferHostType.Ispf)));

    [Fact]
    public void An_ispf_receive_carries_the_prefix_too() =>
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "commandprefix=TSO", "mode=ascii", "cr=add", "remap=yes", "exist=replace"], Args(Receive(TransferHostType.Ispf)));

    [Fact]
    public void An_ispf_send_keeps_every_tso_only_keyword()
    {
        var r = Send(TransferHostType.Ispf) with { RecordFormat = RecordFormat.Undefined, Lrecl = 80, Blksize = 3120, AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5, SecondarySpace = 1, AverageBlock = 4096 };
        Assert.Equal(["recfm=undefined", "lrecl=80", "blksize=3120", "allocation=avblock", "primaryspace=5", "secondaryspace=1", "avblock=4096"], Args(r)[8..]);
    }

    [Theory]
    [InlineData(TransferHostType.Tso)]
    [InlineData(TransferHostType.Vm)]
    [InlineData(TransferHostType.Cics)]
    public void Only_ispf_sends_a_command_prefix(TransferHostType host)
    {
        Assert.DoesNotContain(Args(Send(host)), a => a.StartsWith("commandprefix=", StringComparison.Ordinal));
        Assert.DoesNotContain(Args(Receive(host)), a => a.StartsWith("commandprefix=", StringComparison.Ordinal));
    }
```

And add a row to `Every_host_type_has_a_keyword`:

```csharp
    [InlineData(TransferHostType.Ispf, "host=tso")]
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~TransferMapperTests"`
Expected: FAIL — ISPF currently maps to `host=cics` (the discard arm) with no prefix and no TSO keywords.

- [ ] **Step 3: Implement**

In `TransferMapper.cs`, replace the last line of the class doc comment

```csharp
/// stays honest. Values are raw strings; RunOperation quotes them as JSON, so spaces need no escaping.</summary>
```

with

```csharp
/// stays honest. Values are raw strings; RunOperation quotes them as JSON, so spaces need no escaping. ISPF is TSO
/// with <c>commandprefix=TSO</c>, a keyword only LizTerm's patched engine knows (native/patches).</summary>
```

Replace the start of `ToAction`, from `var tso = request.HostType == TransferHostType.Tso;` through the `args` initializer, with:

```csharp
        var tso = request.HostType.IsTso();
        var vm = request.HostType == TransferHostType.Vm;

        var args = new List<string>
        {
            "direction=" + (send ? "send" : "receive"),
            "hostfile=" + request.HostFile,
            "localfile=" + request.LocalPath,
            "host=" + HostKeyword(request.HostType),
        };
        // LizTerm's b3270 patch types this, and a space, ahead of IND$FILE; TSO makes ISPF hand the command to TSO.
        if (request.HostType == TransferHostType.Ispf) args.Add("commandprefix=TSO");
        args.Add("mode=" + (text ? "ascii" : "binary"));
```

Replace `HostKeyword` with:

```csharp
    private static string HostKeyword(TransferHostType type) => type switch
    {
        TransferHostType.Tso or TransferHostType.Ispf => "tso",
        TransferHostType.Vm => "vm",
        _ => "cics",
    };
```

Everything after the `mode=` line stays as it is; it already branches on `tso`, which now covers ISPF.

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~TransferMapperTests"`
Expected: PASS, the existing TSO/VM/CICS tests included.

- [ ] **Step 5: Commit**

```bash
git add src/LizTerm.Backend.B3270/Protocol/TransferMapper.cs tests/LizTerm.Backend.B3270.Tests/Protocol/TransferMapperTests.cs
git commit -m "Map ISPF transfers to host=tso with commandprefix=TSO

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 5: Backend — refuse ISPF on an engine without the patch, and point users at reinstalling

**Files:**
- Create: `src/LizTerm.Backend.B3270/Process/EnginePatches.cs`
- Modify: `src/LizTerm.Backend.B3270/B3270Session.cs` (a field after `_location`, the constructor, `TransferAsync`), `src/LizTerm.Backend.B3270/Process/B3270Locator.cs:60-62`
- Test: `tests/LizTerm.Backend.B3270.Tests/Process/EnginePatchesTests.cs` (new), `tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs`, `tests/LizTerm.Backend.B3270.Tests/Process/B3270LocatorTests.cs`

**Interfaces:**
- Consumes: `TransferHostType.Ispf` (Task 3); the ISPF mapping (Task 4); `MARKERS=(CommandPrefix)` in `native/build/shared-verify-patches.sh` (Task 2).
- Produces: `internal static class EnginePatches` in `LizTerm.Backend.B3270.Process` with `const string CommandPrefixMarker = "CommandPrefix"`, `const string MissingCommandPrefixMessage` (spec §6.4 sentence) and `static bool Carries(string path, string marker)`. `B3270Session.TransferAsync` returns `new FileTransferResult(false, EnginePatches.MissingCommandPrefixMessage)` for an ISPF request when the engine file lacks the marker, sending nothing.

**Why:** b3270 4.5ga6 silently ignores a `Transfer` keyword it does not know (its "Unknown option" check sits inside the keyword loop and never fires), so an unpatched engine would drop `commandprefix`, type a bare `IND$FILE` into the ISPF command line and time out after 30 s. Spec §2 and §4.5.

- [ ] **Step 1: Write the failing `EnginePatchesTests`**

Create `tests/LizTerm.Backend.B3270.Tests/Process/EnginePatchesTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.CompilerServices;
using System.Text;
using LizTerm.Backend.B3270.Process;

namespace LizTerm.Backend.B3270.Tests.Process;

public class EnginePatchesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "lizterm-patches-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Finds_the_marker_among_other_bytes()
    {
        File.WriteAllBytes(_path, [0x00, 0xff, .. Encoding.ASCII.GetBytes("OtherOptions"), 0x00, .. Encoding.ASCII.GetBytes(EnginePatches.CommandPrefixMarker), 0x00, 0x7f]);
        Assert.True(EnginePatches.Carries(_path, EnginePatches.CommandPrefixMarker));
    }

    [Fact]
    public void Does_not_find_a_marker_that_is_absent_or_only_partly_there()
    {
        File.WriteAllBytes(_path, [0x00, .. Encoding.ASCII.GetBytes("OtherOptions\0CommandPrefiX\0CommandPrefi")]);
        Assert.False(EnginePatches.Carries(_path, EnginePatches.CommandPrefixMarker));
    }

    /// <summary>The build gate and the backend must look for the same string, or a gated engine could still be refused.</summary>
    [Fact]
    public void The_marker_is_the_one_the_build_gate_checks()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "native", "build", "shared-verify-patches.sh"));
        Assert.Contains($"MARKERS=({EnginePatches.CommandPrefixMarker})", script);
    }

    /// <summary>This file sits at tests/LizTerm.Backend.B3270.Tests/Process/, three levels below the root.</summary>
    private static string RepositoryRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", ".."));
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~EnginePatchesTests"`
Expected: build error — `EnginePatches` does not exist.

- [ ] **Step 3: Create `src/LizTerm.Backend.B3270/Process/EnginePatches.cs`**

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Backend.B3270.Process;

/// <summary>What LizTerm's own b3270 patches (<c>native/patches</c>) add, and how to tell whether an engine binary
/// carries them. The marker is the string <c>native/build/shared-verify-patches.sh</c> checks every built engine for;
/// <c>EnginePatchesTests</c> holds the two to the same name.</summary>
internal static class EnginePatches
{
    /// <summary>The Transfer keyword <c>b3270-transfer-commandprefix.patch</c> adds. A stock 4.5ga6 b3270 does not
    /// contain the string at all.</summary>
    public const string CommandPrefixMarker = "CommandPrefix";

    /// <summary>What an ISPF transfer fails with on an engine without that patch. Such an engine cannot be relied on to
    /// refuse the keyword: 4.5ga6 silently ignores a Transfer keyword it does not know, so it would type a bare
    /// IND$FILE into the ISPF command line and time out.</summary>
    public const string MissingCommandPrefixMessage =
        "This engine can't transfer from ISPF: it wasn't built with LizTerm's patch. The engine that ships with LizTerm can.";

    /// <summary>Whether the file's bytes contain the marker's ASCII bytes. Throws whatever reading the file throws.</summary>
    public static bool Carries(string path, string marker)
    {
        ReadOnlySpan<byte> needle = Encoding.ASCII.GetBytes(marker);
        return File.ReadAllBytes(path).AsSpan().IndexOf(needle) >= 0;
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~EnginePatchesTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Write the failing session tests**

In `B3270SessionTransferTests.cs`, add `using System.Text;` and `using LizTerm.Backend.B3270.Process;` to the usings. Change the `StartAsync` helper's signature and session line to take an optional location:

```csharp
    private static async Task<(B3270Session Session, FakeB3270Process Fake)> StartAsync(B3270Location? location = null)
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => IsTransferStart(line) ? Array.Empty<string>() : new[] { AutoResult(line) };
        var session = new B3270Session(Profile, () => fake, location: location);
        await session.StartProcessAsync(CancellationToken.None);
        return (session, fake);
    }
```

Add a helper and three tests:

```csharp
    /// <summary>A stand-in engine binary on disk: arbitrary bytes, with or without the patch marker among them.</summary>
    private static B3270Location EngineFile(bool patched)
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-engine-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, [0x7f, 0x45, 0x4c, 0x46, .. Encoding.ASCII.GetBytes(patched ? "OtherOptions\0CommandPrefix\0" : "OtherOptions\0")]);
        return new B3270Location(path, EngineSource.Override);
    }

    [Fact]
    public async Task An_ispf_transfer_on_an_engine_without_the_patch_fails_at_once_and_sends_nothing()
    {
        var engine = EngineFile(patched: false);
        try
        {
            var (session, fake) = await StartAsync(engine);
            var result = await session.TransferAsync(Request with { HostType = TransferHostType.Ispf }, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(WaitTime, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Equal(EnginePatches.MissingCommandPrefixMessage, result.Message);
            Assert.DoesNotContain(fake.InputLines, IsTransferStart);
            Assert.False(session.IsTransferInProgress);

            // Only ISPF needs the patch: a TSO transfer on the same engine goes out as usual.
            var tso = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
            var line = await TransferLineAsync(fake);
            fake.Emit(SuccessResult(line, "Transfer complete, 1 bytes transferred"));
            Assert.True((await tso.WaitAsync(WaitTime, TestContext.Current.CancellationToken)).Succeeded);
        }
        finally
        {
            File.Delete(engine.Path);
        }
    }

    [Fact]
    public async Task An_ispf_transfer_on_a_patched_engine_sends_the_command_prefix()
    {
        var engine = EngineFile(patched: true);
        try
        {
            await AssertIspfTransferIsSentAsync(engine);
        }
        finally
        {
            File.Delete(engine.Path);
        }
    }

    /// <summary>No engine file to read (a session built without a location) is assumed patched, so the transfer is
    /// attempted as it was before the check existed.</summary>
    [Fact]
    public Task An_ispf_transfer_with_no_known_engine_file_is_attempted() => AssertIspfTransferIsSentAsync(null);

    private static async Task AssertIspfTransferIsSentAsync(B3270Location? engine)
    {
        var (session, fake) = await StartAsync(engine);
        var transfer = session.TransferAsync(Request with { HostType = TransferHostType.Ispf }, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        Assert.Contains("\"host=tso\",\"commandprefix=TSO\",\"mode=ascii\"", line);
        fake.Emit(SuccessResult(line, "Transfer complete, 1 bytes transferred"));
        Assert.True((await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken)).Succeeded);
    }
```

- [ ] **Step 6: Run them to see the first one fail**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionTransferTests"`
Expected: `An_ispf_transfer_on_an_engine_without_the_patch_fails_at_once_and_sends_nothing` FAILS with a `TimeoutException` after 5 s (the transfer is sent and never answered); the two "sends" tests pass already.

- [ ] **Step 7: Implement the check in `B3270Session`**

After the `private readonly B3270Location _location;` field, add:

```csharp
    /// <summary>Whether the engine at <see cref="_location"/> carries LizTerm's CommandPrefix patch: read from the
    /// binary on the first ISPF transfer and kept for the session (see <see cref="EngineCarriesCommandPrefix"/>).</summary>
    private readonly Lazy<bool> _carriesCommandPrefix;
```

In the constructor, after `_location = location ?? B3270Location.Unknown;`, add:

```csharp
        _carriesCommandPrefix = new Lazy<bool>(EngineCarriesCommandPrefix);
```

In `TransferAsync`, after `RequireProcess();` and before `var context = new TransferContext(progress);`, add:

```csharp
        // An engine without LizTerm's patch cannot be asked and does not refuse the keyword: it would drop the prefix
        // and type a bare IND$FILE into the ISPF command line, then time out. So it is never sent one.
        if (request.HostType == TransferHostType.Ispf && !_carriesCommandPrefix.Value)
            return new FileTransferResult(false, EnginePatches.MissingCommandPrefixMessage);
```

After `TryCancelTransferAsync`, add:

```csharp
    /// <summary>The check behind <see cref="_carriesCommandPrefix"/>. b3270 4.5ga6 silently ignores a Transfer keyword
    /// it does not know, so the binary is the only thing to ask. A session with no engine file (the tests' fake
    /// process) or one that cannot be read is assumed to carry the patch, so the transfer is attempted, as it was
    /// before this check existed.</summary>
    private bool EngineCarriesCommandPrefix()
    {
        if (string.IsNullOrEmpty(_location.Path)) return true;
        try
        {
            return EnginePatches.Carries(_location.Path, EnginePatches.CommandPrefixMarker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
```

- [ ] **Step 8: Run the session tests to see them pass**

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270SessionTransferTests"`
Expected: PASS, the existing transfer tests included.

- [ ] **Step 9: Write the failing locator test**

In `B3270LocatorTests.cs`, replace `Missing_binary_reports_where_it_looked` with:

```csharp
    [Fact]
    public void Missing_binary_reports_where_it_looked_and_points_at_reinstalling()
    {
        var ex = Assert.Throws<BackendUnavailableException>(() => B3270Locator.Find(null, _dir));
        Assert.Contains(_dir, ex.Message);
        Assert.EndsWith("\nReinstalling LizTerm restores it.", ex.Message);
        Assert.DoesNotContain("LIZTERM_B3270_PATH", ex.Message);
    }
```

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~B3270LocatorTests"`
Expected: FAIL (the message still ends with the `Set LIZTERM_B3270_PATH ...` line).

- [ ] **Step 10: Reword the not-found message**

In `B3270Locator.cs`, replace

```csharp
            $"\nSet {EnvironmentOverride} to a b3270 executable to override.");
```

with

```csharp
            "\nReinstalling LizTerm restores it.");
```

(The environment variable is for development only now; `docs/development.md` documents it.)

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests`
Expected: PASS, whole project.

- [ ] **Step 11: Commit**

```bash
git add src/LizTerm.Backend.B3270/Process/EnginePatches.cs src/LizTerm.Backend.B3270/B3270Session.cs src/LizTerm.Backend.B3270/Process/B3270Locator.cs tests/LizTerm.Backend.B3270.Tests/Process/EnginePatchesTests.cs tests/LizTerm.Backend.B3270.Tests/B3270SessionTransferTests.cs tests/LizTerm.Backend.B3270.Tests/Process/B3270LocatorTests.cs
git commit -m "Refuse ISPF transfers on an engine without LizTerm's patch

b3270 silently ignores Transfer keywords it does not know, so the backend
checks the engine binary for the CommandPrefix marker before an ISPF
transfer. The engine-not-found message now points at reinstalling.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 6: App — ISPF (MVS) in the File Transfer dialog

**Files:**
- Modify: `src/LizTerm.App/ViewModels/FileTransferViewModel.cs:78,114,129`, `src/LizTerm.App/Views/FileTransferWindow.axaml:68`, `src/LizTerm.App/Views/TransferLabels.cs:11-21`, `src/LizTerm.App/Files/LocalFileNames.cs:9-21`
- Test: `tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs`, `tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs`, `tests/LizTerm.App.Tests/Files/LocalFileNamesTests.cs`

**Interfaces:**
- Consumes: `TransferHostType.Ispf`, `TransferHostTypes.IsTso()` (Task 3).
- Produces: `FileTransferViewModel.CursorHint` (string), `public const string TsoCursorHint`, `public const string IspfCursorHint`; `HostTypes` = `[Tso, Ispf, Vm, Cics]`; a `TextBlock` named `CursorHintText` bound to `CursorHint`.

- [ ] **Step 1: Write the failing view-model tests**

In `FileTransferViewModelTests.Defaults_are_send_tso_text_with_crlf_and_remap`, replace

```csharp
        Assert.Equal([TransferHostType.Tso, TransferHostType.Vm, TransferHostType.Cics], vm.HostTypes);
```

with

```csharp
        Assert.Equal([TransferHostType.Tso, TransferHostType.Ispf, TransferHostType.Vm, TransferHostType.Cics], vm.HostTypes);
        Assert.Equal(FileTransferViewModel.TsoCursorHint, vm.CursorHint);
```

In `Derived_flags_raise_property_changed`, add `nameof(vm.CursorHint)` to the first `HashSet` (the one checked after `vm.HostType = TransferHostType.Vm;`).

Add two tests after `Switching_to_vm_drops_an_undefined_record_format`:

```csharp
    [Fact]
    public void Ispf_is_tso_in_the_form_with_its_own_cursor_hint()
    {
        var (vm, _, _) = Create();
        vm.RecordFormat = RecordFormat.Undefined;
        vm.AllocationUnits = AllocationUnits.AvBlock;
        vm.HostType = TransferHostType.Ispf;
        Assert.True(vm.IsTso);
        Assert.True(vm.CanSetRecordFormat);
        Assert.True(vm.CanSetBlksize);
        Assert.True(vm.CanSetSpace);
        Assert.True(vm.CanSetAverageBlock);
        Assert.Equal(RecordFormat.Undefined, vm.RecordFormat);
        Assert.Contains(RecordFormat.Undefined, vm.RecordFormats);
        Assert.Equal("The cursor must be on an ISPF Command ===> or Option ===> line before you start.", vm.CursorHint);

        vm.HostType = TransferHostType.Vm;
        Assert.Equal("The cursor must be at a TSO READY prompt or a command line before you start.", vm.CursorHint);
    }

    [Fact]
    public void An_initial_ispf_request_round_trips()
    {
        var initial = new FileTransferRequest { Direction = TransferDirection.Send, LocalPath = "/tmp/x", HostFile = "A.B", HostType = TransferHostType.Ispf };
        var (vm, _, _) = Create(initial);
        Assert.Equal(TransferHostType.Ispf, vm.HostType);
        Assert.Equal(initial, vm.TryBuildRequest());
    }
```

- [ ] **Step 2: Write the failing view and file-name tests**

In `FileTransferWindowTests.Combo_box_labels`, add:

```csharp
    [InlineData(TransferHostType.Ispf, "ISPF (MVS)")]
```

Add to `FileTransferWindowTests`:

```csharp
    [AvaloniaFact]
    public void The_cursor_hint_follows_the_host_type()
    {
        var (window, vm, _) = Show();
        var hint = window.FindControl<TextBlock>("CursorHintText")!;
        Assert.Equal(FileTransferViewModel.TsoCursorHint, hint.Text);
        vm.HostType = TransferHostType.Ispf;
        Assert.Equal(FileTransferViewModel.IspfCursorHint, hint.Text);
    }
```

In `LocalFileNamesTests.Suggests_a_local_name_from_the_host_name`, add:

```csharp
    [InlineData("LIZTERM.JCL(JOB1)", TransferHostType.Ispf, "JOB1")]
    [InlineData("'MVSCE02.LIZTERM.ITEST'", TransferHostType.Ispf, "ITEST")]
```

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferViewModelTests|FullyQualifiedName~FileTransferWindowTests|FullyQualifiedName~LocalFileNamesTests"`
Expected: build error — `CursorHint`, `TsoCursorHint` and `IspfCursorHint` do not exist.

- [ ] **Step 4: Implement the view model**

In `FileTransferViewModel.cs`, add `nameof(CursorHint)` to the `_hostType` attribute so it reads:

```csharp
    [NotifyPropertyChangedFor(nameof(IsTso), nameof(CursorHint), nameof(RecordFormats), nameof(CanSetRecordFormat), nameof(CanSetLrecl), nameof(CanSetBlksize), nameof(CanSetSpace), nameof(CanSetAverageBlock))]
```

Replace `public bool IsTso => HostType == TransferHostType.Tso;` with:

```csharp
    public bool IsTso => HostType.IsTso();

    public const string TsoCursorHint = "The cursor must be at a TSO READY prompt or a command line before you start.";
    public const string IspfCursorHint = "The cursor must be on an ISPF Command ===> or Option ===> line before you start.";

    /// <summary>Where the cursor has to be before Start. Transfer types its command into the input field the cursor is
    /// in, and from ISPF that field is a command line rather than TSO's READY prompt.</summary>
    public string CursorHint => HostType == TransferHostType.Ispf ? IspfCursorHint : TsoCursorHint;
```

Replace the `HostTypes` line with:

```csharp
    public TransferHostType[] HostTypes { get; } = [TransferHostType.Tso, TransferHostType.Ispf, TransferHostType.Vm, TransferHostType.Cics];
```

- [ ] **Step 5: Implement the window, labels and file names**

In `FileTransferWindow.axaml`, replace

```xml
      <TextBlock Text="The cursor must be at a TSO READY prompt or a command line before you start." Foreground="#A0A0A0" TextWrapping="Wrap" />
```

with

```xml
      <TextBlock x:Name="CursorHintText" Text="{Binding CursorHint}" Foreground="#A0A0A0" TextWrapping="Wrap" />
```

In `TransferLabels.cs`, change the summary's first line to `/// <summary>Labels for the transfer enums in the dialog's combo boxes: TSO, ISPF (MVS), VM, CICS, AVBLOCK; every other member` and add the arm after `TransferHostType.Tso => "TSO",`:

```csharp
        TransferHostType.Ispf => "ISPF (MVS)",
```

In `LocalFileNames.cs`, change the summary's first line to `/// <summary>Suggests a local file name for a received host file: the member name or last qualifier of a TSO` (unchanged) and its second to `/// dataset (TSO and ISPF alike), FN.FT for a VM file, the name as typed for CICS. Case is preserved.</summary>`, and replace `case TransferHostType.Tso:` with:

```csharp
            case TransferHostType.Tso or TransferHostType.Ispf:
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferViewModelTests|FullyQualifiedName~FileTransferWindowTests|FullyQualifiedName~LocalFileNamesTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/LizTerm.App/ViewModels/FileTransferViewModel.cs src/LizTerm.App/Views/FileTransferWindow.axaml src/LizTerm.App/Views/TransferLabels.cs src/LizTerm.App/Files/LocalFileNames.cs tests/LizTerm.App.Tests/ViewModels/FileTransferViewModelTests.cs tests/LizTerm.App.Tests/Views/FileTransferWindowTests.cs tests/LizTerm.App.Tests/Files/LocalFileNamesTests.cs
git commit -m "Offer ISPF (MVS) in the File Transfer dialog

It behaves as TSO in the form, and the hint under the form says where the
cursor must be for the chosen host type.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 7: Live ISPF round trip, and the navigator's leftover-panel rule

**Files:**
- Modify: `tests/LizTerm.Integration.Tests/TsoNavigator.cs`, `tests/LizTerm.Integration.Tests/LiveHostTests.cs` (a test after `Indfile_round_trip_matches`), `tests/CLAUDE.md` (Integration tests)
- Create: `tests/LizTerm.Integration.Tests/TsoNavigatorTests.cs`

**Interfaces:**
- Consumes: Tasks 2 to 5 (a bundled, patched `native/out/osx-arm64/b3270` copied into the test output by `dotnet build`; `TransferHostType.Ispf`; the backend mapping and marker check).
- Produces: `TsoNavigator.IsReadyOverLeftoverPanel(string) : bool` (static), `TsoNavigator.StartIspfAsync() : Task<bool>`, `TsoNavigator.WaitForIspfPanelAsync() : Task`; `LiveHostTests.Indfile_round_trip_from_ispf_matches`, whose wire log Task 8 turns into a fixture.

**Background (spec §3):** on MVS/CE, `ISPF` at READY starts Wally ISPF V2.2 with the cursor on `Option ===>`. After a transfer ISPF repaints its panel with no `***` pause. PF3 on the primary menu ends ISPF but leaves the panel on screen with ` READY` written over row 1; today's `ReachReadyAsync` would press PF3 on it ten times and throw. Clear is safe once ISPF has ended, but Clear on a live Wally ISPF panel locks the keyboard for good.

- [ ] **Step 1: Write the failing unit tests**

Create `tests/LizTerm.Integration.Tests/TsoNavigatorTests.cs`:

```csharp
// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Integration.Tests;

/// <summary>The navigator's text rules on screens copied from MVS/CE (Wally ISPF V2.2), no host needed.</summary>
public class TsoNavigatorTests
{
    /// <summary>What PF3 on Wally ISPF's primary menu leaves: the panel, with READY written over its second row.</summary>
    private const string LeftoverPanel =
        "                      Wally ISPF Primary Option Menu    UNIDENTIFIED INPUT FIELD\n" +
        " READY\n" +
        "  Option ===>\n" +
        "  0  Settings     Specify terminal and user parms           USERID   : MVSCE02\n" +
        "       Enter X to terminate ISPF using log and list defaults\n";

    [Fact]
    public void Ready_written_over_a_leftover_panel_is_recognised_and_is_not_plain_ready()
    {
        Assert.True(TsoNavigator.IsReadyOverLeftoverPanel(LeftoverPanel));
        Assert.False(TsoNavigator.IsAtReady(LeftoverPanel));
    }

    [Fact]
    public void A_live_panel_is_not_a_leftover()
    {
        const string live = "  Option ===>\n  6  Command      Enter TSO command or CLIST\n       READY TO GO\n";
        Assert.False(TsoNavigator.IsReadyOverLeftoverPanel(live));
    }

    [Fact]
    public void Plain_ready_is_not_a_leftover_panel()
    {
        const string ready = " USE COMMAND ISPF TO ACCESS ISPF\n READY\n";
        Assert.False(TsoNavigator.IsReadyOverLeftoverPanel(ready));
        Assert.True(TsoNavigator.IsAtReady(ready));
    }
}
```

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~TsoNavigatorTests"`
Expected: build error — `IsReadyOverLeftoverPanel` does not exist.

- [ ] **Step 2: Implement the rule and the ISPF helpers in `TsoNavigator`**

Add to the end of the class summary, before `</summary>`, the sentence: `A line reading exactly READY while a "===>" panel is still showing is TSO after a full-screen program ended; see ReachReadyAsync.`

After `Quiet`, add:

```csharp
    /// <summary>How long an ISPF panel must stand still before a transfer is typed into it. After a transfer ISPF
    /// repaints its panel, and that repaint can land after the transfer's own result, so the half-second
    /// <see cref="Quiet"/> is not enough to know the repaint is over.</summary>
    private static readonly TimeSpan PanelQuiet = TimeSpan.FromSeconds(2);
```

After `IsAtReady`, add:

```csharp
    /// <summary>A full-screen program that has ended (Wally ISPF after PF3) leaves its panel on the screen, and TSO
    /// writes READY over one of its rows instead of below the last line.</summary>
    public static bool IsReadyOverLeftoverPanel(string text) =>
        text.Contains("===>") && text.Split('\n').Any(l => l.Trim() == "READY");
```

In `ReachReadyAsync`, after `if (IsAtReady(text)) return;`, add:

```csharp
            if (IsReadyOverLeftoverPanel(text))
            {
                // Clear is safe because the program has ended; on a live Wally ISPF panel the host never answers a
                // Clear and the keyboard stays locked. TSO answers the Clear with READY or with nothing, so a screen
                // that stays blank gets an Enter, which TSO answers with READY.
                await screens.WaitForUnlockedKeyboardAsync(Step);
                await session.SendKeyAsync(TerminalKey.Clear);
                await screens.WaitForQuietAsync(Quiet, Step);
                if (string.IsNullOrWhiteSpace(screens.LatestText))
                {
                    await screens.WaitForUnlockedKeyboardAsync(Step);
                    await session.SendKeyAsync(TerminalKey.Enter);
                }
                continue;
            }
```

After `CommandAsync`, add:

```csharp
    /// <summary>Types ISPF at READY and waits for its first panel. False when TSO answers that the command was not
    /// found: the host has no ISPF.</summary>
    public async Task<bool> StartIspfAsync()
    {
        await TypeAndEnterAsync("ISPF");
        var screen = await screens.WaitForAsync(t => t.Contains("===>") || t.Contains("NOT FOUND", StringComparison.OrdinalIgnoreCase), Step, "an ISPF panel");
        if (!screen.ToText().Contains("===>")) return false;
        await WaitForIspfPanelAsync();
        return true;
    }

    /// <summary>Waits until an ISPF panel is ready for a transfer: a "===>" prompt, the screen still for
    /// <see cref="PanelQuiet"/>, and the keyboard unlocked. A "***" pause on the way is answered with Enter. Never
    /// sends Clear: Wally ISPF does not answer one.</summary>
    public async Task WaitForIspfPanelAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await screens.WaitForAsync(t => t.Contains("===>") || t.Contains("***"), Step, "an ISPF panel or a *** pause");
            await screens.WaitForQuietAsync(PanelQuiet, Step);
            var text = screens.LatestText;
            if (text.Contains("***"))
            {
                await screens.WaitForUnlockedKeyboardAsync(Step);
                await session.SendKeyAsync(TerminalKey.Enter);
                await screens.WaitForAsync(t => t != text, Step, "the screen to change");
                continue;
            }
            if (!text.Contains("===>")) continue;
            await screens.WaitForUnlockedKeyboardAsync(Step);
            return;
        }
        throw new InvalidOperationException("Could not reach an ISPF panel. Last screen:\n" + screens.LatestText);
    }
```

Run: `dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~TsoNavigatorTests"`
Expected: PASS (3 tests).

- [ ] **Step 3: Add the live test**

In `LiveHostTests.cs`, after `Indfile_round_trip_matches`, add:

```csharp
    /// <summary>The same round trip started from ISPF's primary menu with the ISPF (MVS) host type, so the engine types
    /// TSO ahead of IND$FILE. Only the bundled engine carries LizTerm's CommandPrefix patch, so this test never uses
    /// LIZTERM_B3270_PATH. Skips when TSO answers ISPF with "not found". Run alone with LIZTERM_WIRE_LOG set, its log is
    /// the source of Fixtures/indfile-ispf-roundtrip.jsonl; the log holds the password on its outbound side and must
    /// never be committed.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task Indfile_round_trip_from_ispf_matches()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        var user = Environment.GetEnvironmentVariable("LIZTERM_TEST_USER");
        var password = Environment.GetEnvironmentVariable("LIZTERM_TEST_PASSWORD");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(user), "LIZTERM_TEST_USER is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(password), "LIZTERM_TEST_PASSWORD is not set");
        var engine = BundledEngine.Require();
        var ct = TestContext.Current.CancellationToken;

        var dir = Directory.CreateTempSubdirectory("lizterm-ispf-");
        var sent = Path.Combine(dir.FullName, "sent.txt");
        var received = Path.Combine(dir.FullName, "received.txt");
        await File.WriteAllTextAsync(sent, "LizTerm IND$FILE round trip from ISPF\nsecond line with lowercase text\nthird line has trailing spaces   \nEND\n", ct);

        await using var session = new B3270Session(ProfileFor(target!), () => new B3270ChildProcess(engine.Path), WireLog.TryFromEnvironment(out _), location: engine);
        using var screens = new ScreenWaiter(session);
        var tso = new TsoNavigator(session, screens);
        var dataset = "LIZTERM.ISPFTEST";

        await session.ConnectAsync(cancellationToken: ct);
        try
        {
            await tso.LogonAsync(user!.Trim(), password!);
            Assert.SkipUnless(await tso.StartIspfAsync(), "TSO answered ISPF with \"not found\": this host has no ISPF");

            var progress = new ProgressLog();
            var up = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Send, LocalPath = sent, HostFile = dataset, HostType = TransferHostType.Ispf }, progress, ct);
            Assert.True(up.Succeeded, "send failed: " + up.Message);
            await tso.WaitForIspfPanelAsync();

            var down = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Receive, LocalPath = received, HostFile = dataset, HostType = TransferHostType.Ispf }, progress, ct);
            Assert.True(down.Succeeded, "receive failed: " + down.Message);
            await tso.WaitForIspfPanelAsync();

            Assert.True(progress.Values.Any(v => v > 0), "no bytes were reported for the transfers");
            var expected = (await File.ReadAllLinesAsync(sent, ct)).Select(l => l.TrimEnd());
            var actual = (await File.ReadAllLinesAsync(received, ct)).Select(l => l.TrimEnd());
            Assert.Equal(expected, actual);
        }
        finally
        {
            // Leaving ISPF first: DELETE needs READY. PF3 ends Wally ISPF and ReachReadyAsync clears the panel it
            // leaves behind. A session that never reached ISPF is already at READY, and this returns at once.
            await CleanupStepAsync("leave ISPF", () => tso.ReachReadyAsync());
            await CleanupStepAsync("DELETE", () => tso.CommandAsync($"DELETE '{user!.Trim()}.{dataset}'"));
            await CleanupStepAsync("LOGOFF", () => tso.LogoffAsync());
            await CleanupStepAsync("disconnect", () => session.DisconnectAsync());
            await CleanupStepAsync("delete scratch dir", () => { dir.Delete(recursive: true); return Task.CompletedTask; });
        }
    }
```

Run: `dotnet build tests/LizTerm.Integration.Tests 2>&1 | grep -E " error | warning " | head`
Expected: no output.

- [ ] **Step 4: Run both IND$FILE live tests against MVS/CE**

Needs Robert's `~/.config/lizterm-test.env` (never print it) and the bundled engine from Task 2. In the same shell:

```bash
unset LIZTERM_B3270_PATH
source ~/.config/lizterm-test.env && dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveHostTests.Indfile" --logger "console;verbosity=normal"
```

Expected: `Passed: 2`, `Skipped: 0`, about a minute. If the ISPF test fails, read its message (it carries the last screen), fix the navigator rule, and re-run; do not loop blindly. If a failed run leaves the user logged on, the next run fails at logon with `USERID IN USE`: stop and tell Robert (recovery is `LOGON <user> RECONNECT` by hand).

- [ ] **Step 5: Document the rule in `tests/CLAUDE.md`**

In "Integration tests", after the bullet that ends `fails with \`INVALID COMMAND NAME SYNTAX\`.`, add:

```markdown
- On MVS/CE, `ISPF` at READY starts Wally ISPF. `StartIspfAsync` waits for its first panel (false when TSO says the
  command was not found), and `WaitForIspfPanelAsync` waits for 2 s of quiet before a transfer, so the repaint after
  the previous transfer has landed. PF3 on its primary menu ends ISPF but leaves the panel on screen with READY
  written over a row: a line reading exactly READY while `===>` is still showing. `ReachReadyAsync` answers that
  with Clear, then Enter if the screen stays blank. **Never Clear a live Wally ISPF panel**: that host never answers,
  and the keyboard stays locked until Reset. `Indfile_round_trip_from_ispf_matches` runs only the bundled engine
  (`BundledEngine.Require()`), because an engine set through `LIZTERM_B3270_PATH` lacks the CommandPrefix patch.
```

- [ ] **Step 6: Commit**

```bash
git add tests/LizTerm.Integration.Tests/TsoNavigator.cs tests/LizTerm.Integration.Tests/TsoNavigatorTests.cs tests/LizTerm.Integration.Tests/LiveHostTests.cs tests/CLAUDE.md
git commit -m "Add a live IND\$FILE round trip from ISPF

TsoNavigator can start ISPF, wait for a panel, and leave the panel Wally
ISPF leaves on screen after PF3 without pressing Clear on a live one.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 8: Replay fixture of a real ISPF round trip

**Files:**
- Create: `tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-ispf-roundtrip.jsonl`
- Modify: `tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md`, `tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs` (`Indfile_fixture_parses_with_the_expected_ft_sequence`), `tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs`

**Interfaces:**
- Consumes: Task 7's live test and a reachable MVS/CE host; `tools/wirelog-to-fixture.sh` (keeps lines matching `^<timestamp> < ` and strips the prefix). Wire-log lines are `<ISO-8601 UTC> < <json>` (inbound) and `<ISO-8601 UTC> > <json>` (outbound).
- Produces: the fixture file, copied to test output by the existing `<None Include="Fixtures/**" .../>`.

- [ ] **Step 1: Record a wire log of the ISPF test alone, outside the repository**

```bash
unset LIZTERM_B3270_PATH
rm -f "${TMPDIR:-/tmp}/lizterm-ispf-wire.log"
source ~/.config/lizterm-test.env && LIZTERM_WIRE_LOG="${TMPDIR:-/tmp}/lizterm-ispf-wire.log" dotnet test tests/LizTerm.Integration.Tests --filter "FullyQualifiedName~LiveHostTests.Indfile_round_trip_from_ispf_matches"
wc -l < "${TMPDIR:-/tmp}/lizterm-ispf-wire.log"
```

Expected: the test passes, and the log has several hundred lines. It holds the password on its outbound side: never copy it into the repository.

- [ ] **Step 2: Trim to the two transfers and convert**

```bash
python3 - "${TMPDIR:-/tmp}/lizterm-ispf-wire.log" "${TMPDIR:-/tmp}/lizterm-ispf-slice.log" <<'EOF'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines()
def inbound(l): return re.match(r"^\S+ < ", l) is not None
def outbound(l): return re.match(r"^\S+ > ", l) is not None
# The two Transfer runs the client sent (a Cancel would also be a Transfer action; there is none in a clean run).
sends = [i for i, l in enumerate(lines) if outbound(l) and '"action":"Transfer"' in l and '"Cancel"' not in l]
assert len(sends) == 2, f"expected 2 Transfer runs, found {len(sends)}"
# Start at the last screen update before the first transfer that shows the ISPF prompt.
start = max(i for i in range(sends[0]) if inbound(lines[i]) and '"screen"' in lines[i] and "===>" in lines[i])
# End after the second transfer's run-result and everything the host sent before the client's next line.
tag = re.search(r'"r-tag":"([^"]+)"', lines[sends[1]]).group(1)
done = next(i for i in range(sends[1], len(lines)) if inbound(lines[i]) and '"run-result"' in lines[i] and f'"r-tag":"{tag}"' in lines[i])
end = next((i for i in range(done + 1, len(lines)) if outbound(lines[i])), len(lines))
open(dst, "w", encoding="utf-8").write("\n".join(lines[start:end]) + "\n")
print(f"kept lines {start + 1}..{end} of {len(lines)}")
EOF
tools/wirelog-to-fixture.sh "${TMPDIR:-/tmp}/lizterm-ispf-slice.log" tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-ispf-roundtrip.jsonl
```

Expected: `kept lines N..M of T` and `<count> indications written to ...`. If the `start` search raises (no inbound screen line with `===>` before the first transfer), look at how the log spells the prompt before changing the rule.

- [ ] **Step 3: Check the fixture holds no host address or password, without printing either, then delete the logs**

```bash
F=tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-ispf-roundtrip.jsonl
source ~/.config/lizterm-test.env
echo "host matches: $(grep -cF -- "${LIZTERM_TEST_HOST%%:*}" "$F")  password matches: $(grep -cF -- "$LIZTERM_TEST_PASSWORD" "$F")"
echo "completes: $(grep -c '"state":"complete"' "$F")"
rm -f "${TMPDIR:-/tmp}/lizterm-ispf-wire.log" "${TMPDIR:-/tmp}/lizterm-ispf-slice.log"
```

Expected: `host matches: 0  password matches: 0`, `completes: 2`. If either match count is not 0, stop: do not commit the fixture, and tell Robert.

- [ ] **Step 4: Parse the new fixture alongside the TSO one**

In `IndicationParserTests.cs`, replace

```csharp
    [Fact]
    public void Indfile_fixture_parses_with_the_expected_ft_sequence()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "indfile-tso-roundtrip.jsonl");
```

with

```csharp
    [Theory]
    [InlineData("indfile-tso-roundtrip.jsonl")]
    [InlineData("indfile-ispf-roundtrip.jsonl")]
    public void Indfile_fixture_parses_with_the_expected_ft_sequence(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
```

The body stays: `awaiting` first, a `running` with bytes, two successful completes carrying "Transfer complete".

- [ ] **Step 5: Replay it to the ISPF panel**

Add to `ReplayTests`:

```csharp
    /// <summary>The trimmed ISPF fixture has no initialize block, so the fake's own starts the session and the fixture
    /// follows. What it proves is the host's behaviour LizTerm relies on: after a transfer from ISPF the host
    /// repaints the panel, with no *** pause, because IND$FILE's closing message came through the transfer itself.</summary>
    [Fact]
    public async Task Indfile_from_ispf_replays_back_to_the_ispf_panel()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(new SessionProfile { Name = "replay", Host = "127.0.0.1" }, () => fake);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        foreach (var line in File.ReadLines(Fixture("indfile-ispf-roundtrip.jsonl"))) fake.Emit(line);
        fake.Exit(0);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var text = session.CurrentScreen.ToText();
        Assert.Contains("===>", text);
        Assert.DoesNotContain("***", text);
    }
```

Run: `dotnet test tests/LizTerm.Backend.B3270.Tests --filter "FullyQualifiedName~IndicationParserTests|FullyQualifiedName~ReplayTests"`
Expected: PASS. If the replay test times out, the fake did not end: check that `Exit(0)` follows the emitted lines as in `Ibmlink_help_screen_replays_to_expected_state`.

- [ ] **Step 6: Document the fixture**

Insert an entry, dated today, after the `indfile-tso-roundtrip.jsonl` entry in the fixtures README:

```bash
python3 - tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md "$(date +%F)" <<'EOF'
import sys
path, day = sys.argv[1], sys.argv[2]
anchor = "  waits for the screen to stand still before it treats `READY` as a prompt.\n"
entry = f"""- `indfile-ispf-roundtrip.jsonl`: inbound lines of a real LizTerm session on {day} against the same MVS/CE host,
  recorded from `LiveHostTests.Indfile_round_trip_from_ispf_matches` with the bundled engine and its CommandPrefix
  patch, model 3279-2-E. Trimmed from the last screen update before the first transfer (the Wally ISPF V2.2 primary
  menu, cursor on `Option ===>`) to the last line the host sent after the second transfer ended: a text-mode send to
  `LIZTERM.ISPFTEST` and a receive of it, both with host type ISPF (MVS), which the engine typed as
  `TSO IND$FILE ...` (outbound, so not in the file). Shows that ISPF repaints its panel after each transfer with no
  `***` pause, because IND$FILE's closing message arrives through the transfer's own `FT:MSG` structured field. The
  logon, the ISPF exit and the logoff were cut, so no credentials and no host address are in the file; the TSO user
  id shows on the ISPF panel, as it does on any MVS/CE install.
"""
s = open(path, encoding="utf-8").read()
assert s.count(anchor) == 1, "anchor not found exactly once"
open(path, "w", encoding="utf-8").write(s.replace(anchor, anchor + entry))
print("entry added")
EOF
```

Expected: `entry added`.

- [ ] **Step 7: Commit**

```bash
git add tests/LizTerm.Backend.B3270.Tests/Fixtures/indfile-ispf-roundtrip.jsonl tests/LizTerm.Backend.B3270.Tests/Fixtures/README.md tests/LizTerm.Backend.B3270.Tests/Protocol/IndicationParserTests.cs tests/LizTerm.Backend.B3270.Tests/ReplayTests.cs
git commit -m "Add a replay fixture of an IND\$FILE round trip from ISPF

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

### Task 9: Documentation

**Files:**
- Modify: `docs/user-guide.md` ("File transfer (IND$FILE)"), `src/LizTerm.App/Assets/Docs/user-guide.html` (regenerated, never hand-edited), `README.md` ("Where LizTerm is right now"), `THIRD-PARTY-NOTICES.txt`, `docs/development.md` ("Getting an engine", "Environment variables"), `docs/engines.md` (intro, line 27, new "Patches" section), `docs/ci-and-release.md` ("Caches", "Gates on each package"), `CLAUDE.md`, `src/LizTerm.Core/CLAUDE.md`, `src/LizTerm.Backend.B3270/CLAUDE.md`, `src/LizTerm.App/CLAUDE.md`

**Interfaces:**
- Consumes: everything Tasks 1 to 8 built; spec §7.
- Produces: docs only. `UserGuideAssetTests` passes after regeneration.

- [ ] **Step 1: User guide**

In `docs/user-guide.md`, replace

```markdown
**File > IND$FILE Transfer...** sends files to, or receives them from, a TSO, VM/CMS or CICS host using the host's
IND$FILE program. Before you start, put the cursor at a TSO `READY` prompt or a command line.
```

with

```markdown
**File > IND$FILE Transfer...** sends files to, or receives them from, a TSO, VM/CMS or CICS host using the host's
IND$FILE program. It types the IND$FILE command into the input field the cursor is in, so before you start, put the
cursor at a TSO `READY` prompt or a command line, or use the ISPF (MVS) host type.

- **ISPF (MVS)** starts a transfer from inside ISPF, with the cursor on a `Command ===>` or `Option ===>` line.
  LizTerm types `TSO` ahead of the command, which makes ISPF hand it to TSO, so everything else works as it does for
  TSO, the Advanced options included. ISPF's own TSO command panel (option 6, Command) also accepts a transfer with
  host type TSO.
```

Regenerate the bundled copy and check it:

```bash
LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests|FullyQualifiedName~UserGuideHtmlTests"
```

Expected: both pass; `git status` shows `src/LizTerm.App/Assets/Docs/user-guide.html` modified. If `The_guide_uses_no_construct_the_converter_cannot_render` fails, reword with the constructs the guide already uses rather than changing the converter.

- [ ] **Step 2: README**

In `README.md`, replace

```markdown
The 3270 emulation is **not new code**. It is `b3270`, from the long-established x3270 suite, which every build
bundles. That part of the stack is mature, and it is not where the risk is.
```

with

```markdown
The 3270 emulation is **not new code**. It is `b3270`, from the long-established x3270 suite, which every build
bundles. LizTerm builds it from the upstream source with one small addition of its own, so a file transfer can start
from inside ISPF. That part of the stack is mature, and it is not where the risk is.
```

- [ ] **Step 3: Third-party notices**

In `THIRD-PARTY-NOTICES.txt`, replace

```
x3270 (b3270), BSD-3-Clause

Copyright © 1993-2025, Paul Mattes.
```

with

```
x3270 (b3270), BSD-3-Clause

LizTerm's build of b3270 carries a small modification of its own, published in the LizTerm repository under
native/patches/.

Copyright © 1993-2025, Paul Mattes.
```

Run: `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~AboutWindowTests"`
Expected: PASS (they check that Paul Mattes' notice follows LizTerm's licence, which still holds).

- [ ] **Step 4: Development guide — the override becomes a development shortcut**

In `docs/development.md`, replace

```markdown
The app runs b3270 as a child process and cannot connect without one. Any of these works:

- Build one with `native/build/build-macos.sh`, `native/build/build-linux-docker.sh` or
  `native/build/build-windows-docker.sh` (see [engines](engines.md)), then rebuild the .NET projects so the engine
  is copied into their output.
- Download a CI-built one ([engines](engines.md#using-a-ci-built-engine)).
- Point `LIZTERM_B3270_PATH` at any b3270 4.2 or later. A Homebrew `x3270` install works.
```

with

```markdown
The app runs b3270 as a child process and cannot connect without one. LizTerm ships its own build, with the patches
in `native/patches` applied ([engines](engines.md#patches)), and that is the engine to develop and test against:

- Build one with `native/build/build-macos.sh`, `native/build/build-linux-docker.sh` or
  `native/build/build-windows-docker.sh` (see [engines](engines.md)), then rebuild the .NET projects so the engine
  is copied into their output.
- Download a CI-built one ([engines](engines.md#using-a-ci-built-engine)).

For quick work, `LIZTERM_B3270_PATH` can point at another b3270 4.2 or later, such as a Homebrew `x3270` install. It
lacks LizTerm's patches, so ISPF (MVS) file transfers are refused with it, and it is never what users run.
```

and replace the table row

```markdown
| `LIZTERM_B3270_PATH` | Use this b3270 instead of the bundled one. |
```

with

```markdown
| `LIZTERM_B3270_PATH` | Development only: use this b3270 instead of the bundled one. It lacks LizTerm's [patches](engines.md#patches). |
```

- [ ] **Step 5: Engines page — the override caveat and a Patches section**

In `docs/engines.md`, replace `pinned at 4.5ga6, and runs it` (intro) with `pinned at 4.5ga6 with [LizTerm's patches](#patches) applied, and runs it`.

Replace

```markdown
For development, `LIZTERM_B3270_PATH` accepts any b3270 4.2 or later; a Homebrew `x3270` install works.
```

with

```markdown
For development only, `LIZTERM_B3270_PATH` accepts any b3270 4.2 or later, such as a Homebrew `x3270` install. Such an
engine lacks LizTerm's [patches](#patches), so the backend refuses ISPF (MVS) transfers on it.
```

Insert this section immediately before the `## macOS` heading:

```markdown
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
directory. No fuzz is allowed, so a patch that no longer applies fails the build instead of producing an engine
without it. The Linux and Windows build images install `patch`; macOS has it. `.gitattributes` keeps patch files LF
on every checkout, because the tarball's sources are LF.

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

```

- [ ] **Step 6: CI and release page**

In `docs/ci-and-release.md`, replace

```markdown
- Each engine cache key hashes only the scripts that feed **that** platform — `*macos*.sh` plus `fetch-source.sh`
  and `fetch-openssl.sh` for macOS; `*linux*.sh` plus `fetch-*.sh` for Linux — with `shared-*.sh` in both. A
  Linux-only edit therefore does not force a cold macOS rebuild that cannot change its binary, and new shared
  machinery cannot be added to one key and forgotten in the other.
```

with

```markdown
- `engine-windows` caches `native/out/win-x64` and the x3270 source tarball.
- Each engine cache key hashes only the scripts that feed **that** platform — `*macos*.sh` plus `fetch-source.sh`
  and `fetch-openssl.sh` for macOS; `*linux*.sh` plus `fetch-*.sh` for Linux; `*windows*.sh` plus `fetch-source.sh`
  for Windows — with `shared-*.sh` and `native/patches/**` in all three. A Linux-only edit therefore does not force a
  cold macOS rebuild that cannot change its binary, new shared machinery cannot be added to one key and forgotten in
  another, and an edit to a patch alone rebuilds every engine instead of restoring an unpatched one from the cache.
```

and replace

```markdown
1. **`verify-bundled-engine.sh`**, which fails on a missing, duplicate or wrong-machine-type engine. The engine
```

with

```markdown
1. **`verify-bundled-engine.sh`**, which fails on a missing, duplicate or wrong-machine-type engine, or one without
   LizTerm's patches (`shared-verify-patches.sh`). The engine
```

- [ ] **Step 7: Root `CLAUDE.md`**

Replace

```markdown
A fresh worktree has no `native/out`, so the app and the engine-dependent tests need `LIZTERM_B3270_PATH` (a Homebrew
b3270 works) or a built engine; see `docs/development.md`.
```

with

```markdown
A fresh worktree has no `native/out`, so the app and the engine-dependent tests need a built engine, or
`LIZTERM_B3270_PATH` (a Homebrew b3270 works for everything except ISPF (MVS) transfers, which need LizTerm's
patches); see `docs/development.md`.
```

- [ ] **Step 8: Nested `CLAUDE.md` files**

`src/LizTerm.Core/CLAUDE.md`: replace

```markdown
  success, failure or cancel. (b3270 reports a cancel as a failure reading "Transfer canceled by user"; a host
```

with

```markdown
  success, failure or cancel, except that a backend may fail a request the engine cannot perform, in its own words,
  without sending it (the b3270 backend does this for an ISPF transfer to an engine without LizTerm's patch). (b3270
  reports a cancel as a failure reading "Transfer canceled by user"; a host
```

and after the `FileTransferRequest.Validate()` bullet, add:

```markdown
- `TransferHostType.Ispf` is TSO reached from an ISPF command line. `IsTso()` (on `TransferHostTypes`) is true for it
  and for `Tso`, and is the one place that says so; every TSO rule in Core, the backend and the dialog asks it.
```

`src/LizTerm.Backend.B3270/CLAUDE.md`: replace

```markdown
  not executable"; `B3270Locator.Candidates` is how callers tell the two apart.
```

with

```markdown
  not executable"; `B3270Locator.Candidates` is how callers tell the two apart. Its not-found message ends by telling
  users to reinstall LizTerm; the environment variable is for development only.
```

Under "File transfer", after the `TransferMapper` bullet, add:

```markdown
- ISPF is TSO with a prefix: `TransferMapper` sends `host=tso`, then `commandprefix=TSO`, then every TSO keyword. Only
  LizTerm's patched engine knows `commandprefix` (`native/patches`, `docs/engines.md`), and b3270 silently ignores a
  keyword it does not know, so an unpatched engine would type a bare IND$FILE into the ISPF line and time out.
  `TransferAsync` therefore checks the engine binary for `EnginePatches.CommandPrefixMarker` before an ISPF transfer
  (read once per session; no engine path, or an unreadable file, counts as patched) and without it returns
  `EnginePatches.MissingCommandPrefixMessage` as a failed result, sending nothing. TSO, VM and CICS never read the
  file.
```

`src/LizTerm.App/CLAUDE.md`: under "File transfer", after the `LocalFileNames` bullet, add:

```markdown
- The Host type list is TSO, ISPF (MVS), VM, CICS. ISPF (MVS) is TSO everywhere in the form (`IsTso` is
  `HostType.IsTso()`), and `CursorHint`, bound to the `CursorHintText` line under the form, says where the cursor
  must be for the chosen host type. `LocalFileNames` names an ISPF receive as it does a TSO one.
```

- [ ] **Step 9: Check and commit**

```bash
grep -rn "Homebrew \`x3270\` install works\|to a b3270 executable to override" docs README.md CLAUDE.md src --include='*.md' --include='*.cs'
dotnet test tests/LizTerm.Core.Tests
git add docs/user-guide.md src/LizTerm.App/Assets/Docs/user-guide.html README.md THIRD-PARTY-NOTICES.txt docs/development.md docs/engines.md docs/ci-and-release.md CLAUDE.md src/LizTerm.Core/CLAUDE.md src/LizTerm.Backend.B3270/CLAUDE.md src/LizTerm.App/CLAUDE.md
git commit -m "Document ISPF (MVS) transfers, LizTerm's b3270 patches, and override engines as developer-only

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Expected: the `grep` prints nothing (no doc still offers another install as an equal choice, and the old not-found wording is gone); Core tests pass.

### Task 10: Final verification

**Files:**
- Modify (only if the label is clipped): `src/LizTerm.App/Views/FileTransferWindow.axaml:30` (the Host type `ComboBox` `Width`)

**Interfaces:**
- Consumes: Tasks 1 to 9.
- Produces: a branch ready for `superpowers:finishing-a-development-branch`.

- [ ] **Step 1: Whole suite and zero warnings**

```bash
dotnet test LizTerm.slnx
dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "
```

Expected: every test passes (the live tests skip without `LIZTERM_TEST_HOST`; `EngineSmokeTests` passes because Task 2 built the engine), and the count is `0`.

- [ ] **Step 2: Look at the dialog in the real app (no logon needed)**

Connecting to MVS/CE's banner screen is enough to enable File > IND$FILE Transfer...; nothing here types a password. Seed a throwaway profile from the test host without printing it, and launch the built apphost with a scratch `HOME`:

```bash
H="$PWD/native/build-tmp/inapp/home"
mkdir -p "$H/Library/Application Support/LizTerm/profiles"
source ~/.config/lizterm-test.env
python3 - "$H/Library/Application Support/LizTerm/profiles/MVSCE.json" <<'EOF'
import json, os, sys
host, _, port = os.environ["LIZTERM_TEST_HOST"].partition(":")
json.dump({"name": "MVSCE", "host": host, "port": int(port or 23)}, open(sys.argv[1], "w"))
EOF
dotnet build src/LizTerm.App
env -u LIZTERM_B3270_PATH HOME="$H" LIZTERM_MENU=classic nohup src/LizTerm.App/bin/Debug/net10.0/LizTerm.App MVSCE > "$H/../app.log" 2>&1 &
```

Then, through the Avalonia DevTools MCP (recipe in `src/LizTerm.App/CLAUDE.md`, "Driving the app with the Avalonia DevTools MCP"): open File > IND$FILE Transfer..., open the Host type drop-down and take a screenshot. Check:

1. The list reads TSO, ISPF (MVS), VM, CICS, and `ISPF (MVS)` is shown whole, both in the list and when selected.
2. With ISPF (MVS) selected, the line under the form reads `The cursor must be on an ISPF Command ===> or Option ===> line before you start.`; with TSO it reads the TSO sentence.
3. The Advanced expander keeps BLKSIZE and Allocation enabled for ISPF (MVS).

If the label is clipped, raise the `ComboBox` `Width` in `FileTransferWindow.axaml:30` from `120` to the smallest multiple of 10 that shows it whole, re-run `dotnet test tests/LizTerm.App.Tests --filter "FullyQualifiedName~FileTransferWindowTests"`, and commit (`Widen the Host type drop-down for ISPF (MVS)`). Close the dialog, disconnect, quit the app and delete `native/build-tmp/inapp`.

If the app cannot start (for example Avalonia's `RenderTimer -6661` while the display is asleep), do not retry in a loop: hand these three checks to Robert.

- [ ] **Step 3: Hand Robert the end-to-end transfer in the app**

This one needs his logon, so it is his to run. Ask Robert to, in LizTerm against MVS/CE:

1. Log on, type `ISPF`, and leave the cursor on the primary menu's `Option ===>` line.
2. File > IND$FILE Transfer..., Host type **ISPF (MVS)**, send a small text file to `LIZTERM.ISPFTEST`; the dialog reports "Transfer complete" and ISPF shows its menu again.
3. Receive `LIZTERM.ISPFTEST` back to a new local file and compare.
4. Clean up with `TSO DELETE LIZTERM.ISPFTEST` on the `Option ===>` line (answer the `***` with Enter).

- [ ] **Step 4: Integrate**

Use `superpowers:finishing-a-development-branch`. Once the branch is pushed and CI has run, open the `engine-macos`, `engine-linux` (both legs) and `engine-windows` logs and confirm each gate step printed its patch-marker rejection (`OK: rejected by the patch marker check`, or on Windows `OK: the patch marker arm rejected its fixture`) as well as accepting the real engine. A green run without those lines means the reject case did not run: treat it as a failure.

