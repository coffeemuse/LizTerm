# LizTerm 0.5.0 — release scope

Date: 2026-09-12

> **The release this plans shipped as 0.5.1, not 0.5.0.** Decided 2026-09-12, by Robert, after the
> cross-platform pass. 0.5.0 was carried in the tree and rehearsed but never tagged and never published, so
> no 0.5.0 exists anywhere public; the pass found the Windows executable had no icon of its own (PR #84), and
> rather than rehearse 0.5.0 a second time the version went to 0.5.1.
>
> The objection raised was that the Releases page will show 0.4.1 then 0.5.1, and that a gap reads to a
> stranger like a withdrawn release. Robert's answer, which is the decision: the project is pre-1.0 and this
> is its first attempt at a release meant to be public before it is announced, so early hiccups are what a
> user should expect — and a visible defect in a published build costs more than a skipped version number.
> **Get it right rather than look like we did not check before publishing.** That is the standard for
> anything section 7 finds from here on, and it outranks the triage rule's letter: the rule decides what
> blocks a *tag*, not what is worth fixing before one.
>
> **This document keeps its name and its contents.** It is the record of what was scoped and why, and the
> repository does not rewrite a plan to match what the code later did. Read every "0.5.0" below as the release
> that became 0.5.1; §5.5 and §9 carry the amendments.

This is a release plan, not a design spec. It records what 0.5.0 contains, what it deliberately does not,
the decisions taken while scoping it, and the order the work happens in. Each item of new work still gets
its own design spec and implementation plan; this document only says which items those are.

## 1. The theme

**Public debut and usable.**

v0.4.1 was the last tag, on 2026-09-11. Everything before it was built and tested by the person writing it.
0.5.0 is the first release meant to be found by a stranger, installed, and used for real work.

**Definition of done:** a person who has never seen this project can find it, understand what it is, install
it on their platform, use it as a day-to-day TN3270 client, and file a bug report that is useful without a
follow-up question.

Feature gaps are acceptable in 0.5.0 and are named in the user guide's "Known limitations". Broken basics
are not. That distinction is what section 7's triage rule turns into a rule.

## 2. Decisions taken while scoping

Recorded here because each one closes something, and none of them is derivable from the code.

**2.1 Native menus are macOS-only.** The system menu bar is offered on macOS, with a preference to draw the
in-window menu instead, or both at once. On Windows and Linux the in-window menu is the only style, and the
preference group is hidden rather than disabled — there is no choice to explain. This is what PR #74 already
implements. It **withdraws issue #22** ("flip the menu default on Windows and Linux"), which is now closed
by decision rather than deferred.

**2.2 The pre-release is a rehearsal, not a tag.** `release.yml`'s `version` job fails the run unless the
tag, `Directory.Build.props` and `LizTerm.parcel` agree exactly. A `v0.5.0-rc1` tag would therefore mean
carrying `0.5.0-rc1` in both files and handing that string to Parcel's installer builders, where a semver
pre-release suffix has never been exercised. `workflow_dispatch` already exists as a rehearsal that builds
all six RIDs and every installer, creates no release, and compares nothing. That is the mechanism: no tag,
no version churn, nothing to delete afterwards. Artifacts come off the workflow run page, with the standard
seven-day retention.

**2.3 Offline documentation is bundled, rather than built into the app.** The alternative considered was a
Help > Keyboard window rendering `DefaultKeymap`'s table. Bundling the existing documentation instead means
one source of truth, a copy that matches the release it shipped with, and no second place for the keyboard
table to drift from. See section 4.1 for what this leaves undecided.

**2.4 The pre-1.0 disclaimer says nothing about platforms.** See section 5.3.

## 3. Merged, or merging — releasing, not building

These four are 0.5.0's headline items. Three are on `main` and need no further work; PR #74 is reviewed and
merges before anything else in section 9 starts. They are listed because none of them appears in the README
yet, which is section 5's problem.

| Item | Issue | PR |
|---|---|---|
| Settings store, `SettingsLayers`, and the Preferences window | — | #67 |
| Terminal bell, visual and audible | #47 | #68 |
| On-screen PF/PA keypad, docked at the bottom or the right | #31 | #69 |
| Menu style as a preference, macOS-gated, with a `Both` mode | #70 | #74 |

## 4. New work

Three items. One of them is not small.

### 4.1 Help menu links, and bundled offline documentation (#48)

> **Done, 2026-09-12.** Both menus' Help now reads User Guide, Project on GitHub, Report an Issue..., Releases,
> a separator, then the pre-existing Wire Log / Show Wire Logs..., a separator, About LizTerm.... `IUriOpener`
> (`Files/`) sits beside `IFolderOpener`, injected the same way, with `FakeUriOpener` in tests. The design spec
> answered the open questions in the four bullets below: `docs/user-guide.md` alone ships, as HTML generated by
> a test-only converter (`tests/LizTerm.Core.Tests/Documentation/UserGuideHtml.cs`) and committed at
> `src/LizTerm.App/Assets/Docs/user-guide.html`, an embedded resource `Documentation/UserGuide.cs` extracts to
> the temp directory and opens in the user's browser; a platform that cannot open it gets an error naming the
> guide's GitHub URL instead. `UserGuideAssetTests` holds the committed HTML to its source and the source to
> what the converter can render, so an edit that outruns regeneration fails locally, not in CI.

The links themselves are the cheap half and the shape is already proven: `AvaloniaFolderOpener` calls
`Launcher.LaunchDirectoryInfoAsync` for Show Wire Logs, so an `IUriOpener` beside `IFolderOpener` is the
same constructor injection, the same fake in tests, and no new dependency.

The bundled documentation is the half that needs a design pass of its own, because none of these questions
has an answer yet:

- **Which files ship.** `docs/user-guide.md` certainly. `README.md` probably. `development.md`,
  `ci-and-release.md`, `engines.md` and `architecture.md` are developer documentation and should not.
- **What format.** Raw Markdown opens in a text editor on Windows with its relative links dead. HTML opens
  in a browser on every platform with its links intact, but a converter has to run in three publish jobs.
- **Where it lands** in each of the six packages, across a macOS `.app` bundle, a `.deb`, an `.rpm`, a
  Windows installer and three ZIPs.
- **How the app locates it** at runtime, and what it does when the copy is absent — which is the normal case
  for `dotnet run` from a source tree.

The mechanism is not in doubt: `b3270` already ships as a `None` item with `CopyToOutputDirectory` beside a
single-file executable, and `PublishSingleFile` leaves loose content files next to it. The format and layout
decisions are what is missing.

**This item requires its own design spec before it is planned.** It is the schedule risk in this release.

Issue #48's body also needs correcting before anyone plans from it: it states there is no user documentation
anywhere in the project, which stopped being true when `docs/user-guide.md` was written. The user guide now
carries Keyboard, Menus, Preferences and Known limitations sections, which is most of what the issue's
"phase 2" was for, and it changes where phase 1's links should point.

### 4.2 View > Keypad as a submenu carrying the dock (#71)

> **Done, 2026-09-12.** PR #80, merged as `b45c674`. Issue #71 is closed. Both renderers carry the same
> submenu: **Show the Keypad**, a separator, then **At the Bottom** and **On the Right** over
> `KeypadDockConverter`. The separator is the seam this section predicted — two settings rather than one
> enum, because keypad spec §2.1 rejected a `Hidden` member that would forget the dock whenever the keypad
> was hidden — so it is structural, not decoration. One-way binding plus Click throughout, the Crosshair
> items' shape, and no `Gesture` on any of them.

Small, and it finishes a feature that ships for the first time in this release. `KeypadDockConverter` is
written and tested, `SettingsViewModel.KeypadDock` already writes through and notifies, and the
View > Crosshair submenu is the template for both renderers. The one thing that is not copy-and-paste is
that Crosshair gets away with a single radio group because `None` is one of its modes, where the keypad's
on/off and dock are deliberately two settings; the keypad spec §2.1 rejected collapsing them.

### 4.3 Drop the engine path from About (#75)

> **Done, 2026-09-12.** PR #77, merged as `66fcf99`. Issue #75 is closed. All three sites are gone from
> `main`, and the `AboutWindowTests` comment that justified `SizeToContent.Height` by the path wrapping was
> rewritten rather than left dangling.

About shows the b3270 binary's full path on its own line. It is a residual from before the engine was built
and bundled on all six RIDs, when "which binary is this actually running?" was a live question worth
answering on screen. It is not live now: `engines.yml` builds the engine for every RID and
`verify-bundled-engine.sh` gates the archive that carries it. For the user this release is aimed at, the line
is a filesystem path they did not ask for and cannot act on.

Three sites, all in About — the `EnginePathText` `TextBlock`, the line that fills it, and one assertion.
`StatusFormatter.Engine` already reports provenance without a path, so the status bar is unaffected, and
`EngineInfo.Path` stays because the backend needs it to start the process.

The issue names one decision — whether to keep the line when `LIZTERM_B3270_PATH` is in force — and one
detail: `AboutWindowTests` justifies `SizeToContent.Height` by saying the engine path wraps, so that comment
has to be rewritten rather than left pointing at a line that no longer exists.

## 5. Public-debut housekeeping

> **Status, 2026-09-12: 5.1 to 5.4 are done and on `main`; 5.5 is done last, as planned — see its own note.**
> The file
> changes reached `main` in PR #81's merge, `703a754`: `8563dea` (the issue forms) and `7a23ffe` (the
> README, the screenshot and the disclaimer). 5.1 needed no commit, being repository settings rather than
> files.
>
> This paragraph was first written from the branch it was describing, so it named that branch and the
> pre-rebase SHAs `0f4d9c3` and `18099f2`, which no longer reach `main`. A status note drafted in the same
> commit it reports on cannot say where that commit ended up; the note is amended here rather than left to
> read as unmerged work. The rest of the section is the record as written.

