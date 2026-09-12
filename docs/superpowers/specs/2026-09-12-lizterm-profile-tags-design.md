# Profile tags and notes — design

Date: 2026-09-12

Closes the design half of issue #43. The issue was captured as "a rough idea for tracking rather than
specified", posed two open questions, and recommended answers to both. Brainstorming with Robert on
2026-09-12 answered them differently and enlarged the shape, so the decisions in section 2 supersede the
issue's recommendations where they disagree. The issue text stays as written; this document is the design.

## 1. What this adds

A profile can carry **zero or more tags** and **one short note**. The session list renders tags as uppercase
colour chips and the note as a third line, and gains a filter row — a text box and a scope drop-down — above
the list.

A tag has a **definition** that exists independently of any profile: a name and a colour, held in a registry.
Profiles reference tags by name only. `FAVORITE` is a reserved tag with an immutable definition, rendered as
a gold star rather than a text chip.

**Definition of done:** a user with a handful of profiles can mark one `FAVORITE`, tag two others `PROD` and
`MVS`, see those as chips in the list, narrow the list to one tag from the drop-down, type a few characters to
narrow it further, and read a note reminding them which box has no live data on it — without renaming a single
profile into a `DEV-`/`PROD-` prefix group.

## 2. Decisions taken while designing

Each of these closes something, and none is derivable from the code.

**2.1 Many tags, not one.** The issue proposed a single `string? Tag` plus a colour. Robert's answer: an array
of zero or more tags per profile, a small chip for each, typically two or three (`FAVORITE`, `PROD`, `MVS`).
This is the decision that drove every other one below.

**2.2 Colour belongs to the tag, not to the profile.** The issue considered only per-profile colour. Robert's
phrasing — a tag has an *"immutable definition"* separate from *"what items are tagged with it"* — describes a
registry, and a registry is what this spec builds. `PROD` is one colour everywhere, by construction, because
there is one definition of `PROD`. Per-profile colour was rejected: the same name could be red on one profile
and green on another, which destroys the scan that colour exists for.

The cost is that a profile file is no longer fully self-describing — issues #51, #52, #53 and #54 all move
profiles between machines, and a moved profile's tag *names* travel with it while its *colours* do not.
Section 4.3 makes this a graceful degradation rather than data loss: an unrecognised name registers itself on
first sight and takes an auto-assigned colour locally. Issue #55 already scopes bundling preferences and
profiles together for export, which is where the registry would travel if that is ever wanted.

**2.3 Colours are auto-assigned, and overridable only in the registry.** A new tag takes a palette colour
automatically. Changing it is a registry edit, which means Manage Tags (2.6) — there is deliberately **no
colour picker in the profile editor**. This keeps phase one free of colour UI while still delivering
colour-coded chips.

**2.4 `FAVORITE` is a reserved tag, not a separate field.** Singular, that spelling. Its definition is
immutable: always present, always gold, never renameable, never recolourable. Only what is tagged with it
changes. It renders as a gold star in a fixed left gutter, never as a text chip. A `bool Favorite` field was
considered and rejected — as a reserved tag it needs no new field, no new filter axis, and no new concept.

**2.5 A filter row, not a tag-aware sort.** The design recommendation had been to sort by `(tag, name)` in
`ProfileStore.LoadAll()`. Robert chose a filter box plus a scope drop-down instead, narrowing by scope first
and then by typed text. Given a scope selector, clustering only helps inside "All sessions", which is not
worth a Core change — so **`ProfileStore.LoadAll()` keeps sorting by name alone and is otherwise untouched.**
Group headers in the `ListBox` and a unified filter/Quick-Connect box were both considered and rejected; see
2.8 and section 5.2.

**2.6 Manage Tags is a separate issue.** Renaming a tag across every profile file, collision rules, deletion
and recolouring get their own issue and their own attention. The drop-down's "Manage Tags" entry, and the
`---` separator that set it apart in Robert's sketch, are **not** in this phase — the entry's absence is also
why the separator loses most of its job (section 5.3).

