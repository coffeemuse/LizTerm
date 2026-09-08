# LizTerm Milestone 3, plan 3e: packaging and release

Date: 2026-09-08. Parent spec: `2026-09-03-lizterm-v1-design.md` (section 8.1 names all six targets; section
8.4 asks for `dotnet publish` self-contained per runtime identifier, a macOS app bundle, archives, and GitHub
Releases). Predecessors: `2026-09-06-lizterm-m3-ci-design.md` (plan 3a, whose `platforms.yml` this plan
restructures), `2026-09-07-lizterm-m3c-linux-engines-design.md` (plan 3c, whose pinned-source, static-link and
prove-the-gate-rejects discipline this plan extends to macOS) and
`2026-09-07-lizterm-m3d-windows-engine-design.md` (plan 3d, which settled that `win-arm64` ships the `win-x64`
engine and left placing it to this plan). Status: approved in discussion on 2026-09-08; awaiting review of
this text.

## 1. Purpose

Every engine LizTerm needs exists and is proven to start, except one: `osx-x64`. Nothing, however, is
*shipped*. There is no release workflow, no app bundle, no icon, no LICENSE, and the csproj rule that bundles
the engine can only ever bundle the build host's own. A hobbyist cannot download LizTerm; they can only clone
it and build it, which is the audience this project does not have.

This plan is the difference between "the code is done" and "a hobbyist can download LizTerm". It produces six
self-contained publishes with the right engine in each, packages them with Avalonia Parcel, and uploads them
to GitHub Releases on a tag.

Milestone 3 after this plan: **3f**, the scheduled integration lane against a Hercules TK4-/TK5 container.

Decisions taken in the brainstorm on 2026-09-08:

- **v1 ships unsigned.** An Apple Developer ID is available and will be used, but not for v1. Notarization
  becomes the first post-v1 item rather than an open-ended "later", because macOS 15 made the unsigned
  experience materially worse (section 2.3).
- **Parcel owns packaging, gated on a spike.** Parcel is the only route on which "add notarization later" is a
  configuration change rather than a new pipeline. Its cost — a proprietary licence key on the critical path
  of releasing an open-source project — is real, is confined to one workflow, and is checkable before this
  design is built (section 5.1). The hand-rolled alternative is recorded as a contingency, not maintained as a
  second path (section 5.5).
- **Archives are the headline download; installers are the second row.** A ZIP per platform is what the
  release notes point a first-time user at. DMG, DEB, RPM and NSIS sit below them for people who want them.
- **`osx-x64` is a cross build on an arm64 Mac, not an Intel runner.** `macos-13` is retired and
  `macos-15-intel` is scheduled to disappear in August 2027, so the runner route is a stopgap with a known
  expiry. Cross-building matches what `win-x64` and the Linux engines already do.
- **Both macOS engines link OpenSSL built from pinned source.** Homebrew cannot supply an x86_64 OpenSSL on an
  Apple Silicon Mac, so the cross build needs a source build regardless; giving only the new leg one would
  leave the two Mac engines linking different OpenSSL versions from different provenances, with only one
  pinned. Plan 3c's reasoning — a statically linked library never receives the distribution's security
  updates, so its version has to be ours to bump deliberately — applies to Homebrew exactly as it applies to
  AlmaLinux.
- **Packaging runs on each platform's native runner.** This is a deliberate non-use of a feature the licence
  pays for (section 5.2).

## 2. Facts that shape the design

### 2.1 What Parcel is, and what it costs

Parcel is a .NET tool (`dotnet tool install --global AvaloniaUI.Parcel`) with a GUI and a CLI. The CLI is
**not** in the free Community edition, which is additionally single-platform, GUI-only, capped at 200 MB of
output, and adds Parcel branding to installers. Everything this plan needs — the CLI, cross-platform
packaging, signing, notarization — is the paid tier, activated by `AVALONIA_TOOLS_LICENSE_KEY` or
`--license-key`.

Avalonia's Schedule 1A states that Plus, Pro and Enterprise licences allow installation on automated build
machines and CI servers without a separate Named User licence, provided the build server is used only for
automated compilation, packaging and deployment and every individual who interactively uses the software holds
a licence. A repository secret is therefore a licensed use, not a workaround.

