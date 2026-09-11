# LizTerm v0.4.1 macOS signing and notarization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sign every macOS package with the project's Developer ID, notarize and staple it, prove that in CI with a gate that can fail, delete `Entitlements.plist`, retire the macOS first-run instructions, and ship the result as v0.4.1.

**Architecture:** No application code changes. Parcel 1.1.1 already signs and notarizes when it is configured to, so the work is configuration (`LizTerm.parcel`, plus four repository secrets scoped to the steps of `release.yml`'s macOS job that need them), one new gate script (`native/build/verify-notarized.sh`) with a CI step that proves it rejects, and documentation. The one question the spec leaves open — whether Parcel staples the app inside the ZIP — is settled by a dispatch rehearsal (Task 4); only then does Task 5 add the step the answer calls for.

**Tech Stack:** GitHub Actions (`macos-15` runners), bash, Apple's `codesign`, `spctl`, `stapler` and `notarytool`, AvaloniaUI.Parcel 1.1.1, actionlint with shellcheck, and the .NET 10 test suite (for `RepositoryHeadersTests`).

**Spec:** `docs/superpowers/specs/2026-09-11-lizterm-macos-notarization-design.md`

## Global Constraints

- **Worktree only.** All work happens in `/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/backlog-review-0-4-0-eb44c8`, on branch `claude/backlog-review-0-4-0-eb44c8`. Never run git in, or write to, the main checkout at `/Users/robert/ClaudeSandbox/LizTerm`.
- **Robert's gates.** Creating secrets, pushing, opening or changing a pull request, dispatching a workflow, and downloading any file each wait for Robert's explicit go-ahead in chat.
- **Credentials never pass through an implementer.** No secret value is typed, echoed, logged, or written anywhere but the runner's `$RUNNER_TEMP`. No `set -x` in a step that holds a secret.
- **Secrets scoping.** A `${{ secrets.* }}` expression appears only under a *step's* `env:`, never job- or workflow-wide, so `ci.yml` and `platforms.yml` stay secret-free and a fork's pull request still runs both in full.
- **One home for the Team ID.** `Z9G64Y4827` is written in `LizTerm.parcel` (`MacOsSettings.TeamId`) and read from there. No script or workflow step hardcodes it.
- **License header.** Every `.sh` under `native/build/` starts, after the shebang, with `# This file is part of LizTerm.`, `# Copyright 2026 by CoffeeMuse`, `# SPDX-License-Identifier: BSD-3-Clause`. `RepositoryHeadersTests` fails the suite otherwise. Scripts are committed executable (mode `100755`).
- **Lint.** `actionlint .github/workflows/release.yml` prints nothing and exits 0; it runs shellcheck over every `run:` block. `shellcheck native/build/verify-notarized.sh` prints nothing.
- **Zero warnings.** `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "` prints `0`.
- **Test commands.** Full suite: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx` (a fresh worktree has no `native/out`). One class: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"`.
- **Design history is a record.** Edit nothing under `docs/superpowers/` except §6 of this slice's spec.
- **Windows is out of scope** (#65). The Windows SmartScreen instructions in the README and the release notes stay word for word.
- **Commits** end with the line `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `native/build/verify-notarized.sh` | The notarization gate: the team's ID on every Mach-O file, a stapled ticket, and `spctl`'s notarized verdict, for a `.app` or a `.dmg`. |

**Modified**

| File | Change |
|---|---|
| `LizTerm.parcel` | `MacOsSettings.TeamId`, `SigningCredentialsType`, `NotaryCredentialsType`; version 0.4.1. |
| `.github/workflows/release.yml` | macOS job: the secrets check, signing credentials to Parcel, a 45-minute timeout, the notarization gate and its proof, and — only if Task 4 calls for it — the ZIP staple step. Two comment references in the Linux and Windows jobs. The release notes' First run section. |
| `Directory.Build.props` | Version 0.4.1. |
| `docs/ci-and-release.md` | "macOS signing and notarization" replaces "macOS signing and `Entitlements.plist`"; the secrets bullet; the gates list. |
| `README.md` | First run. |
| `docs/user-guide.md` | Known limitations. |
| `CLAUDE.md` | A pointer replaces the `Entitlements.plist` warning. |
| `native/build/verify-app-launches.sh` | Header comment: past tense, new section name. |
| `docs/superpowers/specs/2026-09-11-lizterm-macos-notarization-design.md` | §6 As built. |

**Deleted**

| File | Why |
|---|---|
| `Entitlements.plist` | Its one entry, `cs.disable-library-validation`, only ever worked around ad hoc signatures (spec §2.1, §3.5). |

## Local fixtures

The spike left fixtures in this session's scratchpad. Every step below that uses them sets:

```bash
SPIKE=/private/tmp/claude-501/-Users-robert-ClaudeSandbox-LizTerm--claude-worktrees-backlog-review-0-4-0-eb44c8/e34d9709-a314-42ba-88c0-0cf510ebd169/scratchpad
```

- `$SPIKE/resigned/LizTerm.app` — v0.4.0 re-signed with the Developer ID, notarized and stapled. The only local *passing* fixture.
- `$SPIKE/orig/LizTerm.app` — v0.4.0 as published: ad hoc throughout.

They are throwaway and may be gone in another session. Each step that uses them says what happens when they are missing; the rehearsal in Task 4 is the authoritative positive test either way. Local temporary files go under `$SPIKE`, not `/tmp`.

---

## Task 1: The notarization gate

**Files:**
- Create: `native/build/verify-notarized.sh`
- Test (throwaway, not committed): `$SPIKE/test-verify-notarized.sh`

**Interfaces:**
- Consumes: nothing.
- Produces: `native/build/verify-notarized.sh <path-to-.app-or-.dmg> <team-id>`.
  - Exit 0, printing `OK: <path> is signed by team <team-id>, notarized, and stapled`, when all three checks pass.
  - Exit 1 when any check fails, after printing one `ERROR:` line per failure to stderr, each starting with its check's name: `ERROR: Team ID check: …`, `ERROR: stapling check: …`, `ERROR: notarization check: …`. Task 2's proof step matches on the prefixes `Team ID check:`, `stapling check:` and `notarization check:`.
  - Exit 2 for a path that is neither `.app` nor `.dmg`, or that does not exist.

**Why a throwaway harness:** the durable proof is Task 2's CI step, which runs against the package that actually ships. The harness exists to drive this script test-first on a Mac, against fixtures that cannot live in the repository — a notarized bundle is over 100 MB and tied to one certificate.

- [ ] **Step 1: Write the failing harness**

Create `$SPIKE/test-verify-notarized.sh`:

```bash
#!/usr/bin/env bash
# Throwaway harness for native/build/verify-notarized.sh (plan Task 1). Not committed.
set -uo pipefail
REPO=/Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/backlog-review-0-4-0-eb44c8
SPIKE=/private/tmp/claude-501/-Users-robert-ClaudeSandbox-LizTerm--claude-worktrees-backlog-review-0-4-0-eb44c8/e34d9709-a314-42ba-88c0-0cf510ebd169/scratchpad
GATE=$REPO/native/build/verify-notarized.sh
TEAM=Z9G64Y4827
FX=$(mktemp -d "$SPIKE/harness.XXXXXX")
trap 'rm -rf "$FX"' EXIT
PASSED=0
FAILED=0

# expect <name> <wanted-exit> <path> <team> [+message | -message]...
# A +message must appear in the gate's output; a -message must not.
expect() {
  local name=$1 want=$2 path=$3 team=$4 out code ok=1 m
  shift 4
  out=$("$GATE" "$path" "$team" 2>&1)
  code=$?
  [ "$code" -eq "$want" ] || ok=0
  for m in "$@"; do
    case "$m" in
      +*) case "$out" in *"${m#+}"*) ;; *) ok=0 ;; esac ;;
      -*) case "$out" in *"${m#-}"*) ok=0 ;; *) ;; esac ;;
    esac
  done
  if [ "$ok" -eq 1 ]; then
    echo "PASS: $name"
    PASSED=$((PASSED + 1))
  else
    echo "FAIL: $name (exit $code, wanted $want; expectations: $*)"
    printf '%s\n' "$out" | sed 's/^/    /'
    FAILED=$((FAILED + 1))
  fi
}

