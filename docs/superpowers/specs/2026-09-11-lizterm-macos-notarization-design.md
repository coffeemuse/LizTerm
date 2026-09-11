# LizTerm v0.4.1: macOS signing and notarization

Date: 2026-09-11. Parent spec: `2026-09-03-lizterm-v1-design.md`. Predecessor:
`2026-09-08-lizterm-m3e-packaging-design.md`, whose §1 ruled that v1 ships unsigned and whose §10 names
notarization as the first post-v1 item. Issue: #36, macOS only since 2026-09-11; Windows Authenticode moved to #65.
Status: approved in discussion on 2026-09-11; awaiting review of this text.

## 1. Purpose

v0.4.0 is published and the repository is public. A Mac user who downloads LizTerm today meets the first-run block
the packaging spec measured (its §2.3): the file is refused, and allowing it takes System Settings > Privacy &
Security > **Open Anyway**, a second confirmation, and Touch ID or an administrator password — per file, so twice
for the DMG. The README and every release body spend four paragraphs explaining it. Notarization removes all of it.

This slice signs every macOS package with the project's Developer ID, notarizes and staples it, proves that in CI
with a gate that can fail, and ships the result as **v0.4.1** with no other change.

Decisions taken in the brainstorm on 2026-09-11:

- **Ship as 0.4.1, on its own.** Not folded into the next feature release: the point is what a newcomer meets,
  and that should not wait on unrelated work.
- **Signing is mandatory.** A release run without the signing secrets fails; there is no unsigned fallback.
  `Entitlements.plist` is deleted (§3.5), which makes an unsigned macOS package unlaunchable, so the two
  decisions stand or fall together.
- **macOS only.** #36 was split; Windows Authenticode is #65, unscheduled.
- **The macOS first-run instructions go** from the README, the release notes and the user guide, since a
  notarized build opens like any other download.

## 2. What the spike measured

On 2026-09-11, on macOS 26.6, against the published `LizTerm-osx-arm64-0.4.0.zip` (SHA-256
`598bbd434b09a8ad72f08cbedea1edee3b6868ef36ddaa807394ed0cb95e3a14`). Everything built for it was throwaway and
lived outside the repository.

### 2.1 Developer ID signing makes the entitlement unnecessary

The bundle was re-signed inside-out with the project's Developer ID Application certificate (team `Z9G64Y4827`): the three
dylibs (`libSkiaSharp`, `libHarfBuzzSharp`, `libAvaloniaNative`) and `runtimes/osx-arm64/native/b3270` with
`--options runtime --timestamp`, then the bundle with Parcel's five default entitlements (`network.client`,
`network.server`, `files.user-selected.read-write`, `files.bookmarks.document-scope`, `cs.allow-jit`) and
**without** `cs.disable-library-validation`.

- `codesign --verify --deep --strict`: valid. Every Mach-O reports `TeamIdentifier=Z9G64Y4827` and
  `flags=0x10000(runtime)`.
- `verify-app-launches.sh`: OK. `shared-verify-tls.sh` on the re-signed engine: OK, OpenSSL 3.5.8.

**Negative control.** The same ZIP, left ad hoc with the hardened runtime and only the entitlement removed, died
inside the gate's five seconds with `DllNotFoundException: libSkiaSharp` and "mapping process and mapped file
(non-platform) have different Team IDs" — the incident `docs/ci-and-release.md` records, reproduced. So the launch
check still bites, and the entitlement was only ever needed because an ad hoc signature carries no Team ID.

### 2.2 The bundle notarizes unchanged

`notarytool submit --wait` returned **Accepted** with no issues in about three minutes (submission
`869ed097-5be2-4637-ac81-2910a3c7e703`). The ticket covers every Mach-O in the bundle, including b3270 where it sits
today, at `Contents/MacOS/runtimes/osx-arm64/native/`. `stapler staple` worked, and the stapled bundle still passed
strict verification and the launch check. Nothing about the layout has to move.

### 2.3 Two checks that cannot stand alone

- **`spctl` says `accepted` for a build that is not notarized.** Before notarization, `spctl -a -vvv -t exec`
  printed `accepted` and `source=Developer ID` for the signed bundle, on a machine where the file carried no
  quarantine flag — which is exactly a CI runner's situation. A gate that looks for "accepted" cannot fail on a
  signed but unnotarized build, and that is the likeliest regression: a misnamed notary variable makes Parcel log
  "Notarization credentials are not set — skipping", as every pack does today, and carry on. The check has to
  match `source=Notarized Developer ID`.