Against that: `AvaloniaUI/AvaloniaPro#81` records a Plus subscriber whose build server rejected their portal
key with `This subscription doesn't provide online license keys` after it had previously worked. That is the
specific failure this plan's spike exists to rule out before anything is built on it.

The CLI is `parcel pack ./LizTerm.parcel -r <rid> -p <format> -o <dir>`, with `-r`/`-p` repeatable and
`--no-build` consuming an existing publish tree. The `.parcel` file's schema is not publicly documented; the
intended workflow is that the GUI authors it once and the CLI consumes it headlessly. This plan follows that
workflow: the file is generated on the developer's Mac, committed, and never touched by CI.

### 2.2 The engine copy is keyed on the wrong RID

`src/LizTerm.App/LizTerm.App.csproj` uses `$(NETCoreSdkRuntimeIdentifier)` — the *host's* RID — for both the
source directory and the link path. Publishing `-r linux-arm64` on an x64 runner therefore bundles nothing at
all, silently: the `Condition="Exists(...)"` simply fails and the copy does not happen.

Those two uses are also no longer the same value. `B3270Locator.Candidates` resolves
`runtimes/<RuntimeInformation.RuntimeIdentifier>/native/`, so a `win-arm64` app looks under
`runtimes/win-arm64/native/` — and plan 3d settled that what belongs there is the **x64** binary, run under
Windows 11's emulation. The source directory and the link path are two different RIDs.

### 2.3 macOS 15 closed the Gatekeeper bypass

The parent spec's plan was that "release notes carry the right-click Open instruction until then". That
instruction no longer works. macOS 15 removed the Control-click override for software that is not signed and
notarized, and 15.1 closed the remaining routes. A user must now launch the app, have it refused, open System
Settings > Privacy & Security, and click "Open Anyway" under the Security heading — a button that appears only
for roughly an hour after the failed launch, so a user who goes looking later has to trigger the refusal
again.

This does not change the decision to ship v1 unsigned, but it changes what the release notes must say, and it
is why notarization is named as the first post-v1 item rather than left undated.

### 2.4 Intel Mac runners are ending

`macos-13` is retired. `macos-15-intel` exists and is stated to be the last x86_64 macOS image on GitHub
Actions, available until August 2027. Any design that reaches `osx-x64` through a native Intel runner acquires
that date as its own expiry.

### 2.5 Two properties of GitHub Actions this plan relies on

**`actions/cache` is repository-scoped, not workflow-scoped.** The engine caches `platforms.yml` populates are
restorable by a release run, so a tagged release cut from a green `main` restores five engines rather than
building them. This is what makes calling the engine jobs from a second workflow affordable.

**`actions/download-artifact` does not preserve the executable bit.** The README already documents this for
humans downloading an engine by hand. Inside `platforms.yml` it has never mattered, because each engine job
builds and uses its binary without an artifact round trip. This plan introduces exactly that round trip, and a
missing `chmod +x` would ship macOS and Linux releases whose engine `B3270Locator.Find` rejects with "is not
executable" — a failure visible to a user at connect time and to nobody during the build.

## 3. The pipeline

### 3.1 `engines.yml`, a reusable workflow

The five engine builds and their gates move into `.github/workflows/engines.yml` with `on: workflow_call`.
`platforms.yml` becomes a trigger wrapper that calls it on its existing push, dispatch and path-filtered
pull-request events. `release.yml` calls the same workflow.

The alternative — a release workflow that rebuilds the engine logic — would give the repository two
definitions of "an engine that has been proven to start", which is precisely the drift plan 3d's two-name
artifact hop exists to prevent. One definition, two callers.

`engine-macos` gains a second matrix leg for `osx-x64`, mirroring `engine-linux`'s two-leg shape. Both legs
run on `macos-15`; the difference between them is the target architecture, not the runner.

The check names branch protection sees will change when jobs move under a reusable workflow. `ci.yml`'s `test`
is the only required check and is untouched, but the platform check names must be re-recorded in CLAUDE.md
after the first run, the way plan 3c recorded `engine-linux (ubuntu-24.04, linux-x64)`.