# A fake bundle: a copy of /usr/bin/true, ad hoc signed. No download and no certificate needed.
mkdir -p "$FX/fake/Fake.app/Contents/MacOS" "$FX/dmgsrc"
cp /usr/bin/true "$FX/fake/Fake.app/Contents/MacOS/Fake"
cat > "$FX/fake/Fake.app/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Fake</string>
<key>CFBundleIdentifier</key><string>dev.coffeemuse.lizterm.gate-fixture</string>
<key>CFBundlePackageType</key><string>APPL</string>
</dict></plist>
EOF
codesign --force --sign - "$FX/fake/Fake.app" 2>/dev/null
# An unsigned disk image holding it.
ditto "$FX/fake/Fake.app" "$FX/dmgsrc/Fake.app"
hdiutil create -quiet -volname GateFixture -srcfolder "$FX/dmgsrc" -format UDZO "$FX/fixture.dmg"
touch "$FX/not-a-package.zip"

expect "a path that is neither .app nor .dmg" 2 "$FX/not-a-package.zip" "$TEAM" "+neither a .app bundle nor a .dmg"
expect "a path that does not exist"           2 "$FX/missing.app"        "$TEAM" "+does not exist"
expect "an ad hoc app fails all three checks" 1 "$FX/fake/Fake.app"      "$TEAM" \
  "+Team ID check:" "+team 'not set'" "+stapling check:" "+notarization check:"
expect "an unsigned DMG fails all three checks" 1 "$FX/fixture.dmg"      "$TEAM" \
  "+Team ID check:" "+team 'none'" "+stapling check:" "+notarization check:"

if [ -d "$SPIKE/orig/LizTerm.app" ]; then
  expect "published v0.4.0 (ad hoc) names each ad hoc file" 1 "$SPIKE/orig/LizTerm.app" "$TEAM" \
    "+Contents/MacOS/libSkiaSharp.dylib is signed by team 'not set'" \
    "+runtimes/osx-arm64/native/b3270 is signed by team 'not set'" \
    "+stapling check:" "+notarization check:"
else
  echo "SKIP: $SPIKE/orig/LizTerm.app is gone"
fi

if [ -d "$SPIKE/resigned/LizTerm.app" ]; then
  expect "the notarized spike bundle passes" 0 "$SPIKE/resigned/LizTerm.app" "$TEAM" "+OK:" "-ERROR:"
  expect "another team's ID is refused" 1 "$SPIKE/resigned/LizTerm.app" AAAAAAAAAA \
    "+Team ID check:" "+not AAAAAAAAAA" "-stapling check:"
  mkdir -p "$FX/unstapled"
  ditto "$SPIKE/resigned/LizTerm.app" "$FX/unstapled/LizTerm.app"
  rm "$FX/unstapled/LizTerm.app/Contents/CodeResources"
  expect "an unstapled copy fails the stapling check" 1 "$FX/unstapled/LizTerm.app" "$TEAM" \
    "+stapling check:" "-Team ID check:"
else
  echo "SKIP: $SPIKE/resigned/LizTerm.app is gone; Task 4's rehearsal is the positive test"
fi

echo "passed $PASSED, failed $FAILED"
[ "$FAILED" -eq 0 ]
```

- [ ] **Step 2: Run it and watch it fail**

Run: `bash "$SPIKE/test-verify-notarized.sh"`

Expected: every case reports `FAIL` with exit 127 ("No such file or directory"), because the gate does not exist yet. The last line is `passed 0, failed 8`, or `passed 0, failed 4` with two `SKIP` lines if the spike fixtures are gone.

- [ ] **Step 3: Write the gate**

Create `native/build/verify-notarized.sh`:

```bash
#!/usr/bin/env bash
# This file is part of LizTerm.
# Copyright 2026 by CoffeeMuse
# SPDX-License-Identifier: BSD-3-Clause

# Fails unless a packaged macOS app bundle or disk image is signed by the project's Developer ID team,
# notarized by Apple, and carries its notarization ticket stapled. release.yml runs it on the app inside every
# macOS ZIP and on every DMG, for both RIDs: it executes nothing, so osx-x64 is covered on an arm64 runner.
#
# Three checks, none of which implies another -- each was measured on macOS 26.6 while this was designed (see
# docs/ci-and-release.md, "macOS signing and notarization"):
#
# 1. Team ID. Every Mach-O file in the bundle carries the team's identifier. codesign --verify --deep --strict
#    passes a bundle with an ad hoc sibling library, and an ad hoc sibling is exactly what once stopped every
#    packaged build from launching.
# 2. Stapled. xcrun stapler validate. Without a stapled ticket a first launch needs Apple's servers, and a user
#    who is offline is refused. spctl cannot see this: it looks the ticket up online, and reports
#    source=Notarized Developer ID for a bundle whose ticket has been deleted.
# 3. Notarized. spctl must report "accepted" AND source=Notarized Developer ID. A file with no quarantine flag
#    -- which is every file on a CI runner -- is "accepted" with source=Developer ID when it is signed but was
#    never notarized, so a check for "accepted" alone cannot fail on the likeliest regression: Parcel skipping
#    notarization because a credential is missing or misnamed.
#
# Every check runs and every failure is reported, rather than stopping at the first, so one run shows each way
# a package is wrong -- and so release.yml's "The notarization gate can fail" step can require each check to
# fire by its own message. Exit 1 means a check failed; exit 2 means the arguments were wrong.
set -uo pipefail
TARGET=${1:?usage: verify-notarized.sh <app-or-dmg> <team-id>}
TEAM=${2:?usage: verify-notarized.sh <app-or-dmg> <team-id>}

case "$TARGET" in
  *.app) KIND=app ;;
  *.dmg) KIND=dmg ;;
  *) echo "ERROR: $TARGET is neither a .app bundle nor a .dmg" >&2; exit 2 ;;
esac
if [ ! -e "$TARGET" ]; then
  echo "ERROR: $TARGET does not exist" >&2
  exit 2
fi

FAILED=0
fail() {
  echo "ERROR: $*" >&2
  FAILED=1
}

# codesign -dv reports on stderr. An ad hoc signature says TeamIdentifier=not set; an unsigned file has no
# TeamIdentifier line at all, which comes back empty and is reported as 'none'.
team_of() {
  codesign -dv "$1" 2>&1 | sed -n 's/^TeamIdentifier=//p'
}

# 1. Team ID
if [ "$KIND" = app ]; then
  MACHO=0
  while IFS= read -r -d '' f; do
    # Captured and matched, not piped into grep -q: grep -q exits at its first match and SIGPIPEs the writer,
    # which pipefail turns into a failure -- the trap engines.yml's gate proofs record.
    description=$(file -b "$f")
    case "$description" in
      *Mach-O*) ;;
      *) continue ;;
    esac
    MACHO=$((MACHO + 1))
    team=$(team_of "$f")
    if [ "$team" != "$TEAM" ]; then
      fail "Team ID check: ${f#"$TARGET"/} is signed by team '${team:-none}', not $TEAM"
    fi
  done < <(find "$TARGET" -type f -print0)
  if [ "$MACHO" -eq 0 ]; then
    fail "Team ID check: no Mach-O file under $TARGET"
  fi
else
  team=$(team_of "$TARGET")
  if [ "$team" != "$TEAM" ]; then
    fail "Team ID check: $TARGET is signed by team '${team:-none}', not $TEAM"
  fi
fi

# 2. Stapled
if ! stapled=$(xcrun stapler validate "$TARGET" 2>&1); then
  fail "stapling check: xcrun stapler validate rejects $TARGET: $(printf '%s\n' "$stapled" | tail -n 1)"
fi

# 3. Notarized
if [ "$KIND" = app ]; then
  assessment=$(spctl -a -vvv -t exec "$TARGET" 2>&1)
else
  assessment=$(spctl -a -vvv -t open --context context:primary-signature "$TARGET" 2>&1)
fi
case "$assessment" in
  *": accepted"*"source=Notarized Developer ID"*) ;;
  *) fail "notarization check: spctl does not report an accepted, notarized $KIND: $(printf '%s' "$assessment" | tr '\n' ' ')" ;;
