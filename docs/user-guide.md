# LizTerm user guide

This guide covers everything past the first connection. For downloading and first-run steps, see the
[README](../README.md).

- [Sessions and profiles](#sessions-and-profiles)
- [The session window](#the-session-window)
- [Several sessions](#several-sessions)
- [Keyboard](#keyboard)
- [Mouse, selection and clipboard](#mouse-selection-and-clipboard)
- [Finding text](#finding-text)
- [Saving and copying the screen](#saving-and-copying-the-screen)
- [TLS and certificates](#tls-and-certificates)
- [File transfer (IND$FILE)](#file-transfer-indfile)
- [mvsMF Browser (preview)](#mvsmf-browser-preview)
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

The editor puts its settings on four tabs: **Connection** (name, host, port, TLS, keep-alive, reconnect),
**Terminal** (model, screen size, code page, LU name, keyboard), **Organize** (FAVORITE, tags, note) and
**mvsMF** (the address and userid for the mvsMF Browser, a feature preview). If
**Save** can't accept something, the editor shows the tab it's on, outlines the setting, and says what to fix beside
the buttons.

| Setting | Meaning |
|---|---|
| Name | How the profile appears in the list, and what you type on the command line to open it. |
| Host, Port | The TN3270 server. |
| Use TLS | Connect over TLS. |
| Verify host certificate | Check the host's certificate (on by default). See [TLS and certificates](#tls-and-certificates). |
| Keep-alive every *n* seconds | Keeps the network connection open through firewalls and routers that drop idle connections. `0` turns it off. It does not stop the host logging you off for being idle. |
| Reconnect automatically | Reconnect if the host drops the session. |
| Model | The screen size: model 2 (24x80), 3 (32x80), 4 (43x80) or 5 (27x132), or **Other (custom size)** for a size of your own, for hosts that support one. |
| Colour / Mono | **Colour (3279)**, the default, or **Mono (3278)**. A mono profile tells the host it is a 3278, so the host leaves colour out of what it sends, and the screen is drawn in one green phosphor, brighter where the host intensifies. Some applications lay out their panels differently for a mono terminal. |
| Extended data stream | Colours and extended attributes (on by default). It applies to both terminal types. |
| Screen size | Columns and rows. They show the model's own size, and you can change them, by typing or with the arrows, only when Model is **Other**, which starts from the size already shown. A custom size must be at least 80 columns and 24 rows, and columns times rows can be at most 16,383 (at 160 columns, 102 rows at most). |
| Code page | The host's character set. The default is `cp037` (US/Canada); TK4- and TK5 users may want `bracket`, which maps the 3270 bracket characters. |
| LU name | Request a specific logical unit from the host. Optional. |
| Backspace erases | On by default: Backspace erases the previous character. Off: Backspace only moves the cursor left. |
| Mark as FAVORITE | Adds the reserved `FAVORITE` tag, shown as a gold star in the list. |
| Tags | Short labels, drawn as uppercase color chips. Type a tag and press Enter or type a comma to add it; the list under the box offers the tags you already use. Select a chip's **×** to remove it, or press Backspace in the empty box to remove the last one. A new tag's chip already shows the color it will get, and a tag's color is the same everywhere it appears; **Tags...** in the Sessions list changes it. At most 8 per profile, FAVORITE included, 16 characters each. |
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

The status bar along the bottom is a 3270 Operator Information Area, drawn with the symbols x3270 uses. Every
symbol has its plain words on a tooltip. From left to right:

- A small note icon (see below).
- The mode field: a boxed **4**, then an underlined **A** or **B** for a TN3270 or TN3270E connection, then a solid
  box once a 3270 session is bound, a boxed **?** while there is none, or **N** for a host in NVT mode. Its tooltip
  also names the terminal model.
- On a TLS connection, a padlock with a green **✓** when the host's certificate was verified, or an orange **!** when
  it was not.
- The message area: blank while the keyboard is free, otherwise a lock **X** and why. While connecting it shows the
  broken wire and the step in brackets; then a clock while the host works, **SYSTEM** once the host has acknowledged
  and is still busy, and in red the operator errors — for example the little figure between arrows for typing into
  a protected field, which Esc clears.
- On the right: the insert-mode caret, the LU name the host assigned, the cursor as row/column, and — while a wire
  log is recording — a red **● wire log** marker.

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

**View > Keypad > Show the Keypad** shows a panel of buttons for PF1 to PF24, PA1 to PA3, Insert, Clear, Reset, Attn,
SysReq, Erase EOF, Erase Input, Dup and Field Mark, for the keys a keyboard cannot reach. It is off by default,
remembered, and applies to every session window. The same submenu chooses whether it sits **At the Bottom** of the
window or **On the Right**, and so does Preferences; either way the choice is saved and every open window follows
it. If your keyboard's F-keys already cover PF1 to PF24, turn off **Show PF1 to PF24** in Preferences to leave only
PA1 to Field Mark: one row at the bottom, or one column on the right. The buttons never take the keyboard away from
the screen, and they are greyed out while the session is disconnected. Once it is connected, hold the pointer over a
button to see its keyboard shortcut, where it has one; a greyed-out button shows none.

## Several sessions

Each session has its own window, so with several open they mix in with every other application's windows. There
are three ways back to the one you want:

- **Cmd+K** on macOS, or **Ctrl+K** on Windows and Linux, opens a list of every open session over the current
  window; so does **Window > Switch Session...**. Sessions are numbered in the order you opened them, 1 to 9 and
  then 0 for the tenth: press a number to go straight there. Or type part of a session's name, host, tag or note
  to narrow the list, then use the arrow keys and **Enter**, or click a session. The session you were in before
  this one is highlighted when the list opens, so **Cmd+K** then **Enter** flips between two sessions. **Escape**
  closes the list. Nothing you type while it is open is sent to the host.
- The **Window** menu lists the same numbered sessions, with a check mark on the session you are in. On Windows
  and Linux each number is also the item's access key, so **Alt**, **W**, **3** reaches session 3. **Bring All to
  Front** raises every LizTerm session that is not minimised above other applications' windows. On macOS the menu
  also has **Minimize** (Cmd+M, which works only while the menu is in the system menu bar) and **Zoom**.
- On macOS, right-click LizTerm's icon in the Dock to see the sessions there too, and choose one to go straight to
  it from any application. **New Session...** in the same menu opens the Sessions list.

In the list, a session shows its star if it is a favourite, then its tags, host and note, a filled circle ● while
it is connected or an open circle ○ and the word **Disconnected** when it is not, and **This window** or **On top**
where they apply. A session that is not a saved profile — Quick Connect, or a host named on the command line —
shows **Quick Connect** under its name.

**Window > Keep on Top** keeps that window above other applications' windows, for a session you want in view while
you work in another, such as an operator console. It is not remembered: a new window starts without it.

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
| Insert, or Ctrl+I | Toggle insert mode |
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
The session switcher is Cmd+K on macOS and Ctrl+K elsewhere; see [Several sessions](#several-sessions).

The **Keys** menu sends every key the keyboard might not reach: Clear, Reset, Attn, SysReq, Dup, Field Mark, Insert,
PA1 to PA3, and PF13 to PF24. Insert has a check mark while insert mode is on. The on-screen keypad
(**View > Keypad > Show the Keypad**) offers all of those as buttons, plus PF1 to PF12, Erase EOF and Erase Input.

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

Turning **Verify host certificate** off in a profile skips the check entirely. On a TLS connection, the status bar's
padlock shows whether the certificate was verified: green with a **✓**, or orange with a **!**.

## File transfer (IND$FILE)

**File > IND$FILE Transfer...** sends files to, or receives them from, a TSO, VM/CMS or CICS host using the host's
IND$FILE program. It types the IND$FILE command into the input field the cursor is in, so before you start, put the
cursor at a TSO `READY` prompt or a command line, or use the ISPF (MVS) host type.

- **ISPF (MVS)** starts a transfer from inside ISPF, with the cursor on a `Command ===>` or `Option ===>` line.
  LizTerm types `TSO` ahead of the command, which makes ISPF hand it to TSO, so everything else works as it does for
  TSO, the Advanced options included. ISPF's own TSO command panel (option 6, Command) also accepts a transfer with
  host type TSO.

- Choose **Send to host** or **Receive from host**, the local file, and the host file (for example
  `LIZTERM.JCL(JOB1)`, or `'USER.DATA.SET'` with quotes for a fully qualified TSO name).
- **Text** mode translates between ASCII and EBCDIC and can convert line endings; **Binary** copies bytes unchanged.
- **Append to an existing file** adds to the target rather than replacing it. Without it, LizTerm will not receive
  over a local file you did not pick with the Browse dialog.
- When sending, **Advanced** sets up the new host file: record format and LRECL (TSO and VM), BLKSIZE and space
  allocation (TSO only), plus a buffer size and extra options appended to the IND$FILE command.
- **Buffer size** is how much data travels in each piece of the transfer. It starts at 2500, and a blank field means
  2500 too. Some hosts, MVS/CE among them, cannot take much larger pieces: the transfer stops at the end, the TSO
  logon screen appears, and the TSO user stays logged on until an operator cancels it. If a transfer ends that way,
  try a smaller buffer size.

The dialog shows progress while the transfer runs. **Cancel**, or closing the dialog, asks the host to stop. The
host only answers on its next turn, so if it has stalled, closing the dialog again lets it go. The host's own message is shown when
the transfer ends. The dialog remembers your last transfer for that window.

## mvsMF Browser (preview)

**This is a feature preview.** It works, but it is new and still being refined, and it has been tested against one
build of mvsMF (1.1.0). It follows that build's behaviour; older builds are not supported (see
[Signing in](#signing-in)). Please report what you find
([Help > Report an Issue...](https://github.com/coffeemuse/LizTerm/issues/new/choose)).

[mvsMF](https://github.com/mvslovers/mvsmf) is a z/OSMF-style REST server for MVS 3.8j. The **mvsMF Browser** uses
it to list datasets and members, download and upload them, and delete members, without typing anything on the
3270 screen. It is a second way to move files alongside [IND$FILE](#file-transfer-indfile), and it doesn't need the
session to be logged on, or even connected.

### Setting it up

Open the profile in the editor and go to the **mvsMF** tab:

- **URL** is the address of the mvsMF server, for example `http://mvs.example:8080`. When the address has no path,
  LizTerm adds `/zosmf`. Only `http://` and `https://` addresses are accepted.
- **Userid** is optional. It fills in the sign-in window, and the browser starts by listing `USERID.**`.
- **Test** checks the server is reachable, then signs in and asks it what it is, showing the answer (for example
  **✓ Connected: mvsMF 1.1.0 on MVS 3.8j**) or what went wrong. If the URL cannot be reached, it says so without
  asking for a password. A sign-in made for **Test** is ended right after.

Once a profile has a URL, its session window has **File > mvsMF Browser...**. The item has no keyboard shortcut, so
it never takes a key the host needs. Choosing it again brings the open browser back to the front: each session
window has one browser, which closes when the session window does. A change to the URL takes effect the next time
you open the session.

### Signing in

**LizTerm needs mvsMF 1.1.0 or later.** Older builds have no sign-in service, and LizTerm says so rather than
connecting.

The first time the browser reaches the host, it asks for your userid and password. LizTerm uses them once to sign
in, then keeps only a session token in memory; the password is not stored. Every browser operation in that session
uses the token, so you sign in once. mvsMF forgets an idle session after about 30 minutes, and then LizTerm asks
you to sign in again. Closing the session window signs you out. LizTerm cannot guarantee the password is wiped from
memory, because .NET gives no way to erase a string.

Your password crosses the network once, at sign-in. Over `http://` it is unencrypted, so keep plain `http` to a
network you trust, or put mvsMF behind a TLS reverse proxy and use an `https://` URL. An `https`
certificate is checked the same way as a TLS session's (see [TLS and certificates](#tls-and-certificates)): an
untrusted one opens the **Certificate not verified** window, and **Trust this certificate** pins it to the profile.
The mvsMF pin is kept apart from the 3270 one, and **Forget** on the **mvsMF** tab removes it. An mvsMF pin trusts
only the exact certificate you pinned, and only until that certificate expires.

### Browsing

Type a dataset pattern in **Filter**, such as `MVSCE02.**`, and choose **List**. The left pane lists the matching
datasets with their **NAME**, **DSORG**, **RECFM** and **LRECL**. VSAM and direct-access (`DA`) datasets are listed
as **(not supported)**: nothing can be downloaded from or uploaded to them.

Choosing a partitioned dataset (a PDS) lists its members on the right, where **Filter members** narrows the list as
you type and you can select several members at once. For a sequential dataset, **Download…** and **Upload…** act on
the dataset itself.

**Mode** chooses **Text**, which converts between EBCDIC and your computer's characters, or **Binary**, which copies
bytes unchanged. **Binary** is chosen for you on an undefined-length (`RECFM=U`) dataset, such as a load library.

The keyboard reaches everything. In the member list, **Enter** downloads the selection, and **Delete** or
**Backspace** deletes it. **Escape** answers a question with Cancel, else cancels what is running, else closes an
upload review, else closes the window.

### Downloading

With one member (or a sequential dataset) selected, **Download…** asks where to save it, suggesting the member name
with `.txt` in text mode. With several selected, it asks for a folder and downloads two at a time, showing each
member's progress in its **STATUS** column. A file that already exists in that folder is not replaced without
asking: choose **Replace** or **Skip**, and tick **Apply to all** to answer for the rest.

A download is written to a hidden temporary file first and moved into place only when it is complete, so a
cancelled or failed download never leaves a half-written file under the real name.

- In text mode, **Trim trailing blanks** (on by default) removes the spaces that pad out each fixed-length record.
  Lines end the way your system expects: LF on macOS and Linux, CRLF on Windows.
- In binary mode, a fixed-length dataset hands back whole records, so its last record is padded with zero bytes; a
  note in the window says so.

### Uploading

**Upload…** on a partitioned dataset asks for one or more files and opens a review in place of the member list.
Each file becomes the member named by its file name up to the first dot, in capitals (`hello.jcl` becomes `HELLO`).
You can edit a name, and a name MVS won't accept is marked **✗** until you fix it. Before anything is sent, LizTerm
checks each text file against the dataset:

- **Characters**: the file must be UTF-8 text, and every character must exist in the host code page (the Latin-1
  range). The review lists the first few that don't; send such a file in **Binary** instead.
- **Line length**: no line may be longer than a record holds (LRECL, less 4 for variable-length datasets). mvsMF
  would cut a long line short and still write the rest, so LizTerm refuses the file instead.
- **Tabs**: **Expand tabs (every 8 columns)** (on by default) turns them into spaces. Left off, each tab reaches the
  host as a tab character.
- **Empty lines** become blank records on the host.

Choose **Upload** to start. Before replacing a member that already exists, the browser asks **Replace** or
**Skip**. With **Verify after upload** on (the default), each text member is read back and compared with what was
sent. A member that differs is marked **⚠ Uploaded, but the host copy differs at line N**.

A sequential dataset takes one file at a time. After the checks, the browser asks before replacing the dataset's
contents.

mvsMF cannot undo a write. If an upload fails or is cancelled while a member is being written, the member may be
left partly written, and its row says so.

### Deleting

**Delete…** deletes the selected members, after a question that names them (**Delete 3 members**). A deleted
member cannot be recovered. If you cancel part-way, the browser lists the members again and says how many were
deleted. Datasets themselves can't be deleted from the browser.

### When something goes wrong

A problem with one member shows in its row or in the status line at the bottom of the window. A problem with the
connection itself (the host can't be reached, the sign-in was refused, or the certificate isn't trusted) shows as a
red banner with **Retry**. Every status line starts with a mark as well as words (**✓**, **✗**, **⚠**, **⟳** or
**–**), so its meaning never depends on colour.
## Which build you are running

**Help > About LizTerm...** names the version, and so does the splash screen. A build that is not a release — one
made from source, or from a pull request — shows it as `0.6.1-DEV (a1b2c3d)`, where `a1b2c3d` is the commit it was
built from; a build made outside a git checkout, such as one from a downloaded source archive, says `0.6.1-DEV` with
no commit. A release shows its plain version, with no `-DEV` and no commit. The line in About can be selected and
copied, so a bug report can say exactly which build it was filed against.

## Wire logs

A wire log records every message between LizTerm and its emulation engine. It is the most useful thing to attach
to a bug report.

- **Help > Wire Log** starts and stops a log for the current session. Starting one asks first, and says what a
  log records — every start asks, because it is easy to forget what is in the file by the time you send it.
- **Help > Show Wire Logs...** opens the folder they are saved in.

**A wire log records the whole session, in both directions**: everything you type, including passwords — and a
password typed as part of a longer command, such as `LOGON JOHN/SECRET`, is recorded in full — and every screen
the host sends, so whatever data the session displays. Read it before you share it, or record only the part of
the session you need.

On macOS and Linux the file is readable only by your account. Nothing prunes the folder, so old logs stay until
you delete them.

## Menus

On macOS, LizTerm uses the system menu bar, and About and Preferences are in the LizTerm application menu. On
Windows and Linux, the menu is drawn inside each window.

The Help menu carries the project's own pages, ahead of Wire Log (see [Wire logs](#wire-logs)):

- **Help > User Guide** opens this guide in your browser, from a bundled copy, so it works with no network
  connection. If the platform cannot open a browser, an error names this page's address on GitHub instead.
- **Help > Project on GitHub** opens the project's page on GitHub.
- **Help > Report an Issue...** opens GitHub's form for a new bug report or feature request.
- **Help > Releases** opens the project's releases page on GitHub.
- **Help > Check for Updates...** checks now, and always tells you the result — a newer version, or that you are
  up to date, or why the check failed. See [Preferences](#preferences) for the automatic version of this check.

The **Window** menu lists the open sessions and holds **Keep on Top**, **Switch Session...** and **Bring All to
Front**, and on macOS **Minimize** and **Zoom**; see [Several sessions](#several-sessions).

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

The macOS LizTerm application menu (About, Preferences, and Apple's standard items) stays whatever you pick, and
Cmd-comma opens Preferences under all of them. A menu drawn inside the window carries **Edit > Preferences...** and
**Help > About LizTerm...** as well, so after picking **Inside the window** you can still reach this setting from the
window itself.

## Preferences

**Preferences...** is in the LizTerm application menu on macOS (Cmd-comma). It is also at the bottom of the
**Edit** menu wherever a session window draws its menu inside the window: always on Windows and Linux, and on macOS
under **Inside the window** or **Both**. Changes apply as you make them, to every open session window. The settings
sit on four tabs:

**General**

- **Splash screen** — whether LizTerm shows its splash screen when it starts (on by default). Off, LizTerm opens
  straight to the Sessions list, or to the session you named on the command line. The change applies from the next
  time LizTerm starts.
- **Updates** — whether LizTerm checks for a newer release automatically when it starts (on by default). A check
  never interrupts you unless there is something new, and skipping a version keeps it quiet only until the next
  one is published. Help > Check for Updates... works either way. A check is one request to GitHub
  (`api.github.com`) that sends LizTerm's version number and nothing about your profiles or sessions.

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
  View > Keypad submenu carries both of the same settings. **Show PF1 to PF24**, on by default, is here only: turn
  it off when your keyboard's F-keys are enough, and the keypad keeps just PA1 to Field Mark.

## Where LizTerm keeps its files

| System | Folder |
|---|---|
| macOS | `~/Library/Application Support/LizTerm` |
| Windows | `%APPDATA%\LizTerm` |
| Linux | `$XDG_CONFIG_HOME/LizTerm`, or `~/.config/LizTerm` |

Profiles are in `profiles/`, one JSON file each, and wire logs in `logs/`, named
`wire-<profile>-<date>-<time>.log`. An older LizTerm reading a profile written by a newer one keeps the settings it
understands and drops the rest the next time it saves that profile: going back to a LizTerm without the mvsMF
Browser loses that profile's mvsMF URL, userid and pinned certificate. `settings.json` holds your preferences — only
the ones you have changed, so deleting it puts everything back to the defaults. `tags.json` holds one colour per tag
name; deleting it loses only the colours, because the tag names themselves live in the profiles and are given fresh
colours the next time LizTerm starts. `recent-hosts.json` holds the hosts Quick Connect remembers; deleting it
empties the drop-down.

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
- **The mvsMF Browser is a preview** ([#17](https://github.com/coffeemuse/LizTerm/issues/17)). Older mvsMF builds
  are not supported (see [Signing in](#signing-in)). Very long dataset or member lists arrive in one piece, with no
  paging. It
  can't create, rename or delete datasets, submit jobs, or browse the z/OS UNIX file system.