The "debut" half of the theme. None of it is a feature, and all of it is what a stranger meets first.

### 5.1 The repository's own metadata

> **Done, 2026-09-12.** Description, homepage and eleven topics set on the repository. The homepage points
> at the user guide on GitHub, since #49's website does not exist and an empty field helps nobody.

Verified empty on 2026-09-12: no description, no homepage, no topics. Issue #49 makes the point itself —
this costs nothing and should happen regardless of what a website eventually becomes.

### 5.2 The README

> **Done, 2026-09-12.** The screenshot is `docs/images/session-ispf.png` — a live MVS/CE session at the
> ISPF primary option menu, keypad docked at the bottom, captured on macOS. It sits under the opening
> paragraphs, ahead of "The name". The logo is referenced in place as agreed. The Features list now names
> the keypad, the bell and Preferences, and records the menu-bar choice as a macOS one.

Four changes:

- **The logo at the top.** `src/LizTerm.App/Assets/Icons/lizterm-256.png` is already in the tree; reference
  it in place rather than copying the binary to a second location.
- **A screenshot.** There is not a single screenshot in the repository. This is the largest gap between what
  LizTerm is and what a visitor can tell it is. A screenshot needs a new home — `docs/images/` — because
  unlike the logo it is not an application asset.
- **The Features list is stale.** It does not mention the bell, the keypad, Preferences, or the menu-bar
  choice. Three of those four are this release's headline items.
