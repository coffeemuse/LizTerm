# Changelog

What changed in each LizTerm release, newest first. Downloads are on the
[Releases page](https://github.com/coffeemuse/LizTerm/releases). Releases before 0.6.0 are not listed here.

## Unreleased

- **Mono terminals.** A profile can now be a **Mono (3278)** as well as a **Colour (3279)**: the host is told it is a
  3278 and the screen is drawn in a green phosphor, brighter where the host intensifies. Choose it in the profile
  editor's Terminal tab, beside Model ([#123](https://github.com/coffeemuse/LizTerm/issues/123)).
- **mvsMF Browser (feature preview).** On MVS 3.8j hosts running [mvsMF](https://github.com/mvslovers/mvsmf) 1.1.0
  or later, a second way to move files beside IND$FILE: sign in once per session window, then list, download,
  upload and delete. Long lists arrive 500 entries at a time with **Load more** for the rest, and on such a library
  the member filter is the host's work. It is not yet complete and there are likely bugs. See the user guide's
  [mvsMF Browser section](https://github.com/coffeemuse/LizTerm/blob/main/docs/user-guide.md#mvsmf-browser-preview).
- **A build that is not a release says so.** **About LizTerm** and the splash screen show the version as
  `0.6.1-DEV (a1b2c3d)`, naming the commit it was built from, and you can select and copy it for a bug report
  ([#141](https://github.com/coffeemuse/LizTerm/issues/141)).
- **Starting a wire log now asks first.** A wire log records the whole session, everything you type and passwords
  included, so **Help > Wire Log** says so and asks before it starts one.
- **Wire logs are readable only by your account** on macOS and Linux. They used to be readable by every other
  account on the machine ([#139](https://github.com/coffeemuse/LizTerm/issues/139)).
- **A clearer message when the emulation engine stops.** LizTerm now says that the engine stopped and repeats its
  explanation, instead of showing the problem alone a moment before the session vanishes
  ([#139](https://github.com/coffeemuse/LizTerm/issues/139)).
- **Binary uploads to MVS/CE work.** File transfers now use a 2500-byte buffer unless you set another size under
  **Advanced**; the larger size used before left uploads truncated and the TSO user logged on
  ([#137](https://github.com/coffeemuse/LizTerm/issues/137)).
- **Custom screen sizes are easier to set.** Choose **Other (custom size)** at the end of the profile editor's Model
  list, then type the columns and rows in two boxes.
- **A tidier profile editor.** Its settings are now on tabs — Connection, Terminal and Organize, plus mvsMF — and
  tags are colored chips you add with Enter or a comma, with the tags you already use offered as you type. When
  **Save** can't accept something, the editor shows that tab and outlines the setting.

## 0.6.1

- **A new app icon**: Liz on a green 3270 screen, without the "3270" lettering in title bars and on the taskbar,
  where it is too small to read.
- **Switching between sessions.** Cmd+K on macOS, or Ctrl+K on Windows and Linux, lists every open session over the
  current window: press its number, or type part of its name, host, tag or note. A new **Window** menu lists the
  sessions too, with **Keep on Top** for a session you want in view and **Bring All to Front**, and on macOS the Dock
  icon's menu lists them as well.
- **Known issue on Windows:** in testing, LizTerm once failed to start after the installer put 0.6.1 over an
  earlier version, with no error shown. Uninstalling the earlier version first, then installing 0.6.1, fixed it.
  The ZIP archive doesn't use the installer. If this happens to you, please add what you saw to
  [issue #129](https://github.com/coffeemuse/LizTerm/issues/129).

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
