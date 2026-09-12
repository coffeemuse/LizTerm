# LizTerm: drop the engine path from About

Date: 2026-09-12. Parent spec: `2026-09-03-lizterm-v1-design.md`. Slice of
`2026-09-12-lizterm-v050-release-scope.md` §4.3. Issue: #75.
Status: approved in discussion on 2026-09-12; Robert delegated approval of this text and of the plan beside it.

## 1. Purpose

About shows the b3270 binary's full path on its own line, in grey, under the engine name:

```
b3270 4.5.6, bundled
/Applications/LizTerm.app/Contents/MacOS/runtimes/osx-arm64/native/b3270
```

The line is a residual. It was written when the engine was not yet built and bundled on all six RIDs and
"which binary is this actually running?" was a live question worth answering on screen. It is not live now:
`engines.yml` builds the engine for every RID, and `verify-bundled-engine.sh` gates the archive that carries
it. For the user v0.5.0 is aimed at — a hobbyist opening About to see a version number — it is a filesystem
path they did not ask for and cannot act on.

This spec removes it. Nothing else about About changes.

## 2. The decision: remove it unconditionally

`LIZTERM_B3270_PATH` still exists and is still documented in `docs/development.md`. With the path line gone,
a developer who has set it sees `b3270 4.5.6, from LIZTERM_B3270_PATH` and not which binary that resolved to.

The alternative was to keep the line only when `Source == EngineSource.Override`, which would have hidden it
in the bundled case every user sees while preserving the diagnostic where it is still arguably useful. It was
rejected on 2026-09-12: someone who exported the variable can read it back from their own shell, so the app
repeating it is not a diagnostic they lack, and the conditional costs an `IsVisible` binding and a second test
case to hold a line nobody has asked for. Removal is unconditional.

The `Unknown` case needs no thought either way: `EngineInfo.Path` is `""` there already, so the line renders
empty today.

## 3. What stays

- **`EngineInfo.Path`.** `SessionFactory` and `B3270Session` need it to start the process. This spec changes
  what About displays, not what the record carries.
- **`StatusFormatter.Engine`.** It already reports provenance without a path — `b3270 4.5.6, bundled`, or
  `b3270 4.5.6, from LIZTERM_B3270_PATH`, or `b3270, not found`. The status bar and the rest of About are
  untouched, and the engine line in About keeps saying everything it says today.
- **`SizeToContent.Height` on the window.** The engine line and the copyright both wrap, and `LicensesText` is
  a fixed 220-high `TextBox`, so the window must still grow to its content rather than clip at a fixed height.
  What changes is the justification, not the setting: see §5.
- **The design-time constructor's `/path/to/b3270`.** `EngineInfo` is a positional record and still takes a
  path; the preview simply stops showing it.

## 4. Layout

Three `TextBlock`s stay in the DockPanel's top stack, in order: `VersionText`, `EngineText`, `CopyrightText`.
The removed line carried no margin of its own — it sat tight under the engine line — and `CopyrightText` keeps
its `0,12,0,0`, so the gap below the engine line is exactly what it is today and no other margin moves.

## 5. Testing

`AboutWindowTests.Shows_version_engine_and_licenses` asserts the path today. Replacing that assertion with
nothing would leave the removal unguarded, so it becomes a positive statement instead: show About with an
override engine at `/opt/homebrew/bin/b3270`, assert `EngineText` still reads
`b3270 4.5.6 (fake), from LIZTERM_B3270_PATH`, and assert that no `TextBlock` in the window contains that
path. The override engine is deliberate — it is the case where a path is most defensible, so it is the case
worth pinning.

The same test justifies `SizeToContent.Height` with a comment reading "Spec 8: the engine path wraps, so the
window grows with it instead of clipping at a fixed height." Removing the path removes that justification.
The comment is rewritten to rest on what actually wraps now — the engine line and the copyright, above a fixed
220-high licences box — and drops the reference to Spec 8, whose wording no longer describes the window. Per
the root `CLAUDE.md`, the spec itself is a record and is not rewritten to match.

No other test mentions `EnginePathText`; a repository-wide grep finds the three sites in §6 and nothing else.

## 6. Sites

| File | Change |
|---|---|
| `src/LizTerm.App/Views/AboutWindow.axaml:21` | Delete the `EnginePathText` `TextBlock`. |
| `src/LizTerm.App/Views/AboutWindow.axaml.cs:23` | Delete `EnginePathText.Text = engine.Path;`. |
| `tests/LizTerm.App.Tests/Views/AboutWindowTests.cs:25` | Replace the assertion (§5) and rewrite the `SizeToContent` comment. |

## 7. Documentation

None. No user-facing or developer document describes the About path line — `docs/user-guide.md` mentions
About only as a menu item, and `docs/development.md` documents `LIZTERM_B3270_PATH` without reference to where
its value is displayed. Each fact has one home, and this one had no home outside the code.