esac

if [ "$FAILED" -ne 0 ]; then
  exit 1
fi
echo "OK: $TARGET is signed by team $TEAM, notarized, and stapled"
```

Then make it executable: `chmod +x native/build/verify-notarized.sh`

- [ ] **Step 4: Run the harness and watch it pass**

Run: `bash "$SPIKE/test-verify-notarized.sh"`

Expected: eight `PASS` lines and `passed 8, failed 0` — or four `PASS` lines, two `SKIP` lines and `passed 4, failed 0` if the spike fixtures are gone. If a case fails, fix the script, not the harness's expectations: each expectation is a measured fact from the spike (spec §2).

- [ ] **Step 5: Lint it and run the header test**

Run: `shellcheck native/build/verify-notarized.sh`
Expected: no output.

Run: `dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~RepositoryHeadersTests"`
Expected: `Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add native/build/verify-notarized.sh
git ls-files -s native/build/verify-notarized.sh
```

Expected: the mode column reads `100755`. If it reads `100644`, run `git update-index --chmod=+x native/build/verify-notarized.sh`.

```bash
git commit -F - <<'EOF'
Add verify-notarized.sh, the gate for a signed, notarized and stapled macOS package

Three checks, none implied by another: every Mach-O file carries the team's ID, the
ticket is stapled, and spctl reports source=Notarized Developer ID rather than merely
"accepted". Each failure is reported by name so the release job can prove each check
still bites.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

## Task 2: Sign and notarize in the release job

**Files:**
- Modify: `LizTerm.parcel` (the `MacOsSettings` block)
- Modify: `.github/workflows/release.yml` (the `publish-macos` job only)
- Delete: `Entitlements.plist`

**Interfaces:**
- Consumes: `native/build/verify-notarized.sh <path> <team-id>` and its three message prefixes (Task 1).
- Produces:
  - Repository secret names, which Task 4 creates: `MACOS_SIGNING_P12_BASE64`, `MACOS_SIGNING_P12_PASSWORD`, `MACOS_NOTARY_APPLE_ID`, `MACOS_NOTARY_APP_PASSWORD`.
  - Step names, which Tasks 4 to 6 refer to: "The signing secrets are present", "The shipped app and disk image are notarized", "The notarization gate can fail".
  - `/tmp/roundtrip`, holding the extracted ZIP, is the existing step's and is reused unchanged.

- [ ] **Step 1: Write the failing check**

Save as `$SPIKE/check-task2.sh`:

```bash
#!/usr/bin/env bash
set -uo pipefail
cd /Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/backlog-review-0-4-0-eb44c8
python3 - <<'EOF'
import json, sys
m = json.load(open('LizTerm.parcel'))['MacOsSettings']
want = {'TeamId': 'Z9G64Y4827', 'SigningCredentialsType': 'P12Certificate', 'NotaryCredentialsType': 'AppleAccount'}
bad = {k: m.get(k) for k, v in want.items() if m.get(k) != v}
if bad:
    print(f'LizTerm.parcel MacOsSettings wrong: {bad}')
EOF
for step in "The signing secrets are present" "The shipped app and disk image are notarized" \
            "The notarization gate can fail"; do
  grep -q "name: $step" .github/workflows/release.yml || echo "missing step: $step"
done
grep -q "timeout-minutes: 45" .github/workflows/release.yml || echo "macOS timeout is not 45"
[ ! -e Entitlements.plist ] || echo "Entitlements.plist still exists"
# Every secrets reference must be a step-level env entry: ten spaces of indentation in this file.
grep -n 'secrets\.' .github/workflows/release.yml \
  | grep -v -E '^[0-9]+:          [A-Z0-9_]+: \$\{\{ secrets\.[A-Z0-9_]+ \}\}$' \
  | sed 's/^/secret outside a step env: /'
actionlint .github/workflows/release.yml
```

- [ ] **Step 2: Run it and watch it fail**

Run: `bash "$SPIKE/check-task2.sh"`

Expected: `LizTerm.parcel MacOsSettings wrong: {'TeamId': None, 'SigningCredentialsType': None, 'NotaryCredentialsType': None}`, three `missing step:` lines, `macOS timeout is not 45`, and `Entitlements.plist still exists`. No `secret outside a step env` lines, and no actionlint output.

- [ ] **Step 3: Configure Parcel**

In `LizTerm.parcel`, replace:

```json
  "MacOsSettings": {
    "CreateBundle": true,
    "BundleIdentifier": "dev.coffeemuse.lizterm",
    "AppIcon": "src/LizTerm.App/Assets/Icons/lizterm.icns"
  },
```

with:

```json
  "MacOsSettings": {
    "CreateBundle": true,
    "BundleIdentifier": "dev.coffeemuse.lizterm",
    "AppIcon": "src/LizTerm.App/Assets/Icons/lizterm.icns",
    "TeamId": "Z9G64Y4827",
    "SigningCredentialsType": "P12Certificate",
    "NotaryCredentialsType": "AppleAccount"
  },
```

The credentials themselves stay out of the file on purpose: Parcel reads an automatic `PARCEL_*` environment variable only for a setting the `.parcel` file does not define (spec §2.4).

- [ ] **Step 4: Raise the macOS job's timeout**

In `.github/workflows/release.yml`, replace:

```yaml
    runs-on: macos-15
    timeout-minutes: 20
```

with:

```yaml
    runs-on: macos-15
    # 45, from 20: each leg now waits on Apple's notary service for the app and the disk image, and a
    # submission can queue. One submission took about three minutes when this was measured.
    timeout-minutes: 45
```

- [ ] **Step 5: Add the secrets check as the job's first step**

Replace:

```yaml
        rid: [osx-arm64, osx-x64]
    steps:
      - uses: actions/checkout@v7
```

with:

```yaml
        rid: [osx-arm64, osx-x64]
    steps:
      # Signing is mandatory: there is no Entitlements.plist any more, so an ad hoc package would not even
      # launch, and nothing below falls back to one (docs/ci-and-release.md, "macOS signing and
      # notarization"). Fail here, naming what is missing, before publishing or packaging. The secrets reach
      # only the steps that need them -- never the job or the workflow -- so ci.yml and platforms.yml stay
      # secret-free and a fork's pull request still runs both in full.
      - name: The signing secrets are present
        env:
          MACOS_SIGNING_P12_BASE64: ${{ secrets.MACOS_SIGNING_P12_BASE64 }}
          MACOS_SIGNING_P12_PASSWORD: ${{ secrets.MACOS_SIGNING_P12_PASSWORD }}
          MACOS_NOTARY_APPLE_ID: ${{ secrets.MACOS_NOTARY_APPLE_ID }}
          MACOS_NOTARY_APP_PASSWORD: ${{ secrets.MACOS_NOTARY_APP_PASSWORD }}
        run: |
          missing=""
          for name in MACOS_SIGNING_P12_BASE64 MACOS_SIGNING_P12_PASSWORD MACOS_NOTARY_APPLE_ID \
                      MACOS_NOTARY_APP_PASSWORD; do
            [ -n "${!name:-}" ] || missing="$missing $name"
          done
          if [ -n "$missing" ]; then
            echo "::error::Missing repository secrets:$missing. Every macOS package is signed and notarized; see docs/ci-and-release.md, \"macOS signing and notarization\"."
            exit 1
          fi
          echo "All four signing secrets are present."

      - uses: actions/checkout@v7
```

- [ ] **Step 6: Hand the credentials to Parcel**

Replace:

```yaml
      - name: Package
        env:
          AVALONIA_TOOLS_LICENSE_KEY: ${{ secrets.AVALONIA_TOOLS_LICENSE_KEY }}
        run: |
          mkdir -p packages/${{ matrix.rid }}
          parcel pack ./LizTerm.parcel --no-build \
            -r ${{ matrix.rid }} -p zip -p dmg -o packages/${{ matrix.rid }}
