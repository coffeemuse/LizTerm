# Changelog

What changed in each LizTerm release, newest first. Downloads are on the
[Releases page](https://github.com/coffeemuse/LizTerm/releases). Releases before 0.6.0 are not listed here.

## Unreleased

- **A new app icon**: Liz on a green 3270 screen.

## 0.6.0

- **File transfer from inside ISPF.** In **File > IND$FILE Transfer...**, the new **ISPF (MVS)** host type starts a
  transfer with the cursor on an ISPF `Command ===>` or `Option ===>` line, so there is no need to leave ISPF for a
  TSO `READY` prompt first.
- **Check for updates.** LizTerm checks for a newer release when it starts and tells you only when there is one,
  and **Help > Check for Updates...** checks on demand. Skip a version to hear nothing more until the next one, or
  turn the automatic check off under **Preferences > General > Updates**.
- **Insert mode without an Insert key.** Ctrl+I toggles insert mode, and so does the new **Keys > Insert**, which
  shows a check mark while insert mode is on. The on-screen keypad has an Insert key where Enter was.
- **Start without the splash screen**: turn off **Preferences > General > Splash screen**.
- **A shorter keypad.** Turn off **Show PF1 to PF24** under **Preferences > Window > Keypad** when your keyboard's
  F-keys already cover them.
- **About and Preferences in the in-window menu on macOS.** After choosing **Inside the window**, the window's own
  menu carries **Edit > Preferences...** and **Help > About LizTerm...**, so the choice can always be undone.
- **A dedication to Liz**, the cat LizTerm is named after: **About LizTerm** links to her photo.
- **Fixed:** a profile whose file name did not match the name inside it, such as one renamed by hand, could not be
  starred or deleted, and editing it added a duplicate. Saving a profile also no longer overwrites another profile
  whose name makes the same file name, such as `a/b` and `a:b`.