**2.7 Chips render uppercase regardless of stored casing.** One line of projection, and it makes `prod` versus
`PROD` typo divergence invisible. The registry already treats the two as one tag.

**2.8 Row layout: chips on the name line, right-aligned, filling leftward.** Chosen from three variants shown
as a mockup at the window's real 520 px. Two consequences were settled in the same pass:

- **The star sits in a fixed 15 px left gutter**, outside the chip group. Right-aligned among the chips it
  would slide horizontally with the chip count, and a column of stars that does not line up is not scannable,
  which is most of what a favourite marker is for.
- **Chips never shrink; the name ellipsises.** A truncated `PRO…` carries nothing; a truncated long name is
  still recognisable.

**2.9 The editor takes tags as one comma-separated box.** Decided without asking, and flagged here as the one
thing in this spec that did not come from a question. With colours out of the editor (2.3), a tag row needs no
per-tag control — a single text box and a checkbox is the whole affordance. If a chip-style token editor is
wanted instead, this is the decision to reject.

## 3. The data model

### 3.1 `SessionProfile` gains two parameters

```csharp
TagSet Tags = default,
string? Note = null
```

Both at the end of the positional list, both with defaults, for the reason the record's own doc comment gives:
the source generator honours constructor defaults for a field missing from a file, so every existing profile
loads with no tags and no note and nothing is migrated. An older LizTerm reading a newer file ignores both
keys, because `ProfileJsonContext` sets no `UnmappedMemberHandling.Disallow`.

### 3.2 Why `TagSet` and not `IReadOnlyList<string>`

**A collection member silently breaks record equality.** `SessionProfile` is a record, and the synthesised
`Equals` compares a collection member **by reference**. Two existing assertions depend on structural equality
and would fail the moment a profile carries a tag, because the round trip builds a new list:

- `ProfileStoreTests:37` — `Assert.Equal(profile, loaded)`
- `ProfileStoreTests:129` — `Assert.Equal(opened with { PinnedCertificate = pin }, store.Load("gw"))`

Hand-writing `Equals(SessionProfile?)` was rejected. A record may supply its own, but then a field added later
is silently absent from equality — and this record's entire doc comment is about fields being added later.
The bug would be invisible and permanent.

`ImmutableArray<string>` alone does not fix it either: `ImmutableArray<T>.Equals` is reference equality on the
underlying array, so a `record struct` wrapper inherits the same defect.

So: **`TagSet`, a readonly struct with explicit value equality**, keeps the record's synthesised equality
correct forever and needs no maintenance when the record grows.

```csharp
public readonly struct TagSet : IEquatable<TagSet>
```

- Wraps an `ImmutableArray<string>`; **every member guards `IsDefaultOrEmpty`**, because `default(TagSet)` holds
  a default `ImmutableArray` rather than an empty one, and that is the value every existing profile file
  produces.
- `TagSet.Empty`, and `default` is equal to it.
- `TagSet.From(IEnumerable<string>)` normalises: trim each name, strip a leading `#`, drop blanks, de-duplicate
  case-insensitively keeping the **first** casing seen, preserve order, drop any name longer than
  `MaxTagNameLength`, and keep only the first `MaxTags`.
- `Count`, `Names` (`IReadOnlyList<string>`), `Contains(string)` — case-insensitive.
- `With(string)`, `Without(string)` — return a new set, for the editor's `FAVORITE` checkbox.
- `Equals` is `SequenceEqual` under `OrdinalIgnoreCase`; `GetHashCode` combines the same lowered names, so the
  two agree.
- `ToString()` joins with `", "`, which is what the editor's text box shows.

`MaxTags = 8`, `MaxTagNameLength = 16`. Both are caps against a pathological hand-edited file, not limits a
user with two or three tags will meet.

