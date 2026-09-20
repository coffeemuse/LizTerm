# LizTerm: Keys menu shortcuts as real shortcuts (#23, second pass)

Design, 2026-09-19. Replaces the header-text hints that the editable keymap design (§6.2) gave the Keys menu
earlier the same day, before they shipped. Assumes the native menus design (§2.1, the measured trap) and the
editable keymap design (`KeymapHints`, the keymap in force per window, the Keyboard tab).

## 1. Purpose

The first pass put each Keys item's keystrokes in its header text, `PA2  ⌥+2 or ⌃+↖`, because a gesture on a
native menu item is a real AppKit key equivalent and the screen never sees the key. Robert reviewed the result
in the running app and rejected it: it reads as prose, nothing lines up, every alternative is listed, the notation
is not the platform's, and on the native macOS menu the text can never be grey or right-aligned, which is the
whole point of a shortcut column. The Edit menu beside it shows what the Keys menu should look like.

This spec records the spike that found a way to have real, AppKit-drawn shortcuts on the Keys menu without the
trap, and the design built on it.

## 2. What the spike measured

Run with Robert at the keyboard on 2026-09-19, Apple silicon, Avalonia 12.1.2, a connected session, the Keys menu
closed and the screen focused. A throwaway build logged who saw each keystroke (`SCREEN key X` from
`TerminalScreen.KeyRequested`, `MENU X clicked` from the native item's `Click`).

**Control: real gestures on the Keys items, nothing else.**

| Pressed | Saw it |
|---|---|
| ⌥1 | screen (PA1) |
| ⇧F1 | menu item |
| ⌃R | menu item |
| ⎋ | screen (Attn) |
| ⌃⎋ | menu item |

The trap is real for ⇧F1, ⌃R and ⌃⎋. AppKit never matches an Option-plus-digit or an unmodified Escape key
equivalent, though it renders both. The menu title does not visibly flash on a claimed key; only a log tells.

**Dead ends.**

- `menuHasKeyEquivalent:forEvent:target:action:` on the Keys submenu's delegate. Avalonia's `AvnMenuDelegate`
  does not implement it (the selector in `libAvaloniaNative.dylib` is protocol metadata), so it can be added by
  subclassing the delegate at run time. `NSMenu` caches which delegate methods exist when the delegate is set, so
  the delegate has to be re-set after the class swap. After that AppKit consulted it, but only for ⌘ chords: ⌘C
  and ⌘V were asked about, ⇧F1 and ⌃R fired the items without a question.
- `performKeyEquivalent:` on the Keys submenu itself: never called.

**What works: `performKeyEquivalent:` on the main menu.** `NSApplication.sendEvent:` calls it on the main menu
for every key-down before the key window's responder chain. With the main `AvnMenu` answering NO for any event
without ⌘ and calling the original for the rest: ⇧F1 was offered, declined, and the screen saw PF13; ⌃R likewise
became Reset; ⌃⎋ was declined and fell through (see #157); ⌘C, ⌘V and ⌘Q went to the original and were handled.
Robert confirmed the shortcuts rendered grey and right-aligned on the native menu.

A key pressed while the Keys menu is open fires the item, through the menu's own tracking loop. Every macOS menu
does that and it is not the trap.

**A bug found on the way.** ⌃⎋ reaches the window but never reaches `TerminalScreen` as Clear, on release
0.6.1 as well. Pause does not exist on Apple keyboards, so Clear has no working keystroke on macOS today. Filed
as #157, fixed separately; the Keys menu shows ⌃⎋ beside Clear regardless, as the tooltip and the guide already
do, and all three become true together.

## 3. Design

### 3.1 The override

`LizTerm.App/Menus/MacMenuKeyEquivalents` (macOS only, `DllImport` on `libobjc` in the style of
`SystemBellRinger`) replaces `performKeyEquivalent:` on the `AvnMenu` class once, at startup, under the native
menu strategy only, and keeps the original implementation. The replacement reads the event's modifier flags:
without ⌘ it returns NO, so the keystroke goes on to the key window and the screen; with ⌘ it calls the
original. Replacing the class covers every main menu Avalonia builds, one per window, with no per-window install.

`Installed` is true only when the class and the method were found and replaced. If it is false, no Keys item ever
gets a gesture: a future Avalonia that renames the class loses the shortcut column, never the keys.

The rule "no ⌘, not a menu key" holds because no native item outside the Keys menu declares a ⌘-less gesture
(§4 pins it). Function-key or Control gestures on any other item would be declined silently, which is why
that invariant is a test and not a comment.

### 3.2 One chord per item

`KeymapHints.MenuChord(chords, platform)` picks the chord an item shows, from the chords the keymap in force
maps to the key: taps are dropped, keys the platform's keyboard lacks are dropped (Pause and Insert on macOS),
and the first of the remainder in `KeymapHints.Ordered` wins. `Ordered` changes its modifier order to unmodified,
Shift, Alt, Control, with function keys ahead of other keys within a group and taps last, so the keypad tooltips
and the Keyboard tab list Shift+F1 before Ctrl+F1 as well. A key with no eligible chord shows no shortcut.

With the defaults that gives: PF13 to PF24 ⇧F1 to ⇧F12; PA1 to PA3 ⌥1 to ⌥3; Reset ⌃R; Attn ⎋; SysReq ⇧⎋; Clear
⌃⎋ on macOS and Pause elsewhere; Insert ⌃I on macOS and Insert elsewhere; Dup and Field Mark nothing. The
platform is an argument, so the tests pin both answers without running on both.

### 3.3 The menus

`SessionWindow.ApplyKeymap` keeps its shape: the keymap is reversed once and every Keys row is written, on both
menus, on every keymap change. Each row's header is the bare name on both menus. The native item's `Gesture` is
the chord's `KeyGesture` when `MacMenuKeyEquivalents.Installed`, otherwise null. The classic item's
`InputGesture` is the same `KeyGesture`, display-only by Avalonia's contract, on every platform. Insert's check
mark is unchanged. `KeymapHints.Label` and the header-text path go.

### 3.4 Where the full list lives

The keypad tooltips and the Keyboard tab keep every chord in `Describe`'s "A, B or C" line. The menu shows one;
the tooltip and the tab teach the rest.

## 4. Testing

- `KeymapHintsTests`: `MenuChord` per platform, both answers for Clear and Insert; PA1 rebound to F9 shows F9;
  Dup shows nothing; `Ordered` puts Shift ahead of Control.
- `SessionWindowKeymapTests`: both menus carry the same `KeyGesture` and a bare header, before and after a rebind,
  and no native gesture when the override is not installed (a test seam on the helper, since the headless host is
  never macOS with AppKit).
- `NativeMenuTests`: the parity walk keeps comparing the key each item sends. New guard: no native item outside
  Keys declares a gesture without Meta, the ⌘ modifier. The classic menu is outside the invariant: its
  `InputGesture` is display-only, and on Windows and Linux Copy's is Ctrl+C by design.
- `RepositoryHeadersTests` covers the new files as usual.
- The override itself cannot run headless. The PR carries a live check on macOS with the spike's protocol, menu
  closed: ⇧F1, ⌃R and ⌥1 reach the screen, ⌘C and ⌘M still work, and the shortcut column renders. Recorded in
  the PR description with the log lines.

## 5. Documentation

- User guide, the Keys paragraph: each item shows one keystroke that sends it, following your bindings; the
  keypad tooltip and Preferences > Keyboard list them all.
- `CHANGELOG.md`, `## Unreleased`: the #23 clause rewritten, since the header-text form never shipped.
- `src/LizTerm.App/CLAUDE.md`, the menu notes: the override, its invariant, and that the Keys items are the one
  place a native gesture is allowed, replacing the header-text paragraph.
- The native menus design (§2.1) and the editable keymap design (§6.2) stay as written; this spec supersedes
  them for the Keys menu.

## 6. Out of scope

#157 (⌃⎋ Clear on macOS). Any change to which keys the defaults bind. A shortcut column on menus other than
Keys.