- **The pre-1.0 disclaimer.** Section 5.3.

### 5.3 The pre-1.0 disclaimer

> **Done, 2026-09-12 — ahead of 4.1, by decision.** Section 9 sequences this after 4.1 so that both land in
> one README pass; 5.2 opened that pass first, so taking the disclaimer with it was cheaper than a second
> one. 4.1 now only appends a documentation link. The README section is "Where LizTerm is right now",
> between Features and Download; `release.yml` carries the short version as `## Status`. Neither ranks a
> platform against another, so section 7 is still what makes that silence honest.

What it needs to say, in substance:

- LizTerm is pre-1.0. Settings and profile file formats, defaults, and interfaces may change between
  releases.
- The 3270 protocol layer is **not new code**. Emulation is `b3270`, from the long-established x3270 suite,
  which every build bundles. That part of the stack is mature and is not where the risk is.
- What is new is the client around it — the window, profiles, settings, the file-transfer UI, packaging.
  Expect the terminal session itself to be solid and rough edges in the application surrounding it.
- Please report them, with a link to the issue tracker.

What it must **not** say: anything about where testing has been concentrated, or any statement that ranks
the three platforms against each other. Section 7's cross-platform pass is what makes a platform-neutral
disclaimer honest rather than an omission — running it before the tag is the point, not a formality.

**Home:** the README owns this text. The release notes heredoc in `release.yml` carries a short version, on
the same reasoning that already duplicates the "First run" section there: a person downloading an archive
from the Releases page may never see the README.

### 5.4 Issue templates

> **Done, 2026-09-12.** `.github/ISSUE_TEMPLATE/` now holds `bug_report.yml`, `feature_request.yml` and
> `config.yml`. Issue *forms* rather than markdown, so all five facts below are required fields rather than
> prompts; engine source is a dropdown over `EngineSource`'s own three values.

`.github/` contains workflows and nothing else. A release whose stated premise is that people will report
bugs should capture, in the template: the operating system and its version, the LizTerm version, the host
and its type, whether TLS is in use, and the engine source — `EngineSource` already distinguishes `Bundled`, `Override` and
`Unknown`, and About renders it. A feature-request template alongside it.

### 5.5 The version bump