**The invariant, and who enforces which half.** A `TagSet` never holds more than `MaxTags` names, and never a
name longer than `MaxTagNameLength` — `From` guarantees it by dropping what does not fit, because a file
arriving with ten tags is malformed and repairing it is the same choice `ProfileStore` already makes when it
drops a pin with no PEM. An over-long name is *dropped* rather than truncated: truncating would silently
invent a different tag.

That makes section 7's two validation messages **the editor's job, and they must run before `TagSet.From`
does** — counting and measuring the names the user typed, not the set built from them. Validating afterwards
could never fire, because `From` would already have discarded the evidence.

### 3.3 Serialisation

`TagSet` needs a converter, written as a JSON string array:

```json
"tags": ["FAVORITE", "PROD", "MVS"],
"note": "IND$FILE test box, no live data"
```

Declared the way `AppSettings` declares its enum converters, which is the established idiom:
`[property: JsonConverter(typeof(TagSetJsonConverter))]`. Reading applies `TagSet.From`, so a hand-edited file
with duplicates, blanks, `#` prefixes or too many entries is repaired on load rather than rejected.

### 3.4 `TagColor`

```csharp
public enum TagColor { Gold, Red, Amber, Green, Blue, Purple, Teal, Grey }
```

**Named `TagColor`, not `ProfileColor`, and it lives in `LizTerm.Core.Profiles`.** The issue proposed
`ProfileColor` because a colour then belonged to a profile; under 2.2 it belongs to a tag definition, and
`SessionProfile` never mentions it. Carrying the old name into the new model would say the profile is coloured,
which is exactly the design that was rejected — and it would sit in `Core.Session` next to a record that does
not use it. It goes beside `TagDefinition`, which is its only consumer.

BCL-only either way, so the dependency rule holds — Core cannot name an `Avalonia.Media.Color`. `Gold` is
reserved for `FAVORITE` and excluded from auto-assignment, leaving seven.

**`HostColor` is deliberately not reused.** It exists in `Core.Screen` with colour names already, but it is
the 17-member 3270 screen model in b3270's naming order, and `Rendering/Palette.cs` tunes it for a black
terminal ground. Wrong palette, wrong semantics.

The App maps the enum to brushes in `src/LizTerm.App/Rendering/TagPalette.cs`, mirroring `Palette.cs`. The
palette is chosen for **white chip text at 4.5:1 or better**, so the chips survive issue #78 adding a light
theme without a second palette.

## 4. The tag registry

### 4.1 Shape

`TagDefinition(string Name, TagColor Color)` — a record.

`TagRegistry` — the in-memory model. Case-insensitive lookup, `ColorOf(name)`, and
`Register(IEnumerable<string> names)` returning both a registry and whether anything changed, so a caller can
skip a pointless write.

`All` returns the definitions **sorted by name, `OrdinalIgnoreCase`** — not in file or insertion order. The
scope drop-down (5.3) renders `All` directly, and a list whose order depends on which tag happened to be
created first is not something a user can predict.

`TagRegistryStore(string file)` — load and save, with `ProfileStore`'s exact discipline: write a sibling temp
file and rename over the target so a reader never sees a partial file, and swallow `JsonException`,
`IOException` and `UnauthorizedAccessException` on read.

`AppPaths.TagsFile() => Path.Combine(ConfigRoot(), "tags.json")` — beside `settings.json`, `profiles/` and
`logs/`. `AppPaths`' own doc comment names what lives under the root and needs updating.

```json
{
  "tags": [
    { "name": "PROD", "color": "Red" },
    { "name": "MVS",  "color": "Blue" }
  ]
}
```

An array of records rather than a flat name→colour map, so a tag can gain a field later without reshaping the
file. Written by name, not by number, for the reason `AppSettings` gives: the file is meant to be read by hand,
and reordering the enum must never change a saved meaning.

### 4.2 `FAVORITE` is synthesised, never stored

The reserved definition is **never written to the file and always supplied in memory**. A hand-edit therefore
cannot recolour or rename it, which is what "immutable definition" means. An entry for it found in a file is
ignored.

### 4.3 Registration, repair, and where the side effect lives

