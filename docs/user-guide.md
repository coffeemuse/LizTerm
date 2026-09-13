# LizTerm user guide

This guide covers everything past the first connection. For downloading and first-run steps, see the
[README](../README.md).

- [Sessions and profiles](#sessions-and-profiles)
- [The session window](#the-session-window)
- [Keyboard](#keyboard)
- [Mouse, selection and clipboard](#mouse-selection-and-clipboard)
- [Finding text](#finding-text)
- [Saving and copying the screen](#saving-and-copying-the-screen)
- [TLS and certificates](#tls-and-certificates)
- [File transfer (IND$FILE)](#file-transfer-indfile)
- [Wire logs](#wire-logs)
- [Menus](#menus)
- [Where LizTerm keeps its files](#where-lizterm-keeps-its-files)
- [Known limitations](#known-limitations)

## Sessions and profiles

LizTerm opens on the **Sessions** list. Select a saved profile and choose **Connect**, or use **New...**,
**Edit...** and **Delete** to manage them. Each session opens in its own window. Closing the last session window
brings the list back; closing the list with no sessions open quits.

Right-click a profile (on macOS, Control-click or a two-finger tap) for **Connect**, **Edit...** and **Mark as
FAVORITE**, which stars it without opening the editor. On a starred profile the same entry reads **Remove from
FAVORITE**. A profile already carrying 8 tags has no room for FAVORITE until you remove one.

Above the list, the **Filter** box narrows it to profiles whose name or tag contains what you type, and the
drop-down beside it narrows to **FAVORITE** or to one tag first. The two work together: the drop-down chooses
the scope, the box searches inside it. Whatever the filter shows, **Quick Connect** still reaches every saved
profile by name.

**Tags...** lists every tag, including ones no profile uses any more. Select one to rename it, give it another
colour, or delete it: each change applies at once to every profile carrying the tag, and the tag's **Used by** list
shows which those are. Renaming a tag to one that already exists merges the two, and both a merge and a delete ask
first. **FAVORITE** is fixed: it can't be renamed, recoloured or deleted.

### Profile settings

| Setting | Meaning |
|---|---|
| Name | How the profile appears in the list, and what you type on the command line to open it. |
| Host, Port | The TN3270 server. |
| Use TLS | Connect over TLS. |
| Verify host certificate | Check the host's certificate (on by default). See [TLS and certificates](#tls-and-certificates). |
| Keep-alive every *n* seconds | Keeps the network connection open through firewalls and routers that drop idle connections. `0` turns it off. It does not stop the host logging you off for being idle. |
| Reconnect automatically | Reconnect if the host drops the session. |
| Model | The screen size: model 2 (24x80), 3 (32x80), 4 (43x80) or 5 (27x132). |
| Extended data stream | Colours and extended attributes (on by default). |
| Oversize | A custom screen size, written as columns x rows (for example `132x43`), for hosts that support one. Leave it blank for the model's own size. |
| Code page | The host's character set. The default is `cp037` (US/Canada); TK4- and TK5 users may want `bracket`, which maps the 3270 bracket characters. |
| LU name | Request a specific logical unit from the host. Optional. |
| Backspace erases | On by default: Backspace erases the previous character. Off: Backspace only moves the cursor left. |
| Mark as FAVORITE | Adds the reserved `FAVORITE` tag, shown as a gold star in the list. |
| Tags | Short labels, separated by commas (`PROD, MVS`), drawn as uppercase colour chips. A tag's colour is picked automatically the first time you use it, and is the same everywhere that tag appears; **Tags...** in the Sessions list changes it. At most 8 per profile, 16 characters each. |
| Note | One short line — "no live data", "LAN only" — shown under the host in the list, and inside the session: see [The session window](#the-session-window). |

### Quick Connect and the command line

To connect without saving a profile first, type a host into the **Quick Connect** box below the list: a name
with a dot or an address (`mvs.example.org`, `192.168.1.10`), `host:port`, or `L:host` for TLS. A one-word name needs a port
(`tk5:3270`), or it is taken as a profile name. The box accepts the same forms as the command line, below, and a
saved profile's name too. Once connected, **File > Save as Profile...** turns the session into a saved profile — including
any certificate you chose to trust.

The box remembers the last 10 hosts you connected to this way, newest first, exactly as you typed them. Open its
drop-down to pick one again, and press Enter to connect. To forget one, select the **✕** beside it, or highlight it
and press Delete. The name of a saved profile is not remembered, because the list already has it.

You can also start LizTerm with a profile name or a host on the command line, which skips the list:

```text
LizTerm [profile | [L:][Y:][lu@]host[:port]]
```

The executable is `LizTerm.App` (`LizTerm.App.exe` on Windows); on macOS it is inside the bundle, at
`LizTerm.app/Contents/MacOS/LizTerm.App`. The host form is x3270's own syntax: `L:` means TLS, `Y:` turns certificate verification off, and `lu@` requests an LU.
Bracket IPv6 addresses, as in `[2001:db8::1]:3270`. The port defaults to 23, or 992 with TLS. A saved profile whose
name matches the argument, ignoring case, always wins.

## The session window

The status bar along the bottom shows, in order: a small note icon, the connection state, the TLS state, the
keyboard state (for example *Ready*, or why the keyboard is locked), insert mode, the cursor position, the model and
LU name, and — while a wire log is recording — a red **● wire log** marker.

When a session connects, a banner above the status bar names the profile, its host and port, its tags and its note,
for the moment you have forgotten which box you are on. It goes away on your first keystroke or click on the screen;
there is nothing to dismiss. A profile with neither tags nor a note shows no banner. Hold the pointer over the note
icon to read the note, or click it to bring the banner back at any time. The profile's tags can also be drawn in
the status bar itself, beside the icon: turn on **Show the profile's tags in the status bar** in Preferences.

If a connection attempt has not finished within 30 seconds, LizTerm gives up and says so. **File > Disconnect**
cancels an attempt that is still in progress.

**View > Crosshair** draws a horizontal line, a vertical line, or both through the cursor, for lining up columns.
The choice is remembered, and applies to every session window; it is also in Preferences.

When the host rings the terminal bell, the screen flashes briefly. A sound can be turned on in Preferences; by
default the bell is silent. A host that rings repeatedly is limited to two bells a second.

**View > Keypad > Show the Keypad** shows a panel of buttons for PF1 to PF24, PA1 to PA3, Enter, Clear, Reset, Attn,
SysReq, Erase EOF, Erase Input, Dup and Field Mark, for the keys a keyboard cannot reach. It is off by default,
remembered, and applies to every session window. The same submenu chooses whether it sits **At the Bottom** of the
window or **On the Right**, and so does Preferences; either way the choice is saved and every open window follows
it. The buttons never take the keyboard away from the screen, and they are greyed out while the session is
disconnected. Once it is connected, hold the pointer over a button to see its keyboard shortcut, where it has one;
a greyed-out button shows none.

## Keyboard

The default layout follows Vista TN3270, cross-checked against wc3270. It cannot be changed yet.

| Key | 3270 key |
|---|---|
| Enter, Ctrl+Enter, or a tap of Right Ctrl | Enter |
| Shift+Enter | Newline |
| Escape | Attn |
| Shift+Escape | SysReq |
| A tap of Left Ctrl, or Ctrl+R | Reset |
| Pause, or Ctrl+Escape | Clear |
| F1 – F12 | PF1 – PF12 |
| Shift+F1 – F12, or Ctrl+F1 – F12 | PF13 – PF24 |
| Page Up / Page Down | PF7 / PF8 |
| Alt+1 | PA1 |
| Alt+2, or Ctrl+Home | PA2 |
| Alt+3, or Ctrl+Page Up | PA3 |
| Tab / Shift+Tab | Tab / Back Tab |
| Insert | Toggle insert mode |
| Home | Home |
| End | Erase EOF |
| Delete | Delete |
| Backspace | Erase the previous character, or move left (see the profile's Backspace setting) |
| Arrow keys | Move the cursor |
| Ctrl+[ | Types `¬` |
| Ctrl+6 | Types `¢` |

On a Mac, Alt is the Option key. A "tap" means pressing and releasing the key on its own, with nothing else in
between.

Copy, Paste, Select All and Find use the platform's own shortcuts — Cmd+C, Cmd+V, Cmd+A and Cmd+F on macOS, Ctrl
elsewhere. Ctrl+Insert copies, on every platform, which is why PA1 lives on Alt+1 rather than Vista's Ctrl+Insert.

The **Keys** menu sends every key the keyboard might not reach: Clear, Reset, Attn, SysReq, Dup, Field Mark, PA1 to
PA3, and PF13 to PF24. The on-screen keypad (**View > Keypad > Show the Keypad**) offers all of those as buttons,
plus PF1 to PF12, Enter, Erase EOF and Erase Input.

## Mouse, selection and clipboard

- Click to move the cursor.
- Drag to select a rectangle of the screen. Double-click selects a word.
- **Copy** copies the selection as text, with trailing spaces trimmed from each line. With nothing selected, use
  **Select All** first.
- **Paste** sends clipboard text to the host. Multi-line text is pasted line by line, respecting the screen's
  margins.

## Finding text

**Edit > Find...** (Cmd+F or Ctrl+F) opens a find bar at the bottom of the window. Every match on the current screen is
highlighted as you type; the search ignores case. Press **Enter** to move the cursor to the next match and
**Shift+Enter** for the previous one; **Escape** closes the bar and returns to the screen. Matches update as the host
repaints, and the highlight stays on your current match where it can.

## Saving and copying the screen

- **File > Save Screen As...** saves the current screen as plain text, or as HTML in the colours you see. Choose the
  format in the save dialog, or end the file name with `.html`.
- **Edit > Copy Screen as HTML** puts the coloured screen on the clipboard as HTML source, for pasting into a web
  page, a wiki, or anything else that takes HTML markup.

Both work after the host has disconnected, as long as the last screen is still showing.

## TLS and certificates

With **Use TLS** and **Verify host certificate** on, LizTerm checks the host's certificate against your operating
system's trusted roots, as a web browser does.

Most hobbyist hosts use a self-signed certificate, which fails that check. When it does, LizTerm opens a
**Certificate not verified** window showing why, and what the host presented:

- Tick **Trust this certificate for this profile**, then choose **Connect Anyway**, to *pin* the certificate: the
  profile is saved with it, verification stays on, and later connections accept exactly that certificate and
  nothing else. If the host sent a chain rather than one self-signed certificate, the box reads **Trust this
  certificate and its issuing CA for this profile**: the pin then trusts that CA for this host's name, so a
  certificate the CA later issues to the same host verifies too. If the host ever presents a certificate the pin
  does not cover, LizTerm warns you with a **Certificate changed** window showing both fingerprints.
- **Connect Anyway** without the tick connects once without verification, and asks again next time.
- **Cancel** stays disconnected.

When a certificate cannot be pinned — on Windows, for example — the window says why and offers only the one-time
connection.

The profile editor shows a pinned certificate's fingerprint. **Forget** removes the pin and returns the profile to
normal verification.

Turning **Verify host certificate** off in a profile skips the check entirely. On a TLS connection, the status bar
says whether the certificate was verified.

## File transfer (IND$FILE)

**File > IND$FILE Transfer...** sends files to, or receives them from, a TSO, VM/CMS or CICS host using the host's
IND$FILE program. Before you start, put the cursor at a TSO `READY` prompt or a command line.

- Choose **Send to host** or **Receive from host**, the local file, and the host file (for example
  `LIZTERM.JCL(JOB1)`, or `'USER.DATA.SET'` with quotes for a fully qualified TSO name).
- **Text** mode translates between ASCII and EBCDIC and can convert line endings; **Binary** copies bytes unchanged.
- **Append to an existing file** adds to the target rather than replacing it. Without it, LizTerm will not receive
  over a local file you did not pick with the Browse dialog.
- When sending, **Advanced** sets up the new host file: record format and LRECL (TSO and VM), BLKSIZE and space
  allocation (TSO only), plus a buffer size and extra options appended to the IND$FILE command.

The dialog shows progress while the transfer runs. **Cancel**, or closing the dialog, asks the host to stop. The
host only answers on its next turn, so if it has stalled, closing the dialog again lets it go. The host's own message is shown when
the transfer ends. The dialog remembers your last transfer for that window.

## Wire logs

A wire log records every message between LizTerm and its emulation engine. It is the most useful thing to attach
to a bug report.

- **Help > Wire Log** starts and stops a log for the current session.
- **Help > Show Wire Logs...** opens the folder they are saved in.

**A wire log contains everything you type, including passwords.** Read it before you share it, or record only
the part of the session after you have logged on.

## Menus

On macOS, LizTerm uses the system menu bar, and About and Preferences are in the LizTerm application menu. On
Windows and Linux, the menu is drawn inside each window.

The Help menu carries the project's own pages, ahead of Wire Log (see [Wire logs](#wire-logs)):

- **Help > User Guide** opens this guide in your browser, from a bundled copy, so it works with no network
  connection. If the platform cannot open a browser, an error names this page's address on GitHub instead.
- **Help > Project on GitHub** opens the project's page on GitHub.
- **Help > Report an Issue...** opens GitHub's form for a new bug report or feature request.
- **Help > Releases** opens the project's releases page on GitHub.

On macOS you can choose, under **Menu bar** in Preferences:

- **Wherever this platform puts it** — the default: the system menu bar on a Mac. Picking one of the three
  below pins that choice instead, and this brings you back.
- **In the system menu bar** — where a Mac user expects it, and what the default already gives you there.
- **Inside the window** — draws the menu in each session window and takes it out of the system menu bar, so no
  menu shortcut can intercept a key the mainframe needs.
- **Both** — draws it in both places at once. The system menu bar is a real menu bar again, so the menu
  shortcuts come back with it: pick this only if you did not need **Inside the window** to keep them off a
  key the mainframe uses.

The choice applies immediately, to every open session window. It is macOS only: on Windows and Linux the menu is
always drawn inside the window, whatever a settings file carried from a Mac may say, so Preferences does not
offer the choice there.

To start LizTerm with a particular style, set the `LIZTERM_MENU` environment variable to `native`, `classic` or
`both` before launching; any other value is ignored, and so is the variable itself away from macOS. It only
decides what that run *starts* with — you can still change it in Preferences afterwards, and doing so saves the
new choice as your preference, including when you pick the style the variable already started you on. Leaving
the variable unset uses whatever you last chose.

The macOS LizTerm application menu (About, Preferences, and Apple's standard items) stays whatever you pick.

## Preferences

**Preferences...** is in the LizTerm application menu on macOS (Cmd-comma), and at the bottom of a session
window's **Edit** menu on Windows and Linux. Changes apply as you make them, to every open session window. The
settings sit on three tabs:

**Display**

- **Crosshair** — the same choice as View > Crosshair.
- **Blinking** — whether text the host marks as blinking actually blinks. Off draws it steady.

**Bell**

- **Flash** — whether the screen flashes when the host rings the bell (on by default).
- **Sound** — what sound plays: none, or the system alert sound, at the volume your system uses for alerts. The
  system alert sound is not available on Linux.

**Window**, top of the window to bottom

- **Menu bar** — macOS only; see [Menus](#menus).
- **Status bar** — whether the profile's tags are drawn in the session window's status bar. Off by default; the
  note icon and the connect banner are always there.
- **Keypad** — whether the on-screen keypad is shown, and whether it docks below the screen or to its right. The
  View > Keypad submenu carries both of the same settings.

## Where LizTerm keeps its files

| System | Folder |
|---|---|
| macOS | `~/Library/Application Support/LizTerm` |
| Windows | `%APPDATA%\LizTerm` |
| Linux | `$XDG_CONFIG_HOME/LizTerm`, or `~/.config/LizTerm` |

Profiles are in `profiles/`, one JSON file each, and wire logs in `logs/`, named
`wire-<profile>-<date>-<time>.log`. `settings.json` holds your preferences — only the ones you have changed, so
deleting it puts everything back to the defaults. `tags.json` holds one colour per tag name; deleting it loses
only the colours, because the tag names themselves live in the profiles and are given fresh colours the next
time LizTerm starts. `recent-hosts.json` holds the hosts Quick Connect remembers; deleting it empties the
drop-down.

## Known limitations

- **Unsigned Windows builds.** Windows SmartScreen warns about the app the first time you open it; the
  [README](../README.md#first-run) explains how to allow it.
- **No certificate pinning on Windows.** The Windows emulation engine verifies against the Windows certificate
  store and cannot pin a certificate. A profile carrying a pin refuses to connect on Windows rather than ignoring
  the pin.
- **TLS hosts that rely on SNI** (several TLS sites sharing one address) cannot be verified, because the
  underlying x3270 engine does not send a server name ([#12](https://github.com/coffeemuse/LizTerm/issues/12)).
- **No keymap editing yet** ([#18](https://github.com/coffeemuse/LizTerm/issues/18)).
- **No printer sessions or scripting.**