### 3.2 `release.yml`

Triggered on `push: tags: ['v*']`, plus `workflow_dispatch` for rehearsal against a non-tag ref (which
produces artifacts and no release).

```
tag v*  ->  release.yml
              engines      uses: ./.github/workflows/engines.yml     5 engines, gate-proven
              publish      dotnet publish -r <rid>  x6               engine copied in by target RID
              package      parcel pack              x6               archives + installers
              release      gh release create                          archives first, installers second
```

`publish` and `package` are one job per platform, on that platform's native runner, each handling its own
RIDs: `macos-15` does `osx-arm64` and `osx-x64`, `ubuntu-24.04` does `linux-x64`, `ubuntu-24.04-arm` does
`linux-arm64`, `windows-latest` does `win-x64` and `win-arm64`.

Each publish job downloads the engine artifacts `engines.yml` produced into `native/out/<rid>/` and, on macOS
and Linux, runs `chmod +x` over them before building — the fix for section 2.5's second property, applied at
the one point in the pipeline where an engine crosses an artifact boundary. The Windows job downloads
`b3270-win-x64` only: it feeds both `win-x64` and `win-arm64`, since section 4.1's source RID maps the latter
to the former, and a PE has no executable bit to restore.

A tag whose version does not match `Directory.Build.props`'s `Version` fails the job before anything is
built. A release built from a tag that disagrees with the tree is a release nobody can reproduce.

## 4. Build-side changes

### 4.1 `LizTerm.App.csproj`: a source RID and a target RID

Two properties replace the single `$(NETCoreSdkRuntimeIdentifier)`:

- **Target RID** — `$(RuntimeIdentifier)` when publishing for one, `$(NETCoreSdkRuntimeIdentifier)` otherwise
  (development builds, the test projects, `dotnet run`). This is the link path, and it must equal what
  `RuntimeInformation.RuntimeIdentifier` will report at run time.
- **Source RID** — the target RID, except `win-arm64`, which maps to `win-x64`. This is the directory under
  `native/out/` the copy reads from.

The `Condition="Exists(...)"` moves to the source RID. The existing behaviour — no engine directory, no copy,
`BackendUnavailableException` at connect time with `LIZTERM_B3270_PATH` as the documented override — is
unchanged for developers.

### 4.2 `build-macos.sh`: two architectures, pinned OpenSSL

The script stops deriving its RID from `uname -m` and takes it as an argument (defaulting to the host's, so
existing developer usage and `native/build/build-macos.sh` in the README keep working). For `osx-x64` on an
arm64 host it configures with `--host=x86_64-apple-darwin` and `CC="clang -arch x86_64"`.

OpenSSL comes from `fetch-openssl.sh` for both architectures, through `shared-fetch-tarball.sh` and
`shared-sha256.sh`, exactly as `build-linux.sh` uses them — configured per architecture (`darwin64-arm64-cc`
and `darwin64-x86_64-cc`) into a per-architecture prefix under `native/build-tmp/`. The staging directory that
currently exists to hide Homebrew's dylibs from the linker disappears with Homebrew: a source build with
`no-shared` produces only archives.

Each prefix gains the `.pin` stamp `build-linux.sh` already uses, holding the SHA-256 of `fetch-openssl.sh`
and of `build-macos.sh`, and reused only when both the archive and a matching stamp are present — for the
reason plan 3c gives: a prefix has two owners, the fetcher holding the version and checksum and the build
script holding the configure flags that shape what is built from them.

`shared-*.sh` is in both engine cache keys by construction, and `fetch-openssl.sh` is currently in the Linux
key only. It must be added to the macOS key by this plan, or a bumped OpenSSL would rebuild Linux and silently
reuse a cached Mac engine linking the old one.

Removing Homebrew also removes the "Ensure OpenSSL 3 is installed" step from the macOS CI job and reduces the
README's developer prerequisites to the Xcode command line tools.

### 4.3 `verify-macos.sh`: the architecture check

The gate gains a first check: `lipo -archs` must report exactly the expected architecture, passed as an
argument. It runs before the dependency scan, so a wrong-architecture binary is rejected by a message naming
the mistake.

