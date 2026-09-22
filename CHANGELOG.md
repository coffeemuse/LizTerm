# Changelog

What changed in each LizTerm release, newest first. Downloads are on the
[Releases page](https://github.com/coffeemuse/LizTerm/releases). Releases before 0.6.0 are not listed here.

## Unreleased

- **A hand-edited `keymap.json` says what went wrong.** A mistyped 3270 key or an unrecognised chord used to lose
  that binding without a word; **Preferences > Keyboard** now names each entry it skipped and what is wrong with it.
  A file that is not valid JSON at all costs every binding in it, so LizTerm now says so when it starts, names the
  line to look at, and keeps your file untouched — with **Reset keymap to defaults** in that tab as the way out,
  which moves the file aside to `keymap.json.bad` rather than deleting it
  ([#168](https://github.com/coffeemuse/LizTerm/issues/168)). A `settings.json` LizTerm will not overwrite now
  names its own problem the same way.

- **The mvsMF sign-in window reads more clearly.** The profile and the host address are on separate lines, the
  window says why it is asking, and an expired session is now shown as the routine timeout it is rather than in
  the same red as a refused password. A blank box says which one is blank
  ([#164](https://github.com/coffeemuse/LizTerm/issues/164)).

- **Sessions no longer drop after thirty seconds on Linux desktops whose language writes decimals with a comma**
  (Croatian, German, French and most of Europe). The emulation engine wrote its timings the local way, LizTerm could
  not read them, and a connection that was working in every visible way was reported as timed out and closed, leaving
  the TSO user logged on at the host ([#170](https://github.com/coffeemuse/LizTerm/issues/170)).
- **About LizTerm's engine line can be copied**, like the version line above it, so a bug report can quote both.

## 0.7.0

- **Ctrl+Escape sends Clear on macOS**, which had no working Clear keystroke before: the chord never reached the
  screen, and Pause is not on Apple keyboards ([#157](https://github.com/coffeemuse/LizTerm/issues/157)).
- **Closing a connected session asks first**, and so does quitting with connected sessions. Turn the question off
  under **Preferences > General > Closing** ([#151](https://github.com/coffeemuse/LizTerm/issues/151)).
- **Keyboard bindings.** **Preferences > Keyboard** lists every 3270 key with the keystrokes that send it: add,
  remove, move or reset them. The on-screen keypad's tooltips and the **Keys** menu's shortcuts follow your bindings
  ([#18](https://github.com/coffeemuse/LizTerm/issues/18), [#23](https://github.com/coffeemuse/LizTerm/issues/23)).
- **Mono terminals.** A profile can be a **Mono (3278)** as well as a **Colour (3279)**, drawn in green phosphor.
  Choose it in the profile editor's Terminal tab ([#123](https://github.com/coffeemuse/LizTerm/issues/123)).
- **mvsMF Access (feature preview).** On MVS 3.8j hosts running [mvsMF](https://github.com/mvslovers/mvsmf) 1.1.0
  or later (it installs on any MVS 3.8j under Hercules, whichever distribution you use), a second way to move files
  beside IND$FILE: list, download, upload and delete, and manage datasets and members, in a window with a **Datasets**
  pane and a **Members** pane that each carry their own actions. It is not yet complete and there are likely bugs;
  see the user guide's
  [mvsMF Access section](https://github.com/coffeemuse/LizTerm/blob/main/docs/user-guide.md#mvsmf-access-preview).
- **A build that is not a release says so.** **About LizTerm** and the splash screen show it as
  `0.7.0-DEV (a1b2c3d)`, naming the commit ([#141](https://github.com/coffeemuse/LizTerm/issues/141)).
- **Wire logs.** **Help > Wire Log** now warns that a log records everything you type, passwords included, and asks
  before starting one. On macOS and Linux the log is readable only by your account
  ([#139](https://github.com/coffeemuse/LizTerm/issues/139)).
- **A clearer message when the emulation engine stops**, instead of a problem shown a moment before the session
  vanishes ([#139](https://github.com/coffeemuse/LizTerm/issues/139)).
- **Binary uploads to MVS/CE work.** File transfers now default to a 2500-byte buffer; change it under **Advanced**
  ([#137](https://github.com/coffeemuse/LizTerm/issues/137)).
- **Custom screen sizes**: choose **Other (custom size)** in the profile editor's Model list and type the columns
  and rows.
- **A tidier profile editor.** Its settings are on tabs, and tags are chips, with the tags you already use offered
  as you type.

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
