# Changelog

What changed in each LizTerm release, newest first. Downloads are on the
[Releases page](https://github.com/coffeemuse/LizTerm/releases). Releases before 0.6.0 are not listed here.

## Unreleased

- **Starting a wire log now asks first.** A wire log records the whole session — everything you type, passwords
  included, and every screen the host sends — so **Help > Wire Log** explains that and asks before it starts one.
- **Wire logs are readable only by your account** on macOS and Linux. They were being written with default
  permissions, which left them readable by every other account on the machine
  ([#139](https://github.com/coffeemuse/LizTerm/issues/139)).
- **A clearer message when the emulation engine stops.** The engine reports some problems as fatal and then exits.
  LizTerm used to show the problem alone, and the session would vanish a moment later with nothing connecting the
  two. It now says that the engine stopped, repeats the engine's explanation, and adds the size and shape of the
  last instruction LizTerm sent — never what you typed, which stays in the wire log
  ([#139](https://github.com/coffeemuse/LizTerm/issues/139)).
- **Binary uploads to MVS/CE work.** File transfers now use a 2500-byte buffer unless you set another size under
  **Advanced**. With the larger size used before, MVS/CE dropped binary uploads at the end, showed its logon screen and
  left the TSO user logged on ([#137](https://github.com/coffeemuse/LizTerm/issues/137)).
- **Custom screen sizes are easier to set.** In the profile editor, choose **Other (custom size)** at the end of the
  Model list, then type the columns and rows in two separate boxes. The boxes show the model's own size the rest of
  the time.
- **A tidier profile editor.** Its settings are now on three tabs, Connection, Terminal and Organize, with more room
  between them. Tags are colored chips you add with Enter or a comma, with the tags you already use offered as you
  type; a new tag shows the color it will get. The custom screen size has arrow buttons, and choosing **Other**
  starts from the model's own size. When **Save** can't accept something, the editor shows that tab and outlines the
  setting.

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