```

with:

```yaml
      # Signing and notarization: LizTerm.parcel sets the Team ID and both credential types and leaves the
      # credentials undefined, because Parcel reads an automatic PARCEL_* variable only for a setting the
      # .parcel file does not define. The P12 has to be a file, so it is decoded into the runner's temp
      # directory and removed when the step exits.
      - name: Package
        env:
          AVALONIA_TOOLS_LICENSE_KEY: ${{ secrets.AVALONIA_TOOLS_LICENSE_KEY }}
          MACOS_SIGNING_P12_BASE64: ${{ secrets.MACOS_SIGNING_P12_BASE64 }}
          PARCEL_MACOS_SIGNING_P12_PASSWORD: ${{ secrets.MACOS_SIGNING_P12_PASSWORD }}
          PARCEL_MACOS_NOTARY_APPLE_ID: ${{ secrets.MACOS_NOTARY_APPLE_ID }}
          PARCEL_MACOS_NOTARY_APP_PASSWORD: ${{ secrets.MACOS_NOTARY_APP_PASSWORD }}
        run: |
          set -euo pipefail
          export PARCEL_MACOS_SIGNING_P12_CERTIFICATE="$RUNNER_TEMP/signing.p12"
          trap 'rm -f "$PARCEL_MACOS_SIGNING_P12_CERTIFICATE"' EXIT
          printf '%s' "$MACOS_SIGNING_P12_BASE64" | base64 --decode > "$PARCEL_MACOS_SIGNING_P12_CERTIFICATE"
          mkdir -p packages/${{ matrix.rid }}
          parcel pack ./LizTerm.parcel --no-build \
            -r ${{ matrix.rid }} -p zip -p dmg -o packages/${{ matrix.rid }}
```

`-p dmg` appears only in this job (the Linux and Windows jobs build ZIPs too), so this block is unique. The Linux and Windows `Package` steps are untouched.

- [ ] **Step 7: Correct two comments that describe ad hoc signing as current**

Replace:

```yaml
      # and the .app and ZIP round trip did not invalidate the ad hoc signature the SDK and the linker applied.
```

with:

```yaml
      # and the .app and ZIP round trip did not invalidate its signature.
```

Then replace:

```yaml
      # dylibs -- see docs/ci-and-release.md, "macOS signing and Entitlements.plist". Same osx-x64 skip and
      # the same reason as the engine-start step above: this runner cannot execute an x86_64 binary without
      # Rosetta.
```

with:

```yaml
      # dylibs -- see docs/ci-and-release.md, "macOS signing and notarization". Signed with the Developer ID,
      # the app no longer needs the entitlement that fixed it, and this step is what proves that. Same osx-x64
      # skip and the same reason as the engine-start step above: this runner cannot execute an x86_64 binary
      # without Rosetta.
```

- [ ] **Step 8: Add the gate and its proof after the launch check**

Replace:

```yaml
      - name: The packaged app launches
        if: matrix.rid == 'osx-arm64'
        run: native/build/verify-app-launches.sh /tmp/roundtrip ${{ matrix.rid }}
```

with:

```yaml
      - name: The packaged app launches
        if: matrix.rid == 'osx-arm64'
        run: native/build/verify-app-launches.sh /tmp/roundtrip ${{ matrix.rid }}

      # Every package this job ships, on both RIDs: verify-notarized.sh executes nothing, so unlike the two
      # steps above it covers osx-x64 too. The Team ID comes from LizTerm.parcel, the one place it is written.
      - name: The shipped app and disk image are notarized
        run: |
          set -euo pipefail
          team=$(python3 -c "import json; print(json.load(open('LizTerm.parcel'))['MacOsSettings']['TeamId'])")
          app=$(find /tmp/roundtrip -maxdepth 1 -name '*.app' -type d)
          dmg=$(find packages/${{ matrix.rid }} -name '*.dmg')
          [ "$(printf '%s' "$app" | grep -c . || true)" -eq 1 ] \
            || { echo "expected one .app at the top of the ZIP, found: '$app'" >&2; exit 1; }
          [ "$(printf '%s' "$dmg" | grep -c . || true)" -eq 1 ] \
            || { echo "expected one .dmg, found: '$dmg'" >&2; exit 1; }
          # Both run before the step fails, so one log carries the verdict on the app and on the disk image.
          status=0
          native/build/verify-notarized.sh "$app" "$team" || status=1
          native/build/verify-notarized.sh "$dmg" "$team" || status=1
          exit "$status"

      # A guard that cannot fail is not a guard. Two copies of the shipped app, each broken one way, must each
      # be rejected BY THE CHECK THAT EXISTS FOR IT -- matched on that check's own message, because the script
      # also exits non-zero when spctl cannot reach Apple, and that must not pass as proof. An ad hoc copy must
      # fail the Team ID and notarization checks; an unstapled copy (Contents/CodeResources, the ticket, sits
      # outside the code seal, so deleting it leaves the signature valid) must fail the stapling check.
      # Nothing else is asserted about the unstapled copy: spctl looks its ticket up online, so its verdict
      # there moves with the network. The signed-but-unnotarized case cannot be built here without handing the
      # signing key to this step too; verify-notarized.sh's own comment records why it matches the exact
      # source= line rather than "accepted".
      - name: The notarization gate can fail
        run: |
          set -euo pipefail
          team=$(python3 -c "import json; print(json.load(open('LizTerm.parcel'))['MacOsSettings']['TeamId'])")
          app=$(find /tmp/roundtrip -maxdepth 1 -name '*.app' -type d)
          rm -rf /tmp/gate-proof && mkdir -p /tmp/gate-proof/adhoc /tmp/gate-proof/unstapled
          ditto "$app" /tmp/gate-proof/adhoc/LizTerm.app
          codesign --force --deep --sign - /tmp/gate-proof/adhoc/LizTerm.app
          ditto "$app" /tmp/gate-proof/unstapled/LizTerm.app
          # No -f: the step above has just proved the shipped app is stapled, so a missing ticket here means
          # the proof is no longer testing what it claims to.
          rm /tmp/gate-proof/unstapled/LizTerm.app/Contents/CodeResources

          # Capture and match; do not pipe. grep -q exits on its first match and SIGPIPEs the writer, which
          # under pipefail becomes 141 -- the trap engines.yml's gate proofs record.
          reject() {
            echo "--- the gate must reject $2 by its $1"
            if output=$(native/build/verify-notarized.sh "$2" "$team" 2>&1); then
              printf "%s\n" "$output"
              echo "FAILED: verify-notarized.sh accepted $2; its $1 is not working" >&2
              exit 1
            fi
            printf "%s\n" "$output"
            case "$output" in
              *"$3"*) echo "OK: rejected by the $1" ;;
              *) echo "FAILED: verify-notarized.sh rejected $2, but not by its $1 (nothing matched: $3)" >&2
                 exit 1 ;;
            esac
          }

          reject "Team ID check"      /tmp/gate-proof/adhoc/LizTerm.app     "Team ID check:"
          reject "notarization check" /tmp/gate-proof/adhoc/LizTerm.app     "notarization check:"
          reject "stapling check"     /tmp/gate-proof/unstapled/LizTerm.app "stapling check:"
```

- [ ] **Step 9: Delete the entitlement**

Run: `git rm Entitlements.plist`

- [ ] **Step 10: Run the check and watch it pass**

Run: `bash "$SPIKE/check-task2.sh"` and `python3 -m json.tool LizTerm.parcel > /dev/null && echo "json ok"`

Expected: only `json ok`. Any actionlint output is a failure; fix the YAML, not the check.

- [ ] **Step 11: Dry-run the proof step locally against the spike bundle**

This runs the proof step's `run:` block exactly as committed, with its two `/tmp` paths moved into the scratchpad. Skip it if `$SPIKE/resigned/LizTerm.app` is gone; Task 4 then exercises it for the first time.

```bash
cd /Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/backlog-review-0-4-0-eb44c8
rm -rf "$SPIKE/roundtrip-dry" && mkdir -p "$SPIKE/roundtrip-dry"
ditto "$SPIKE/resigned/LizTerm.app" "$SPIKE/roundtrip-dry/LizTerm.app"
SPIKE="$SPIKE" python3 - <<'EOF' > "$SPIKE/gate-proof-dry.sh"
import os, yaml
spike = os.environ['SPIKE']
jobs = yaml.safe_load(open('.github/workflows/release.yml'))['jobs']
step = next(s for s in jobs['publish-macos']['steps'] if s.get('name') == 'The notarization gate can fail')
print(step['run'].replace('/tmp/roundtrip', f'{spike}/roundtrip-dry').replace('/tmp/gate-proof', f'{spike}/gate-proof-dry'))
EOF
bash "$SPIKE/gate-proof-dry.sh"; echo "exit: $?"
```

Expected: `OK: rejected by the Team ID check`, `OK: rejected by the notarization check`, `OK: rejected by the stapling check`, then `exit: 0`.

- [ ] **Step 12: Commit**

```bash
git add LizTerm.parcel .github/workflows/release.yml
git commit -F - <<'EOF'
Sign and notarize every macOS package, and prove the gate that checks it can fail