**Auto-assignment picks the least-used palette colour**, breaking ties by enum order. Deterministic, and it
spreads colours better than "next in order" once tags have been added and removed.

**An unknown colour repairs per entry, not per file.** A hand-edited `"color": "Chartreuse"` re-assigns that one
tag rather than discarding every definition — the colour is read as a string and mapped with `Enum.TryParse`.
Losing a whole registry to one typo would be harsh where losing one tag's colour is invisible.

**`ProfileStore.LoadAll()` does not touch the registry.** A load must not write. Reconciliation lives in
`ProfilePickerViewModel.Reload()`, which already orchestrates a load: it gathers the tag names off the loaded
profiles, calls `Register`, and saves only if something changed. That keeps Core's store pure and gives 2.2's
degradation for free — a profile arriving from another machine registers its tags the first time it is seen.

## 5. The session list

### 5.1 The row

```
┌──────────────────────────────────────────────┐
│ ★  mvsce                        PROD   MVS   │
│    10.42.37.209:3270                         │
│    IND$FILE test box, no live data           │
└──────────────────────────────────────────────┘
   └ 15 px gutter          chips right-aligned ┘
```

The list is **356 px wide**, not 520: the window's 16 px margins and the 120 px button column with its 12 px
gap take the rest. That is the width the chips must fit, and it is why the star is a glyph rather than a chip —
three text chips plus a `FAVORITE` chip would not fit.

- **Gutter**: 15 px fixed, on all three lines. Holds the gold star when `Tags.Contains("FAVORITE")`, nothing
  otherwise.
- **Chips**: an `ItemsControl` over the non-reserved tags, right-aligned, filling leftward. Text uppercased by
  the projection (2.7); Avalonia has no `text-transform`.
- **Overflow**: up to three chips render; beyond that, the first three plus a `+n` chip whose `ToolTip` lists
  the rest. (The approved mockup showed two chips plus `+3` for a five-tag profile; three plus `+2` is the same
  mechanism and one chip more informative.)
- **Note**: third line, `TextTrimming="CharacterEllipsis"`, `IsVisible` false when null or blank so an un-noted
  profile stays two lines, with the full text on a `ToolTip`.
- **Quick Connect's ad hoc profiles** carry `default` tags and a null note, so gutter, chips and third line all
  collapse and the template renders their absence cleanly — what issue #43 asks of it for #29.

### 5.2 Two text boxes, and why that is acceptable

The header already holds Quick Connect, so the filter box is a second text box a few pixels away, and Enter
means something different in each. Merging them into one box — filter as you type, connect on Enter when the
text parses as a host — was considered and rejected: it changes a shipped, documented feature for elegance
rather than need, and the list flickers empty while a hostname is typed.

They are distinguished by placeholder and by position, the filter row sitting directly on the list. **The
filter box needs no `KeyDown` handler at all**, unlike Quick Connect: if filtering keeps a valid selection
(5.4), the existing `IsDefault` Connect button already does the right thing — narrow to one profile, press
Enter, connected.

### 5.3 The scope drop-down

A `ComboBox`, items in this order:

1. `All sessions`
2. `FAVORITE`
3. `#PROD`, `#MVS`, … — one per registered non-reserved tag, alphabetically

**No separator control.** Robert's sketch drew `---`, whose main job was setting "Manage Tags" apart from the
filters; with that entry out of this phase (2.6) the separator loses most of its purpose, and the `#` prefix
already distinguishes tag scopes from the two fixed entries. Flagged as a simplification from the sketch.

The drop-down is always visible and always holds at least `All sessions` and `FAVORITE`. Hiding it until tags
exist was considered and rejected as invisible machinery; predictable beats clever.

Items are a small `ScopeOption(string Label, string? TagName)` record rather than bare strings — `TagName` is
null for `All sessions` and carries the name for the other two kinds, so 5.4's predicate reads the name it was
given instead of parsing a `#` back off a display string.

### 5.4 Filtering