- **`spctl` also passes a notarized build that is not stapled.** The stapled ticket is `Contents/CodeResources`,
  which the original bundle does not have. It sits outside the code seal, so deleting it leaves
  `codesign --verify --deep --strict` valid. With it deleted, `stapler validate` fails ("does not have a ticket
  stapled to it", exit 65) while `spctl` still prints `source=Notarized Developer ID`, having looked the ticket up
  online. A user whose first launch of that build happens offline is refused. So `stapler validate` is a check of
  its own, not implied by `spctl`.

An ad hoc re-signed copy of the notarized bundle reports `TeamIdentifier=not set` and `spctl` `rejected`.

### 2.4 Parcel 1.1.1

- **The pinned version has what this needs.** 1.1.1 was published on 2026-08-25 and is the newest release; the
  Parcel configuration reference describing the settings below was last updated on 2026-08-11. No Parcel bump.
- **Settings.** `MacOsSettings.TeamId`; `MacOsSettings.SigningCredentialsType` (`None`, `AdHoc` — the default,
  and today's behaviour — `KeyChainIdentity`, `P12Certificate`, `PemCertificate`);
  `MacOsSettings.NotaryCredentialsType` (`None`, `KeyChainProfile`, `AppleAccount`); `MacOsSettings.SignDmg`,
  default `true`. Each scalar setting has an automatic environment variable, which Parcel reads **only when the
  `.parcel` file does not define that setting**: `PARCEL_MACOS_SIGNING_P12_CERTIFICATE` (a path),
  `PARCEL_MACOS_SIGNING_P12_PASSWORD`, `PARCEL_MACOS_NOTARY_APPLE_ID`, `PARCEL_MACOS_NOTARY_APP_PASSWORD`.
- **No App Store Connect API key.** Parcel's notary credentials are an Apple ID with an app-specific password, or a
  `notarytool` keychain profile. #36 listed an API key as an option; with Parcel it is not one.
- **Stapling is documented for the DMG, not the ZIP.** Parcel's macOS page says it "can submit and staple DMG and
  PKG files". It says nothing about the `.app` inside the ZIP, which is the download the release notes recommend.
  §3.4 handles that without assuming the answer.

### 2.5 The certificate

The Developer ID Application certificate is valid from 2025-06-12 to **2027-02-01**. Signatures carry a secure
timestamp, so builds signed before that date keep launching after it; only new releases need a renewed
certificate, and with it a new `MACOS_SIGNING_P12_BASE64`.

## 3. Design

### 3.1 `LizTerm.parcel`

`MacOsSettings` gains three literal settings:

```json
"TeamId": "Z9G64Y4827",
"SigningCredentialsType": "P12Certificate",
"NotaryCredentialsType": "AppleAccount"
```

No credential is in the file. The P12 path and password, the Apple ID and the app-specific password are left
undefined, so that Parcel reads them from the environment (§2.4). The Team ID is not a secret and is written only
here; the gate reads it from here (§3.3). Linux and Windows packs ignore `MacOsSettings`.

### 3.2 Repository secrets

| Secret | Holds |
| --- | --- |
| `MACOS_SIGNING_P12_BASE64` | The Developer ID Application certificate and its private key, exported from Keychain Access as a password-protected `.p12`, base64-encoded |
| `MACOS_SIGNING_P12_PASSWORD` | That export password |
| `MACOS_NOTARY_APPLE_ID` | The Apple ID of the developer account |
| `MACOS_NOTARY_APP_PASSWORD` | An app-specific password for that Apple ID |

They are scoped like `AVALONIA_TOOLS_LICENSE_KEY`: `env:` on the steps that need them, never job- or
workflow-wide, so `ci.yml` and `platforms.yml` stay secret-free and a fork's pull request still runs both in full.
Creating and entering them is Robert's job; no credential passes through the implementer.

### 3.3 `release.yml`, the macOS publish job

The `osx-arm64`/`osx-x64` matrix job, in order:

1. **New first step, "The signing secrets are present".** It receives the four secrets and, before this job
   publishes or packages anything, fails naming each one that is empty. It has to fail: §3.5 makes an unsigned
   package unlaunchable.
2. **Package.** The step decodes `MACOS_SIGNING_P12_BASE64` to a file under `$RUNNER_TEMP`, removes it on exit,
   and passes that path and the other three secrets to `parcel pack` under Parcel's variable names. The command
   line is unchanged: `-p zip -p dmg`.
3. **The ZIP's app is stapled** (§3.4) — present only if the rehearsal shows it is needed.
4. **The three existing gates, unchanged:** the bundled engine (both RIDs), the engine start and the app launch
   (`osx-arm64` only). The launch check now also proves that the signed app starts without the entitlement.
5. **New gate, "The shipped app and disk image are notarized",** on both RIDs: `verify-notarized.sh` against the
   app extracted from the ZIP and against the DMG, with the Team ID read from `LizTerm.parcel`. It needs to launch
   nothing, so `osx-x64` gets its first gate beyond the engine's machine type.
6. **New step, "The notarization gate can fail"** (§3.6).
7. Rename and upload, unchanged.

`timeout-minutes` goes from 20 to **45**: each leg now waits on Apple once or twice, and a submission can queue.

### 3.4 The ZIP's app

What Parcel does to the `.app` inside the ZIP is measured on the rehearsal (§4), not assumed. There are three
possible outcomes:

- **(a) Parcel staples it.** Nothing is added.
- **(b) It is not stapled, but a ticket exists for it.** Notarizing the DMG submits the app inside it, and a ticket
  is keyed by code-directory hash, which the ZIP's copy shares when both were signed identically. A step runs
  `xcrun stapler staple` on the extracted app and rebuilds the ZIP with `ditto -c -k --sequesterRsrc --keepParent`
  in place of Parcel's.
- **(c) No ticket exists for it.** The same step first submits the app with `xcrun notarytool submit --wait`,
  using the same Apple ID credentials, then staples and re-zips as in (b).

Whichever applies, the gate is what enforces it, so a wrong guess fails the rehearsal instead of shipping. The
stapling step runs before the existing gates extract the ZIP, so every gate sees the ZIP that ships.

### 3.5 `native/build/verify-notarized.sh`, and the entitlement

`verify-notarized.sh <path> <team-id>`, where `<path>` is a `.app` bundle or a `.dmg`. It runs every check, prints
an `ERROR:` line naming each one that fails, and exits non-zero if any did. It runs all of them rather than stopping
at the first, so one run shows every way a package is wrong, and so the proof in §3.6 can assert each check by its
own message.

For a `.app`:

1. **Team ID.** Every Mach-O file in the bundle reports `TeamIdentifier=<team-id>` in `codesign -dv`. This catches
   a nested library or engine left ad hoc or signed by someone else, which strict verification does not.
2. **Stapled.** `xcrun stapler validate` succeeds.
3. **Notarized.** `spctl -a -vvv -t exec` output contains `source=Notarized Developer ID` — "accepted" alone is
   not evidence (§2.3).

For a `.dmg`, the same three, with the Team ID taken from the disk image's own signature and the assessment made
with `spctl -a -vvv -t open --context context:primary-signature`.

The script carries the licence header, which `RepositoryHeadersTests` requires.

**`Entitlements.plist` is deleted.** With every Mach-O signed under one Team ID, library validation has nothing to
refuse (§2.1). The entitlement switched off a real hardened-runtime protection and existed only to work around ad
hoc signatures.

### 3.6 Proving the gate can fail

This project's rule is that a guard which cannot fail is not a guard. After the gate passes, the job runs it twice
more, against copies of the extracted app, and requires each run to fail **with the expected messages** rather than
merely exit non-zero — the script also exits non-zero when `spctl` cannot reach Apple, and that must not count as
proof.

- **An ad hoc copy** (`codesign --force --deep --sign -`) must fail the Team ID and notarization checks.
- **An unstapled copy** (`Contents/CodeResources` deleted) must fail the stapling check. Nothing is asserted about
  its other two checks: its `spctl` verdict depends on reaching Apple for the ticket (§2.3), and a proof that
  flickers with the network proves nothing.

The signed but unnotarized case of §2.3 cannot be manufactured in this job without handing the signing key to one
more step. It is covered instead by the check requiring the exact `source=` string, which the ad hoc copy
exercises, and by a comment in the script recording why "accepted" is not enough.

### 3.7 Documentation

Each fact changes where it lives:

- **`README.md`, "First run".** The macOS steps, and the sentence saying builds are unsigned, go. One sentence
  replaces them: macOS builds are signed and notarized by Apple, so they open like any other download, apart from
  macOS's own one-time confirmation for something downloaded from the internet. The Windows SmartScreen paragraph
  stays as it is. The "next on the list (#36)" line goes.
- **The release-notes heredoc in `release.yml`.** The same change, in the same words.
- **`docs/user-guide.md`, "Known limitations".** "Unsigned builds" becomes a Windows-only entry.
- **`docs/ci-and-release.md`.** "macOS signing and `Entitlements.plist`" becomes "macOS signing and notarization":
  the secrets and their scoping, Parcel's settings and its environment-variable rule, the gate and both traps from
  §2.3, the ZIP outcome from §3.4 as measured, and the certificate's expiry. The incident stays, shortened, as the
  reason `verify-app-launches.sh` exists, together with why the entitlement could go.
- **Root `CLAUDE.md`.** The warning that `Entitlements.plist` is load-bearing is replaced by a pointer: read that
  section before touching `LizTerm.parcel` or the macOS job's signing steps.
- **Comments** in `release.yml` (three) and `verify-app-launches.sh` (one) that name the old section title or
  describe ad hoc signing as current.
- The design history in `docs/superpowers/` is not edited.

### 3.8 Version

`0.4.1` in `Directory.Build.props` and in `LizTerm.parcel`'s `GeneralSettings.Version`. The `version` job already
fails a run in which they disagree.

## 4. Rollout and verification

1. **Secrets.** Robert exports the certificate with its private key from Keychain Access as a password-protected
   `.p12` and adds the four secrets. The app-specific password created for the spike serves as
   `MACOS_NOTARY_APP_PASSWORD`.
2. **Local suite.** `dotnet test LizTerm.slnx` green, and `dotnet build LizTerm.slnx --no-incremental` with zero
   warnings.
3. **Rehearsal.** `release.yml` dispatched on the pull-request branch builds every package and publishes nothing.
   Both macOS legs must pass every gate, §3.6 included. The rehearsal settles §3.4, and its outcome is recorded in
   §6.
4. **Manual pass.** Robert downloads the rehearsal's `osx-arm64` ZIP and DMG through a browser, so that they arrive
   quarantined as a user's would, and opens each: at most macOS's single confirmation, and no System Settings
   detour. `osx-x64` is covered by the gate alone, as its launch always has been.
5. **Merge, then tag `v0.4.1`.** The tag runs the same pipeline and leaves a draft release; publishing it stays a
   manual step.

## 5. Out of scope

Windows Authenticode (#65). PKG and Mac App Store distribution (packaging spec §10). Package managers. Auto-update.
Moving b3270 to `Contents/Helpers` or anywhere else: §2.2 shows notarization does not need it.

## 6. As built

**§3.4 outcome.** Outcome (b), and the DMG as well. Rehearsal run 34614456892 failed on both RIDs: the app from the
ZIP failed only the stapling check, with spctl already reporting it notarized, and the DMG failed the stapling and
Team ID checks while spctl accepted it as notarized. Apple's notary log for each submission shows Parcel submits only
the DMG, and its ticket covers the DMG and every file inside it, the app included. Run 34616272784, with the step
"The ZIP's app and the disk image are stapled", passed everything on both RIDs, and run 34617469436 passed again
after the ZIP rebuild switched to `--norsrc`.

**Deviations from this text.**

- **Parcel does not staple what it notarizes (§2.4, §3.4).** Parcel notarizes only the DMG and logs "Stapling
  notarization ticket to DMG file", but the DMG it leaves in the output folder carries no stapled ticket, and neither
  does the app inside the ZIP. The step "The ZIP's app and the disk image are stapled" staples both with Apple's
  `stapler`, using the tickets Apple already issued, and first requires exactly one ZIP and one DMG.
- **A disk image's team comes from its certificate (§3.5).** Parcel signs with `rcodesign`, which leaves a disk
  image's TeamIdentifier unset although it signs the DMG with the team's certificate. `verify-notarized.sh` reads a
  `.dmg`'s team from the leaf certificate its signature names instead. A Mach-O file's team still comes from its
  TeamIdentifier, which `rcodesign` does set.
- **The gate prints less (§3.5).** On failure it prints team IDs, and only spctl's verdict and `source=` lines:
  spctl's `origin=` line names the certificate's holder, and CI logs are public.
- **The proof also covers the DMG (§3.6).** An ad hoc copy of the shipped DMG must fail the Team ID and notarization
  checks too: the gate's disk-image branch was rewritten after the first rehearsal, and §3.6's copies of the app
  cannot reach it.
- **The ZIP is rebuilt with `ditto -c -k --norsrc --keepParent`, not `--sequesterRsrc` (§3.4).** `--sequesterRsrc`
  can add `__MACOSX` entries of the runner's metadata to the public ZIP; Parcel's own ZIP has none, and nothing a
  signed bundle needs lives in an extended attribute.
- **Four comments in `release.yml` changed, not three (§3.7):** the comment above "The shipped archive carries the
  right engine" also described ad hoc signing as current.
- **The rehearsal of §4 step 3 took three runs, not one.**