LizTerm.parcel names the team and the two credential types; four repository secrets
reach the macOS job's secrets check and its Package step, nowhere else. Every macOS
ZIP's app and every DMG must pass verify-notarized.sh on both RIDs, and a second step
proves each of its three checks still rejects. Entitlements.plist is deleted: with every
Mach-O under one Team ID, library validation has nothing to refuse.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

## Task 3: Version 0.4.1 and the local suite

**Files:**
- Modify: `Directory.Build.props:9`
- Modify: `LizTerm.parcel:6`

**Interfaces:**
- Consumes: nothing.
- Produces: version `0.4.1`, which the `version` job reads from both files and compares with the tag.

- [ ] **Step 1: Bump both versions**

In `Directory.Build.props`, replace `    <Version>0.4.0</Version>` with `    <Version>0.4.1</Version>`.

In `LizTerm.parcel`, replace `    "Version": "0.4.0",` with `    "Version": "0.4.1",`.

- [ ] **Step 2: Check they agree the way the release job does**

```bash
tree=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props)
parcel=$(python3 -c "import json;print(json.load(open('LizTerm.parcel')).get('GeneralSettings',{}).get('Version',''))")
echo "$tree $parcel"
```

Expected: `0.4.1 0.4.1`

- [ ] **Step 3: Run the full suite**

Run: `LIZTERM_B3270_PATH=/opt/homebrew/bin/b3270 dotnet test LizTerm.slnx`

Expected: every project reports `Failed: 0`. The live-host tests skip themselves.

- [ ] **Step 4: Check for warnings**

Run: `dotnet build LizTerm.slnx --no-incremental 2>&1 | grep -c " warning "`

Expected: `0`

- [ ] **Step 5: Commit**

```bash
git add Directory.Build.props LizTerm.parcel
git commit -F - <<'EOF'
Bump the version to 0.4.1 for the notarized release

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
```

---

## Task 4: Secrets, push, and the rehearsal

**Files:** none change in this task.

**Interfaces:**
- Consumes: the secret names and step names from Task 2.
- Produces: the rehearsal's run ID and the spec §3.4 outcome — exactly one of **(a)**, **(b)** or **(c)** — which Tasks 5, 6 and 7 act on.

- [ ] **Step 1: Robert creates the secrets**

Give Robert these steps, word for word. They are his to run: no credential passes through an implementer.

1. In **Keychain Access**, choose the **login** keychain and the **My Certificates** tab. Right-click the **Developer ID Application** certificate for team **Z9G64Y4827** and choose **Export**. Pick the format **Personal Information Exchange (.p12)**, save it as `~/Desktop/lizterm-developer-id.p12`, and set an export password. macOS then asks for your login password to allow the export.
2. In Terminal, run these one at a time. Each `gh secret set` without a value prompts for it, so nothing lands in your shell history; the first reads the file through a pipe instead.

```bash
base64 -i ~/Desktop/lizterm-developer-id.p12 | gh secret set MACOS_SIGNING_P12_BASE64 -R coffeemuse/LizTerm
```

```bash
gh secret set MACOS_SIGNING_P12_PASSWORD -R coffeemuse/LizTerm
```

```bash
gh secret set MACOS_NOTARY_APPLE_ID -R coffeemuse/LizTerm
```

```bash
gh secret set MACOS_NOTARY_APP_PASSWORD -R coffeemuse/LizTerm
```

   `MACOS_NOTARY_APP_PASSWORD` is the app-specific password made for the spike.
3. Move `~/Desktop/lizterm-developer-id.p12` somewhere private, or delete it; the certificate stays in the keychain and can be exported again.

- [ ] **Step 2: Confirm the names**

Run: `gh secret list -R coffeemuse/LizTerm`

Expected: `AVALONIA_TOOLS_LICENSE_KEY`, `MACOS_NOTARY_APPLE_ID`, `MACOS_NOTARY_APP_PASSWORD`, `MACOS_SIGNING_P12_BASE64`, `MACOS_SIGNING_P12_PASSWORD`. Only names are listed; values are never readable.

- [ ] **Step 3: Push and open a draft pull request — after Robert says go**

Ask Robert for one go-ahead covering this branch: push it, open the draft pull request, dispatch the rehearsal, and push the fixes the rehearsal needs until it goes green.

Write `$SPIKE/pr-body.md`:

```markdown
Signs every macOS package with the project's Developer ID, notarizes and staples it, and ships the result as
v0.4.1. Spec: `docs/superpowers/specs/2026-09-11-lizterm-macos-notarization-design.md`. Plan:
`docs/superpowers/plans/2026-09-11-lizterm-macos-notarization.md`.

- `LizTerm.parcel` names the team and the credential types. Four repository secrets reach only the macOS release
  job's steps that need them.
- `native/build/verify-notarized.sh` gates the app inside every macOS ZIP and every DMG, on both RIDs, and a second
  step proves each of its checks can still fail.
- `Entitlements.plist` is gone: with every Mach-O under one Team ID, library validation has nothing to refuse.
- The macOS first-run instructions are gone from the README, the release notes and the user guide.

Closes #36.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
```

```bash
git push -u origin claude/backlog-review-0-4-0-eb44c8
gh pr create -R coffeemuse/LizTerm --draft --base main \
  --title "v0.4.1: sign and notarize the macOS build" --body-file "$SPIKE/pr-body.md"
```

Expected: a pull request URL. `ci.yml` runs on it, and `platforms.yml` does too, because `native/**` changed.

- [ ] **Step 4: Dispatch the rehearsal**

```bash
gh workflow run release.yml -R coffeemuse/LizTerm --ref claude/backlog-review-0-4-0-eb44c8
gh run list -R coffeemuse/LizTerm --workflow release.yml --branch claude/backlog-review-0-4-0-eb44c8 \
  --event workflow_dispatch --limit 1 --json databaseId,status --jq '.[0]'
```

If the list is empty, the run has not registered yet; run the `gh run list` line again. Then, with `RUN` set to that `databaseId`:

```bash
gh run watch "$RUN" -R coffeemuse/LizTerm --exit-status
```

It takes roughly 15 to 30 minutes. Run it in the background and wait for it to finish. A dispatch publishes nothing: `release.yml`'s `release` job runs only for a pushed tag.

- [ ] **Step 5: Read both macOS legs**

```bash
gh run view "$RUN" -R coffeemuse/LizTerm --json jobs --jq '.jobs[] | "\(.databaseId) \(.name): \(.conclusion)"'
```

For each `publish-macos` job ID:

```bash
gh run view -R coffeemuse/LizTerm --job "$JOB_ID" --log \
  | grep -E "OK:|ERROR:|FAILED|[Nn]otariz|[Ss]tapl|secrets" | cut -c1-240
```

- [ ] **Step 6: Decide the outcome**

Both legs must agree. If they disagree, stop and report to Robert with both excerpts.

| What the gate step shows for the app from the ZIP | The DMG | Outcome | Next |
|---|---|---|---|
| `OK`, and every job is green | `OK` | **(a)** Parcel staples the ZIP's app | Skip Task 5 |
| Only `ERROR: stapling check:` | `OK` | **(b)** A ticket exists for the ZIP's app: `spctl` found it online, so the DMG's submission covered it | Task 5, version (b) |
| `ERROR: stapling check:` and `ERROR: notarization check:` | `OK` | **(c)** No ticket covers the ZIP's copy | Task 5, version (c) |
| Anything else | — | Not covered by the spec | Stop and report |

"Anything else" includes: the secrets check failing; Parcel failing before the gates run; a `Team ID check` failure; the DMG failing; the proof step failing. Report the log excerpt to Robert and wait. What each usually means:

- `Notarization credentials are not set — skipping` in the Package log, with the gate failing the notarization check: a `PARCEL_MACOS_*` name in the Package step is wrong. Compare the names against spec §2.4.
- Parcel failing to import or use the P12: the fallback, importing it into a temporary keychain and switching to `KeyChainIdentity`, changes the design, so it goes to Robert before anything is written.
- A notarization result of `Invalid`: Robert's local profile can read the team's submissions. Get the submission ID from the Package log, then run `xcrun notarytool log <submission-id> --keychain-profile lizterm-notary`.

- [ ] **Step 7: Record the outcome**

Tell Robert the outcome, with the run ID and the lines that decided it. Tasks 5, 6 and 7 each depend on it.

---

## Task 5 (only for outcome (b) or (c)): The ZIP's app is stapled

**Files:**
- Modify: `.github/workflows/release.yml` (the `publish-macos` job, between Package and "The shipped archive carries the right engine")

**Interfaces:**
- Consumes: the outcome from Task 4; the packages Parcel wrote under `packages/<rid>`.
- Produces: a stapled app inside the ZIP, so that the gates, which extract the ZIP later, see the ZIP that ships. With (c), the Apple ID secrets reach this step too.

Skip this task entirely for outcome (a).

- [ ] **Step 1: Add the step**

Replace:

```yaml
            -r ${{ matrix.rid }} -p zip -p dmg -o packages/${{ matrix.rid }}

      # One step, three claims:
```

with **version (b)**, if Task 4 found outcome (b):

```yaml
            -r ${{ matrix.rid }} -p zip -p dmg -o packages/${{ matrix.rid }}

      # Parcel notarizes and staples the DMG but leaves the app inside the ZIP unstapled (measured on the v0.4.1
      # rehearsal; docs/ci-and-release.md, "macOS signing and notarization"). The DMG's submission already
      # covered that app -- a ticket is keyed by code-directory hash, which both copies share -- so stapling
      # needs no second submission. The ZIP is rebuilt with ditto, the tool Apple documents for zipping a
      # bundle, before any gate below extracts it, so every gate sees the ZIP that ships.
      - name: The ZIP's app is stapled
        run: |
          set -euo pipefail
          zip=$(find packages/${{ matrix.rid }} -name '*.zip')
          [ "$(printf '%s' "$zip" | grep -c . || true)" -eq 1 ] \
            || { echo "expected one .zip, found: '$zip'" >&2; exit 1; }
          rm -rf /tmp/staple && mkdir -p /tmp/staple
          ditto -x -k "$zip" /tmp/staple
          app=$(find /tmp/staple -maxdepth 1 -name '*.app' -type d)
          [ "$(printf '%s' "$app" | grep -c . || true)" -eq 1 ] \
            || { echo "expected one .app at the top of the ZIP, found: '$app'" >&2; exit 1; }
          xcrun stapler staple "$app"
          rm "$zip"
          ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"

      # One step, three claims:
```

or with **version (c)**, if Task 4 found outcome (c):

```yaml
            -r ${{ matrix.rid }} -p zip -p dmg -o packages/${{ matrix.rid }}

      # Parcel notarizes and staples the DMG, but not the app inside the ZIP, and no ticket covers that copy
      # (measured on the v0.4.1 rehearsal; docs/ci-and-release.md, "macOS signing and notarization"). So this
      # step submits it with the same Apple ID credentials, staples it, and rebuilds the ZIP with ditto, the
      # tool Apple documents for zipping a bundle, before any gate below extracts it, so every gate sees the
      # ZIP that ships.
      - name: The ZIP's app is stapled
        env:
          MACOS_NOTARY_APPLE_ID: ${{ secrets.MACOS_NOTARY_APPLE_ID }}
          MACOS_NOTARY_APP_PASSWORD: ${{ secrets.MACOS_NOTARY_APP_PASSWORD }}
        run: |
          set -euo pipefail
          team=$(python3 -c "import json; print(json.load(open('LizTerm.parcel'))['MacOsSettings']['TeamId'])")
          zip=$(find packages/${{ matrix.rid }} -name '*.zip')
          [ "$(printf '%s' "$zip" | grep -c . || true)" -eq 1 ] \
            || { echo "expected one .zip, found: '$zip'" >&2; exit 1; }
          rm -rf /tmp/staple && mkdir -p /tmp/staple
          ditto -x -k "$zip" /tmp/staple
          app=$(find /tmp/staple -maxdepth 1 -name '*.app' -type d)
          [ "$(printf '%s' "$app" | grep -c . || true)" -eq 1 ] \
            || { echo "expected one .app at the top of the ZIP, found: '$app'" >&2; exit 1; }
          ditto -c -k --sequesterRsrc --keepParent "$app" "$RUNNER_TEMP/notarize.zip"
          xcrun notarytool submit "$RUNNER_TEMP/notarize.zip" --apple-id "$MACOS_NOTARY_APPLE_ID" \
            --team-id "$team" --password "$MACOS_NOTARY_APP_PASSWORD" --wait
          xcrun stapler staple "$app"
          rm "$zip"
          ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"

      # One step, three claims:
```

- [ ] **Step 2: Lint**

Run: `bash "$SPIKE/check-task2.sh"`

Expected: no output. With version (c), its two new `secrets.` lines are step-level env entries, so the scoping check still passes.

- [ ] **Step 3: Commit and push**

```bash
git add .github/workflows/release.yml
git commit -F - <<'EOF'
Staple the app inside the macOS ZIP, which Parcel leaves unstapled

Measured on the v0.4.1 rehearsal: Parcel staples the DMG but not the ZIP's app.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
git push
```

- [ ] **Step 4: Rehearse again**

Repeat Task 4, Steps 4 and 5. Expected: every job in the run is green, and each `publish-macos` log shows two `OK: … notarized, and stapled` lines and three `OK: rejected by the …` lines.

---

## Task 6: Documentation

**Files:**
- Modify: `docs/ci-and-release.md`, `README.md`, `docs/user-guide.md`, `CLAUDE.md`, `.github/workflows/release.yml` (the release notes and two comments), `native/build/verify-app-launches.sh` (a comment)

**Interfaces:**
- Consumes: the outcome from Task 4, which picks the one paragraph below that differs by outcome; the step names from Tasks 2 and 5.
- Produces: nothing later tasks depend on.

- [ ] **Step 1: Write the failing check**

Save as `$SPIKE/check-task6.sh`:

```bash
#!/usr/bin/env bash
cd /Users/robert/ClaudeSandbox/LizTerm/.claude/worktrees/backlog-review-0-4-0-eb44c8
# User-facing text that says the builds are unsigned.
grep -n -E "not yet signed|not signed or notarized|next on the list|next thing on the list|Unsigned builds\." \
  README.md docs/user-guide.md .github/workflows/release.yml
# Pointers to the old section, and the warning that the entitlement is load-bearing.
grep -n -E 'signing and .?Entitlements|and Entitlements\.plist"|Entitlements\.plist. is' \
  CLAUDE.md docs/ci-and-release.md .github/workflows/release.yml native/build/verify-app-launches.sh
grep -n "Builds ship unsigned" docs/ci-and-release.md
actionlint .github/workflows/release.yml
```

- [ ] **Step 2: Run it and watch it fail**

Run: `bash "$SPIKE/check-task6.sh"`