**Scope narrows first, then the text box filters what is left** — Robert's ordering, implemented as two
composable predicates.

- Scope: `All sessions` passes everything; `FAVORITE` and `#tag` pass a profile whose `TagSet` contains that
  name, case-insensitively.
- Text: case-insensitive substring against the **profile name or any of its tag names**. Host is not matched —
  Quick Connect is where a host is typed. A possible later addition, not this phase.

`Profiles` stays the loaded source of truth; a `VisibleProfiles` collection is recomputed on reload and on any
change to either control. **After a refilter, a selection that is no longer visible moves to the first visible
profile**, which is what makes 5.2's "no Enter handler" correct.

### 5.5 The window grows

`Height` 400 → **480**, `MinHeight` 300 → **360**. At 400, the Quick Connect row plus the new filter row plus
three-line rows left about four profiles visible.

## 6. The profile editor

The grid declares ten `Auto` rows and uses all ten, so **two row definitions are added** — the issue's note
that it "declares nine and uses eight" is stale.

**Row 10 — Tags**, one `StackPanel` holding three controls, the shape the Security and Connection rows already
use:

```
Tags      [x] Mark as FAVORITE
          [ PROD, MVS                                    ]
          Separate with commas; shown as uppercase chips.
```

The checkbox owns the reserved tag. **Typing `FAVORITE` into the text box turns the checkbox on and drops it
from the box** rather than raising an error — forgiving, and self-explanatory because the checkbox visibly
moves. The label names the tag instead of spelling "favourite" in prose, which also sidesteps a British/American
split in a repository whose prose is British and whose identifiers are American.

**Row 11 — Note**: a `TextBox` with `AcceptsReturn="False"` and `MaxLength="120"`, placeholder
`optional — shown under the host in the list`. Single line by construction, so a pasted paragraph cannot
reshape the list.

No colour control anywhere in this window (2.3).

`TryBuild` gains: a `TagSet` from the checkbox and the parsed box, and a trimmed `Note` that is null when
blank — the same treatment `LuName` already gets.

## 7. Validation

In `TryBuild`, in the existing style — one message, first failure wins:

| Rule | Message |
|---|---|
| A tag name longer than 16 characters | `Tag names can be at most 16 characters.` |
| More than 8 tags, `FAVORITE` included | `A profile can carry at most 8 tags.` |

Blanks, duplicates, `#` prefixes and surrounding whitespace are **normalised silently** by `TagSet.From`, not
refused: there is nothing for the user to fix. A note over 120 characters cannot be typed, because `MaxLength`
stops it.

## 8. Testing

**Core**

- `TagSetTests` — normalisation (trim, `#` strip, blanks, case-insensitive de-duplication keeping first casing,
  order, cap); `default` behaves as empty across every member; `Equals` ignores case and respects order;
  `GetHashCode` agrees with `Equals`; `ToString` round-trips through `From`.
- `ProfileStoreTests` — a round trip carrying tags and a note; **`Assert.Equal(profile, loaded)` with tags
  present**, which is the regression `TagSet` exists to prevent and must be asserted rather than assumed; a
  pre-existing file with neither key loading to empty and null; a hand-edited file with duplicate and `#`-prefixed
  tags repairing on load.
- `TagRegistryTests` — least-used auto-assignment and its tie-break; `FAVORITE` always present, always gold,
  never written, ignored when present in a file; an unknown colour repairing that entry alone; case-insensitive
  lookup; `Register` reporting no change when every name is known.
- `TagRegistryStoreTests` — temp-file-and-rename, an unreadable file loading as empty, a temp file left by a
  crash not being read as definitions. Temp directory per test, as `ProfileStoreTests` and `SettingsStoreTests`
  do.

**App**

- `TagPaletteTests` — **every `TagColor` member maps to a brush**, driven by `Enum.GetValues`, so adding a
  colour without a brush fails the suite rather than rendering nothing.
- `ProfileEditorViewModelTests` — comma parsing; `#` stripped; `FAVORITE` typed into the box turning the
  checkbox on; both validation messages; a blank note becoming null.