> **Done, 2026-09-12** — twice. Both files said `0.5.0`, and now say `0.5.1`: the first bump merged as
> PR #83, and the second followed the cross-platform pass, for the reason the note at the top of this
> document records. Everything below about the mechanism held on both passes, the regeneration included, so
> the three-file shape is the durable finding rather than a one-off. These notes are written in the commits
> they report on, so they name no SHA; section 5's note records why one written that way cannot be trusted.
>
> **The bump is three files, not the two named below.**
> `src/LizTerm.App/Assets/Docs/user-guide.html` carries the version in its "Offline copy, shipped with
> LizTerm ..." banner and pins two `README.md` links to the release's own tag, so
> `UserGuideAssetTests.The_committed_html_is_what_the_converter_produces` fails until the guide is
> regenerated:
>
> ```bash
> LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"
> ```
>
> The offline-docs design spec (§6.3) predicted this and said this step "gains one regeneration"; the
> sentence never reached this section, and is here now. Those pinned links 404 until `v0.5.0` is tagged,
> which is correct — they name the release the copy shipped with, not a branch.

`Directory.Build.props` and `LizTerm.parcel` both still say `0.4.1`. They must both say `0.5.0` and they
must agree, or the release run fails at its first job. This is the last change before the tag.

## 6. Backlog hygiene

> **Done, 2026-09-12.** Milestone `v0.5.0` created (the repository's first) and assigned to #48, #71 and #75.
> #22 was already closed. #19 closed as completed, with a comment recording what Preferences actually carries.
> #55 amended **and retitled** to "Preferences and profiles: export and import as one bundle". #48 amended.
> Amendments were appended rather than written over the original bodies, following #22's own precedent and the
> repository's rule about not rewriting a record to match the code.
>
> **Two decisions taken while doing it, neither derivable from the list below:**
>
> - **A fixed cell size is withdrawn, not deferred.** The residue list below names it; Robert's call is that
>   `CellGeometry.Fit` scaling the screen to the available space is right for a 3270 client, because the
>   screen is a fixed grid and filling the window with it is what someone resizing the window is asking for.
>   Vista sizing the window to the font is a defensible alternative, not a better one. Recorded in #19's
>   closing comment so it is not re-raised as an omission.
> - **The residue splits three ways, not one.** #78 (color themes, and cursor shape and blink), #79 (wire log
>   defaults — which the list below missed, and which had shipped nowhere), and #18, which already existed for
>   the keymap editor and needed only a cross-reference.
>
> **House style, from here on: American English.** These issues say "color". The repository's existing prose
> still says "colour" and "licence"; no sweep was made, so the two conventions currently coexist.

No code. The backlog currently misrepresents itself in three places, and a public debut is when strangers
start reading it.

- **Close #22** — withdrawn by decision 2.1.
- **Close #19** ("A preferences window", tier-2) — delivered by PR #67. Re-file the genuine residue as its
  own issue: screen colours, cursor shape and blink, and a fixed cell size with the window sized to match.
  Leaving an open tier-2 issue by that title says the work has not started.
- **Re-scope #55** — its finding was "there are no preferences", which is no longer true, and its actual
  ask was that the store be designed so it could layer and export. `SettingsLayers` exists and the
  `AppSettings` doc comment records the flat-by-policy decision that makes layering work. What survives is
  export and import; the layering half belongs with #54.
- **Correct #48's body** — see section 4.1.
- **Create a `v0.5.0` milestone** and assign the above. There are no milestones in the repository today, so
  this scope is currently invisible outside this file.

## 7. The cross-platform pass — the gate on the tag

Development and verification have run against macOS. Nothing in section 3 has been exercised on Windows or
Linux by a person. That is a statement about what has been verified so far, and it belongs in an engineering
document; per section 5.3 it does not belong in anything a user reads. This pass is what stands between this release and a debut that embarrasses itself on two
of its three platforms.

**Mechanism:** a `workflow_dispatch` run of `release.yml` at version 0.5.0, per decision 2.2. It produces
all six RIDs and every installer, creates no release, and needs no tag.

**Charter — what has only ever run on macOS:**

- The on-screen keypad: rendering, both docks, and that its buttons do not take focus from the screen.
- The bell, visual and audible. Linux has a known gap — the system alert sound is unavailable there, and
  the Preferences window already says so. Confirm the note appears and the visual bell works.
- The Preferences window: layout, and that the **Menu bar group is correctly absent** rather than present
  and inert.
- The in-window menu carrying View > Keypad and its submenu. 4.2 landed, so this is no longer conditional:
  check that **Show the Keypad**, **At the Bottom** and **On the Right** all work from the in-window menu,
  which off macOS is the only menu there is.