Expected: matches from `README.md` (the unsigned sentence and the "next on the list" line), `docs/user-guide.md` (`Unsigned builds.`), `release.yml` (the release notes' two sentences and the Linux and Windows comments), `CLAUDE.md`, `docs/ci-and-release.md` (the heading and "Builds ship unsigned"), and `verify-app-launches.sh`.

- [ ] **Step 3: README.md**

Replace everything from `## First run` up to, but not including, `## Getting started` with:

```markdown
## First run

**macOS** builds are signed and notarized by Apple, so LizTerm opens like any other app you download. The first
time, macOS may ask you to confirm that you want to open something downloaded from the internet.

**Windows**: SmartScreen warns that the publisher is unknown. Choose **More info**, then **Run anyway**.

```

- [ ] **Step 4: The release notes in release.yml**

In the `Write the release notes` step's heredoc, replace everything from `          ## First run` up to, but not including, `          ## Which file do I want?` with:

```yaml
          ## First run

          **macOS** builds are signed and notarized by Apple, so LizTerm opens like any other app you
          download. The first time, macOS may ask you to confirm that you want to open something downloaded
          from the internet.

          **Windows** -- SmartScreen will warn that the publisher is unknown. Choose **More info**, then
          **Run anyway**.

```

Keep the ten-space indentation: the heredoc sits inside a YAML block scalar.

- [ ] **Step 5: The two comment references in release.yml**

Replace:

```yaml
      # window and all -- see docs/ci-and-release.md, "macOS signing and Entitlements.plist", for the macOS
```

with:

```yaml
      # window and all -- see docs/ci-and-release.md, "macOS signing and notarization", for the macOS
```

Then replace:

```yaml
      # docs/ci-and-release.md, "macOS signing and Entitlements.plist", for the macOS incident this class of
```

with:

```yaml
      # docs/ci-and-release.md, "macOS signing and notarization", for the macOS incident this class of
```

- [ ] **Step 6: docs/user-guide.md**

Replace:

```markdown
- **Unsigned builds.** macOS and Windows warn about the app the first time you open it; the
  [README](../README.md#first-run) explains how to allow it.
```

with:

```markdown
- **Unsigned Windows builds.** Windows SmartScreen warns about the app the first time you open it; the
  [README](../README.md#first-run) explains how to allow it.
```

- [ ] **Step 7: CLAUDE.md**

Replace:

```markdown
- Before touching `LizTerm.parcel` or `Entitlements.plist`, read `docs/ci-and-release.md`. `Entitlements.plist` is
  load-bearing: without its one entry, no packaged macOS build launches.
```

with:

```markdown
- Before touching `LizTerm.parcel` or the signing steps of `release.yml`'s macOS job, read `docs/ci-and-release.md`,
  "macOS signing and notarization". Signing is mandatory: without its four secrets the release fails by design, and
  an ad hoc macOS package would not launch.
```

- [ ] **Step 8: native/build/verify-app-launches.sh**

Replace:

```bash
# then refused to launch for every user: Parcel ad-hoc signs the packaged executable with the hardened
# runtime while signing the bundled libSkiaSharp/libHarfBuzzSharp/libAvaloniaNative dylibs plain ad-hoc, the
# hardened runtime's library validation then refuses to dlopen a sibling with no matching Team ID, and
# Avalonia dies during Skia initialisation before a window ever opens. It was found by launching the app by
```

with:

```bash
# then refused to launch for every user: Parcel ad-hoc signed the packaged executable with the hardened
# runtime while signing the bundled libSkiaSharp/libHarfBuzzSharp/libAvaloniaNative dylibs plain ad-hoc, the
# hardened runtime's library validation then refused to dlopen a sibling with no matching Team ID, and
# Avalonia died during Skia initialisation before a window ever opened. It was found by launching the app by
```

Then replace:

```bash
# bug was -- and proves it is still running a few seconds later. See docs/ci-and-release.md, "macOS signing
# and Entitlements.plist", for the full incident.
```

with:

```bash
# bug was -- and proves it is still running a few seconds later. See docs/ci-and-release.md, "macOS signing
# and notarization", for the full incident.
```

- [ ] **Step 9: docs/ci-and-release.md, the secrets bullet**

Replace:

```markdown
- `AVALONIA_TOOLS_LICENSE_KEY`, the repository secret Parcel needs to run at all, is scoped to the `parcel pack`
  steps alone, by `env:` on each step rather than job- or workflow-wide. `ci.yml` and `platforms.yml` carry no
  secret, so a fork's pull request still runs both in full.
```

with:

```markdown
- `AVALONIA_TOOLS_LICENSE_KEY`, the repository secret Parcel needs to run at all, is scoped to the `parcel pack`
  steps alone, by `env:` on each step rather than job- or workflow-wide. The four macOS signing secrets follow the
  same rule (see "macOS signing and notarization" below). `ci.yml` and `platforms.yml` carry no secret, so a fork's
  pull request still runs both in full.
```

- [ ] **Step 10: docs/ci-and-release.md, the gates list**

After item 3 of "Gates on each package" (the one ending `…get through Skia initialisation at all.`), insert:

```markdown
4. **`verify-notarized.sh`**, macOS only and on both RIDs: the app inside the ZIP and the DMG are signed by the
   project's team, notarized, and stapled (see "macOS signing and notarization" below). The step after it proves
   each of its checks can still fail.
```

Then replace the paragraph that begins `Steps 2 and 3 run only for` and ends `…#8-testing-and-verification)).` with:

```markdown
Steps 2 and 3 run only for `osx-arm64`, `linux-x64` and `win-x64`. The other three are cross-packaged on runners
that cannot execute them: `osx-x64` would need Rosetta on the publish runner, `linux-arm64` is packaged on an x64
runner, and `win-arm64`'s app is arm64 on an x64 runner. That limit is structural, and adding Rosetta to a publish
job is deliberately not done. For those three, `verify-bundled-engine.sh` is the only CI gate that looks at the
engine — `osx-x64` also gets step 4, which executes nothing — and launching them is left to the manual pass before
a release: each headline archive, downloaded from a rehearsal run, extracted on the platform it targets and used to
connect to a real host
([packaging spec, section 8](superpowers/specs/2026-09-08-lizterm-m3e-packaging-design.md#8-testing-and-verification)).
```

- [ ] **Step 11: docs/ci-and-release.md, the signing section**

Replace everything from the heading `### macOS signing and \`Entitlements.plist\`` up to, but not including, `### Publishing` with the text below. Where it says **[ZIP paragraph]**, put the one paragraph from the list after it that matches Task 4's outcome.

````markdown
### macOS signing and notarization

Every macOS package is signed with the project's Developer ID, notarized by Apple and stapled, so it opens like any
other download. Signing is mandatory: the macOS job's first step, "The signing secrets are present", fails the run
when any of the four secrets below is missing, and nothing falls back to an ad hoc build — which, without the
entitlement described at the end of this section, would not launch.

**Configuration.** `LizTerm.parcel`'s `MacOsSettings` names the team (`TeamId`) and the two credential types,
`"SigningCredentialsType": "P12Certificate"` and `"NotaryCredentialsType": "AppleAccount"`, and leaves the
credentials themselves undefined. Parcel reads an automatic environment variable only for a setting the `.parcel`
file does not define, so the Package step supplies them: `PARCEL_MACOS_SIGNING_P12_CERTIFICATE` (a path — the step
decodes the P12 into `$RUNNER_TEMP` and deletes it on exit), `PARCEL_MACOS_SIGNING_P12_PASSWORD`,
`PARCEL_MACOS_NOTARY_APPLE_ID` and `PARCEL_MACOS_NOTARY_APP_PASSWORD`. Parcel offers no App Store Connect API key
for notarization; besides a local keychain profile, an Apple ID with an app-specific password is its only route. The
Team ID is written only in `LizTerm.parcel`, and the gate reads it from there.

**Secrets.** Set by the account owner, and reaching only the steps that need them:

| Secret | Holds |
| --- | --- |
| `MACOS_SIGNING_P12_BASE64` | The Developer ID Application certificate and its private key, exported from Keychain Access as a password-protected `.p12`, base64-encoded |
| `MACOS_SIGNING_P12_PASSWORD` | That export password |
| `MACOS_NOTARY_APPLE_ID` | The developer account's Apple ID |
| `MACOS_NOTARY_APP_PASSWORD` | An app-specific password for that Apple ID |

The certificate expires on **2027-02-01**. Packages signed before then keep launching after it, because each
signature carries a secure timestamp; the first release after that date needs a renewed certificate and a new
`MACOS_SIGNING_P12_BASE64`.

**The ZIP's app.** [ZIP paragraph]

**The gate.** `native/build/verify-notarized.sh` checks the app inside every macOS ZIP and every DMG, on both RIDs;
it executes nothing, so `osx-x64` is covered on the arm64 runner. Each of its three checks was measured on macOS
26.6 to be independent of the others:

- **Team ID** — every Mach-O file in the bundle carries the team's identifier. Strict verification passes a bundle
  with an ad hoc sibling library, which is the incident below.
- **Stapled** — `xcrun stapler validate`. `spctl` cannot stand in for it: with the ticket (`Contents/CodeResources`,
  outside the code seal) deleted, the signature stays valid and `spctl` still reports
  `source=Notarized Developer ID`, having looked the ticket up online. A user whose first launch is offline is
  refused.
- **Notarized** — `spctl` must report `accepted` *and* `source=Notarized Developer ID`. A file with no quarantine
  flag, which is every file on a CI runner, is `accepted` with `source=Developer ID` when it is signed but was never
  notarized. So a check for "accepted" cannot fail on the likeliest regression: a missing or misnamed notary
  variable, which makes Parcel log "Notarization credentials are not set — skipping" and carry on.

The next step, "The notarization gate can fail", proves each check still bites: an ad hoc copy of the shipped app
must fail the Team ID and notarization checks, and a copy with its ticket deleted must fail the stapling check, each
matched on the check's own message rather than on a non-zero exit.

**Why there is no `Entitlements.plist`.** Until v0.4.1 packages were ad hoc signed: Parcel signed the executable
with the hardened runtime (`flags=0x10002(adhoc,runtime)`) and the bundled `libSkiaSharp.dylib`,
`libHarfBuzzSharp.dylib` and `libAvaloniaNative.dylib` plain ad hoc (`flags=0x2(adhoc)`). An ad hoc signature
carries no Team ID, and the hardened runtime's library validation refuses to `dlopen` a sibling whose Team ID does
not match the process's, so every bundled dylib failed to load and Avalonia died before it could open a window
(`DllNotFoundException: libSkiaSharp`, "different Team IDs"). A packaged build like that once passed every gate and
launched clean in every rehearsal — until someone actually ran it. The fix then was an `Entitlements.plist` at the
repository root carrying `com.apple.security.cs.disable-library-validation`, which Parcel merged with the five
entitlements it always synthesizes (`network.client`, `network.server`, `files.user-selected.read-write`,
`files.bookmarks.document-scope`, `cs.allow-jit`). With every Mach-O now signed under one Team ID, library
validation has nothing to refuse, so the file is gone and the protection it switched off is back on. Both halves
were reproduced before it was deleted: the v0.4.0 bundle, left ad hoc without the entitlement, dies exactly as
above; re-signed with the Developer ID and still without it, it launches and notarizes.

- Dropping the hardened runtime would also have fixed the crash, but notarization requires the hardened runtime.
- `codesign --verify --deep --strict` passes on a bundle that cannot launch, because library validation is a
  runtime refusal, not a signature-integrity failure. That gap is why `verify-app-launches.sh` exists.
- b3270 escaped the failure not by being signed differently but by being a child process rather than something the
  app loads — which is also why none of the engine gates could have caught it.

````

The **[ZIP paragraph]**, by outcome:

- **(a):** `Parcel staples the app inside the ZIP as well as the DMG (measured on the v0.4.1 rehearsal), so nothing is added after packaging.`
- **(b):** `Parcel notarizes and staples the DMG but leaves the app inside the ZIP unstapled (measured on the v0.4.1 rehearsal). The DMG's submission already covers that app — a ticket is keyed by code-directory hash, which both copies share — so the step "The ZIP's app is stapled" staples it without a second submission and rebuilds the ZIP with \`ditto -c -k --sequesterRsrc --keepParent\`, before any gate extracts it.`
- **(c):** `Parcel notarizes and staples the DMG but not the app inside the ZIP, and no ticket covers that copy (measured on the v0.4.1 rehearsal). The step "The ZIP's app is stapled" submits it with \`xcrun notarytool\`, using the same Apple ID credentials, staples it, and rebuilds the ZIP with \`ditto -c -k --sequesterRsrc --keepParent\`, before any gate extracts it.`

Write each as ordinary Markdown: the backslashes above only escape the backticks inside this list.

- [ ] **Step 12: Run the check and watch it pass**

Run: `bash "$SPIKE/check-task6.sh"`

Expected: no output.

- [ ] **Step 13: Commit and push**

```bash
git add README.md docs/user-guide.md docs/ci-and-release.md CLAUDE.md .github/workflows/release.yml \
  native/build/verify-app-launches.sh
git commit -F - <<'EOF'
Retire the macOS first-run instructions and document signing and notarization

A notarized build opens like any other download, so the README, the release notes and
the user guide drop the System Settings steps; the Windows instructions are unchanged.
docs/ci-and-release.md's "macOS signing and notarization" replaces the Entitlements.plist
section: the secrets, the gate and the two spctl traps it exists for, the certificate's
expiry, and why the entitlement could go.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
git push
```

---

## Task 7: As built, the manual pass, and hand-off

**Files:**
- Modify: `docs/superpowers/specs/2026-09-11-lizterm-macos-notarization-design.md` (§6 only)

**Interfaces:**
- Consumes: the rehearsal run ID and outcome from Task 4 (and Task 5's re-run, if any); every deviation made in Tasks 1 to 6.
- Produces: a pull request ready for Robert to merge, and the facts he needs to tag.

- [ ] **Step 1: Write §6 of the spec**

Replace:

```markdown
## 6. As built

Nothing yet. Implementation records here what the rehearsal found for §3.4, and every place this text turned out
to be wrong.
```

with a §6 in this shape:

```markdown
## 6. As built

**§3.4 outcome.** <one of the three sentences below>

**Deviations from this text.** <one bullet per place Tasks 1 to 6 departed from §3, each saying what changed and
why; or the single word "None.">
```

The §3.4 sentence, by outcome, naming the run:

- **(a):** `Outcome (a): Parcel staples the app inside the ZIP as well as the DMG. Rehearsal run <RUN>: every gate and every proof passed on both RIDs with no step added.`
- **(b):** `Outcome (b): Parcel staples the DMG only. Rehearsal run <RUN> failed the ZIP's app on the stapling check alone, with spctl already reporting it notarized, so the DMG's submission covered it; run <RUN2>, with "The ZIP's app is stapled", passed everything on both RIDs.`
- **(c):** `Outcome (c): Parcel staples the DMG only, and nothing covers the ZIP's copy. Rehearsal run <RUN> failed the ZIP's app on both the stapling and notarization checks; run <RUN2>, with "The ZIP's app is stapled" submitting it, passed everything on both RIDs.`

`<RUN>` and `<RUN2>` are the real run IDs from Tasks 4 and 5, filled in when this is written.

- [ ] **Step 2: Commit and push**

```bash
git add docs/superpowers/specs/2026-09-11-lizterm-macos-notarization-design.md
git commit -F - <<'EOF'
Record what the v0.4.1 rehearsal measured in the notarization spec

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
git push
```

- [ ] **Step 3: Robert's manual pass**

Ask Robert to run the check the spec's §4 step 4 describes. Open the green rehearsal's page with `gh run view "$RUN" -R coffeemuse/LizTerm --web`, then:

1. Download the `packages-osx-arm64` artifact in the browser, so it arrives quarantined as a user's download would, and double-click to unpack it.
2. Unpack `LizTerm-osx-arm64-0.4.1.zip` and open `LizTerm.app`.
3. Open `LizTerm-osx-arm64-0.4.1.dmg`, drag LizTerm to a scratch folder, and open that copy.

Expected each time: at most macOS's single confirmation for a downloaded app, then the splash and the Sessions list — no "Move to Trash" dialog and no trip to System Settings. `osx-x64` is covered by the gate alone, as its launch always has been. If either open is blocked, stop: the gate passed something it should not have, and that is a finding for the spec.

- [ ] **Step 4: Hand off**

Once the manual pass is clean, mark the pull request ready with `gh pr ready -R coffeemuse/LizTerm`, then tell Robert:

- the pull request's URL, its checks (`gh pr checks -R coffeemuse/LizTerm`), and the §3.4 outcome;
- that merging is his (the project merges with a merge commit, `gh pr merge --merge`, and deletes the branch);
- that after the merge, the annotated tag `v0.4.1` ("LizTerm 0.4.1") on the merge commit runs `release.yml` for real and leaves a draft release, which he publishes himself after his usual check. Tag only if he asks.