- `ProfilePickerViewModelTests` — scope-then-text ordering; text matching name and tag but not host; selection
  moving to the first visible profile when the current one is filtered out; the registry saved only when
  `Register` reports a change.
- `ProfilePickerWindowTests` — the star present only with `FAVORITE`; chips uppercase from lowercase input; the
  `+n` overflow chip and its tooltip; the note line collapsed when absent and present when not; an ad hoc
  profile rendering no gutter glyph, no chips and no third line; **the row fitting at the window's `MinWidth`**,
  mirroring the existing `The_quick_connect_row_fits_at_the_windows_minimum_width`.

**Docs**

- `docs/user-guide.md` — three rows in the profile-settings table, and a paragraph on the filter row, then
  `LIZTERM_UPDATE_DOCS=1 dotnet test tests/LizTerm.Core.Tests --filter "FullyQualifiedName~UserGuideAssetTests"`
  to regenerate the bundled HTML. `UserGuideAssetTests` fails until that runs.

## 9. Out of scope

Each of these is a decision to defer, not an oversight.

- **Manage Tags** — rename, recolour and delete a tag across every profile file, plus the drop-down entry and
  its separator. Its own issue (2.6).
- **Toggling `FAVORITE` from the list** — a click on the gutter star, or a context menu, rather than
  Edit → checkbox → Save. Genuinely nicer for something flipped often, but it is a new interaction competing
  with row selection and double-tap-to-connect. Worth an issue.
- **Matching the host in the filter text** (5.4).
- **A tag-aware sort in `ProfileStore`** — explicitly dropped, not deferred (2.5).
- **The note in the session window** — issue #43 raises it and calls it a separate thought; it stays one.
- **Per-profile colour**, and a unified filter/Quick-Connect box — considered and rejected (2.2, 5.2).

## 10. Where it lands

**New**

| File | What |
|---|---|
| `src/LizTerm.Core/Session/TagSet.cs` | The value-equality collection (3.2) |
| `src/LizTerm.Core/Profiles/TagColor.cs` | The palette enum (3.4) |
| `src/LizTerm.Core/Profiles/TagDefinition.cs` | Name and colour (4.1) |
| `src/LizTerm.Core/Profiles/TagRegistry.cs` | Lookup, registration, auto-assignment (4.1, 4.3) |
| `src/LizTerm.Core/Profiles/TagRegistryStore.cs` | Load and atomic save (4.1) |
| `src/LizTerm.Core/Session/TagSetJsonConverter.cs` | String array ↔ `TagSet` (3.3), beside the type it converts |
| `src/LizTerm.Core/Profiles/TagRegistryFile.cs` | The file's root record (4.1) |
| `src/LizTerm.Core/Profiles/TagRegistryJsonContext.cs` | Its source-generated context (4.1) |
| `src/LizTerm.App/Rendering/TagPalette.cs` | `TagColor` → brush (3.4) |

**Changed**

| File | What |
|---|---|
| `src/LizTerm.Core/Session/SessionProfile.cs` | Two parameters and their doc comments (3.1) |
| `src/LizTerm.Core/Profiles/AppPaths.cs` | `TagsFile()`, and the class doc comment it makes stale |
| `src/LizTerm.App/ViewModels/ProfilePickerViewModel.cs` | Filter, scope, `VisibleProfiles`, reconciliation (4.3, 5.4) |
| `src/LizTerm.App/ViewModels/ProfileEditorViewModel.cs` | Tag and note fields, parsing, validation (6, 7) |
| `src/LizTerm.App/Views/ProfilePickerWindow.axaml` | Filter row, item template, window height (5) |
| `src/LizTerm.App/Views/ProfileEditorWindow.axaml` | Two rows, two row definitions (6) |
| `docs/user-guide.md` + bundled HTML | Settings table and the filter row (8) |

`ProfileStore.cs` is **not** in either list, by decision (2.5).