- **Help > User Guide** — 4.1, the newest item in the release and the one no platform but macOS has run.
  The guide is an embedded resource written out as `lizterm-user-guide-0.5.0.html` and handed to the
  platform's default browser, which is three different mechanisms behind one `Launcher` call. Check that it
  opens, that the banner names 0.5.0, that the tables and internal anchors work, and that a platform which
  cannot open it shows the error naming the guide's GitHub URL rather than failing silently.
- **Help's other links** — Project on GitHub, Report an Issue..., Releases — reach the browser, and
  Show Wire Logs... still opens the folder from the same menu.
- **About** — 4.3 removed the engine path, so check the window still sizes to its content without the line
  `SizeToContent.Height` used to be justified by, and that the status bar still reports engine provenance.
- `settings.json` written to the right per-OS path, and surviving a restart.
- The `.deb`, `.rpm` and `.exe` installers themselves, which are packaged on every release and installed by
  nobody so far.
- A real session against a live host on each platform: connect, log on, keyboard, and an IND$FILE transfer
  in both directions.

**Two documented gaps to confirm rather than discover:** Windows SmartScreen warns about the unsigned build
(#65), and the Windows engine cannot pin a certificate, so a profile carrying a pin refuses to connect
there. Both are already in the user guide's Known limitations; the pass is checking that the behaviour
matches the text.

**Triage rule.** A finding blocks the tag if it breaks connecting, keyboard input, file transfer, or
installation on any platform. Everything else is recorded as an issue and becomes 0.5.1. This rule exists so
that the pass cannot quietly turn into an open-ended polish milestone.

## 8. Explicitly out of scope

Named so that "why isn't this in 0.5.0" has an answer: #12 (SNI — a documented limitation, and the fix is
either upstream or a patch-carrying engine build), #17, #18, #20, #21, #23, #32, #43, #46, #49 beyond the
free metadata in 5.1, #51, #52, #53, #54, #58, #65.

Several of these are worth reading as a group once 0.5.0 is out. #51 through #55 are one theme — profiles
and preferences as portable, distributable, layerable things — and #52's finding that RFC 6270 leaves a
`tn3270:` URI with nowhere to put a trust downgrade is the kind of constraint that should shape all of them
together rather than one at a time.

## 9. Sequence

Ticked as of 2026-09-12.

1. ~~**Merge PR #74.**~~ **Done** — merged as `3966ee5`.
2. **Section 6 (backlog hygiene) and sections 5.1, 5.2 and 5.4** — independent of each other and of the
   code; can land in any order, and none of them blocks anything.
   **Done** — 5.1, 5.2 and 5.4, and section 6. The `v0.5.0` milestone now exists and carries #48, #71 and #75.
3. ~~**Design spec for 4.1**, then its plan, then the work. This is the long pole.~~ **Done** — see 4.1.
4. ~~**4.2 and 4.3**, at any point after step 1.~~ **Both done** — 4.3 as PR #77, 4.2 as PR #80.
5. ~~**5.3**, once 4.1's shape is settled~~ — **done early**, with 5.2, for the reason recorded in 5.3.
   4.1 now only appends a documentation link to a README section that already exists.
6. ~~**5.5**, the version bump.~~ **Done** — three files, per its own note.
7. **Section 7**, the rehearsal and the cross-platform pass.
8. **Tag `v0.5.1`** — see the note at the top of this document for why it is not `v0.5.0`.

**What is left:** the tag. Sections 3 to 6 are on `main`, and section 7's pass ran on 2026-09-12 against the
0.5.0 rehearsal: every item passed on all three platforms except the `.rpm`, which had no host to install it
on, and one finding — the Windows executable carrying no Win32 icon resource, so the shell drew a generic
icon while the app's own windows were correct. Fixed as PR #84, which is what the second rehearsal, at 0.5.1,
is for. By §7's triage rule the icon did not block the tag; it was taken anyway.

## 10. Risks

- **4.1 is unscoped.** Its spec could reveal that generating HTML in three publish jobs is more than it
  looks, or that a macOS `.app` bundle and a `.deb` want different answers. If it does, shipping the links
  alone and deferring the bundle is a legitimate reduction that does not damage the theme — the user guide
  is still reachable on GitHub.
- **The cross-platform pass will find things.** That is its purpose, and the triage rule in section 7 is
  what stops it from moving the tag indefinitely.
- **Rehearsal artifacts expire after seven days**, so the pass has to happen while they are live or be
  re-run.
- **Parcel with a semver pre-release version is still unproven.** Decision 2.2 avoids the question rather
  than answering it. It returns the first time a genuine release candidate is wanted.