This is plan 3d's lesson arriving on the platform that now needs it. A cross build's most plausible failure is
emitting the wrong architecture, and an x86_64 b3270 links exactly the same system libraries an arm64 one
does — `otool -L` cannot see the difference, just as the import allowlist could not see a 32-bit PE.

### 4.4 An icon

The app has no icon: there is no `Icon=` anywhere in `src/LizTerm.App`, and `Assets/` holds the 3270 font and
its licence and nothing else. One source image (1024px PNG or SVG) is committed and rendered to `.icns`,
`.ico`, and hicolor PNGs for the `.desktop` entry. `Window.Icon` is set so Linux and Windows title bars and
task switchers are not blank, and the file feeds Parcel's per-platform icon settings.

### 4.5 Version stamping

`AppVersion.Current` already reads the informational version and `AboutWindow` already renders it, so this is
`-p:Version=` derived from the tag plus the agreement check in section 3.2. No UI work.

## 5. Parcel

### 5.1 The spike, which gates this spec

Before implementation begins, one session establishes three things the public documentation does not answer:

1. **The licence key activates the CLI headlessly**, on a Linux runner-like environment, from an environment
   variable — the failure `AvaloniaPro#81` records.
2. **Which RID and format combinations actually work.** `linux-arm64` with DEB and RPM, and `win-arm64` with
   NSIS, are the two this plan depends on and the two the documentation does not confirm.
3. **The `.parcel` schema**, by authoring `LizTerm.parcel` in the GUI and reading the result.

Its output is a committed `LizTerm.parcel` and a recorded table of working combinations. If (1) fails, section
5.5 applies. If (2) fails for a combination, that platform ships its headline archive only, and the spec
records which.

### 5.2 Packaging runs on native runners

Parcel Plus can build a DMG from Linux and sign Apple bundles off-macOS through `rcodesign`. This plan does
not use that, because `engines.yml` already puts the pipeline on `macos-15`, `ubuntu-24.04`, `ubuntu-24.04-arm`
and `windows-latest`. Cross-packaging would buy nothing and would add variables — cross-platform Mach-O ad hoc
signing, Unix modes written by a Linux host into a macOS bundle — whose failures appear to a user opening a
download rather than to CI. The capability stays available for a future in which the runner set shrinks.

### 5.3 Formats

| Platform | Headline | Second row |
| --- | --- | --- |
| macOS | ZIP containing the `.app` | DMG |
| Linux | ZIP | DEB, RPM |
| Windows | ZIP | NSIS |

The formats are chosen for what works unsigned. **PKG is skipped**: a macOS installer package that is neither
signed nor notarized is a worse experience than the DMG beside it, and the reason to want one — Mac App Store
distribution — brings the app sandbox with it (section 10). **MSIX is skipped**: an unsigned MSIX cannot be
installed at all without developer mode, so it is a format that does not function under this plan's premise.

RPM is included because the glibc floor is RHEL 8's: plan 3c chose the AlmaLinux 8 base precisely for that
distribution family, and shipping only a DEB would skip the audience the floor was designed around.

Parcel emits ZIP, not `.tar.gz`, so the Linux headline is a ZIP rather than the tarball the parent spec named.
Parcel preserves Unix modes in ZIPs, which is the property that matters for b3270; adding a tar step for one
platform would put a second archive mechanism in the pipeline to satisfy a convention.

### 5.4 The key is confined to one workflow

`AVALONIA_TOOLS_LICENSE_KEY` becomes a repository secret referenced only by `release.yml`, which is triggered
by tags on the main repository. `ci.yml` and `platforms.yml` remain key-free, so contributors, fork pull
requests and every ordinary check keep working with no licence at all. The proprietary dependency sits on
releases and nothing else, which is the narrowest place it can sit.

### 5.5 The contingency, recorded and not built

If the spike's first question fails, the fallback is a script that assembles `Contents/MacOS`,
`Contents/Resources` and an `Info.plist` into a `.app`, and archives the six publish trees directly. It is
written here so that it is a decision already taken rather than one made under a tag. It is explicitly **not**
built alongside Parcel: a maintained second packaging path would hand-roll the exact work Parcel exists to do
and then run Parcel beside it, paying for both.

## 6. The gate

Five engines now cross an artifact boundary into six publish trees. The mistake this pipeline can make
silently is putting a correctly-shaped **wrong** engine into a tree: it builds, packages, uploads, and fails
at connect time on a user's machine.

### 6.1 In the publish job, per RID

`runtimes/<rid>/native/b3270` (or `b3270.exe`) must exist in the publish tree, must be executable on Unix, and
must report the architecture that RID expects — `lipo -archs` on macOS, `file` on Linux, `objdump -f` on
Windows. The existence check also settles what this plan declines to assume: that `dotnet publish` preserves a
linked `runtimes/` path in a self-contained layout.

The architecture check is the load-bearing one. It is the only check that distinguishes a wrong engine from a
right one, because a wrong engine of the right shape passes every other check in this pipeline.

### 6.2 After packaging

The headline ZIP is extracted on the runner that made it and `shared-verify-tls.sh` is run against the engine
inside it. One step proves three things: the archive round trip preserved the executable bit, the binary still
starts, and — on macOS — the `.app` and ZIP round trip did not invalidate the ad hoc signature the SDK and the
linker applied.

Two combinations cannot run this check where they are built: `osx-x64`, because the runner is arm64 and
Rosetta's presence on `macos-15` images is unconfirmed, and `win-arm64`, because the runner is x64. Those get
section 6.1's architecture assertion only. This is the same split plan 3d made between `engine-windows` and
`test-windows` — each machine proves the half it can — and the spec states it rather than implying six proofs
where there are four.

### 6.3 Proving the gate rejects

The architecture check gets one negative case: an engine placed in the wrong RID's tree must be rejected, and
by the message that check prints rather than by a bare non-zero exit. A guard that cannot fail is not a guard,
and this is the guard whose silent failure ships a broken release.

## 7. Repository changes

- `.github/workflows/engines.yml` — new; the engine jobs and gates, `on: workflow_call`.
- `.github/workflows/platforms.yml` — reduced to triggers calling `engines.yml`.
- `.github/workflows/release.yml` — new; publish, package, release.
- `src/LizTerm.App/LizTerm.App.csproj` — source RID and target RID.
- `src/LizTerm.App/Assets/` — the icon source and its derived forms; `Window.Icon` in the views.
- `native/build/build-macos.sh` — architecture argument, cross build, pinned OpenSSL, `.pin` stamps.
- `native/build/verify-macos.sh` — the architecture check.
- `native/build/verify-bundled-engine.sh` — new; section 6.1's gate, as a script rather than inline YAML,
  because three publish jobs call it and a gate that cannot be run locally cannot be tested.
- `native/build/fetch-openssl.sh` — added to the macOS engine cache key.
- `LizTerm.parcel` — new, generated by the GUI in the spike.
- `LICENSE` — new; the choice is the repository owner's.
- `README.md`, `CLAUDE.md` — developer prerequisites lose Homebrew; the release process, the new check names,
  and the corrected macOS first-run instruction are recorded.

## 8. Testing and verification

The existing suite is unaffected: no Core, Backend or App behaviour changes. `EngineSmokeTests` continues to
resolve the bundled engine from the test output, and the csproj change preserves the development path it
depends on.

What is new is verified by the pipeline itself, in section 6, plus one manual pass before the first tag: each
headline archive downloaded from a rehearsal run on the platform it targets, extracted, and used to connect to
a real host — the check no CI job in this plan performs, because none of them can run a GUI.

## 9. Prerequisites

Two items block the first tag and are the repository owner's to supply: the **LICENSE** choice, and an **icon
source**. Neither blocks starting the implementation.

## 10. Out of scope

Code signing, notarization and Authenticode — deferred by decision, and named as the first post-v1 item.
PKG and Mac App Store distribution: the App Store route requires the app sandbox, and a bundled child process
like b3270 is permitted there only inside the bundle, signed with the same team identifier, inheriting the
sandbox — real work rather than one more output format. Package managers (Homebrew cask, winget, Flatpak,
AUR). Auto-update. A native `win-arm64` engine, which still needs upstream host-detection patches. The
scheduled integration lane and its Hercules container, which is plan 3f.
