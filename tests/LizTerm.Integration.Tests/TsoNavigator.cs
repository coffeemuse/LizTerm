// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Drives an MVS 3.8j TSO session: logon to READY, a command that returns to READY, and logoff. Screens
/// are recognized by text only, never by coordinates. The rules, which are all a host must satisfy: the Hercules
/// banner is a screen that says "hit ENTER" (any case) and has no "===>"; the logon screen is the first screen
/// with "===>"; the password prompt says "PASSWORD"; READY means the last non-blank line is exactly READY, which
/// is where IND$FILE must be started from; a "***" pause is answered with Enter and any other "===>" screen (a
/// menu) with PF3. A host whose screens carry none of these strings needs a rule added here, not coordinates. A line
/// reading exactly READY while a "===>" panel is still showing is TSO after a full-screen program ended; see
/// ReachReadyAsync.</summary>
internal sealed class TsoNavigator(IEmulatorSession session, ScreenWaiter screens)
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);
    /// <summary>How long the screen must stand still before a keystroke is sent. The host paints in bursts and
    /// finishes a burst after the text that identifies the screen has appeared, so typing straight away lands
    /// the text wherever the cursor happens to be, or in a field the host is about to rewrite.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);
    /// <summary>How long an ISPF panel must stand still before a transfer is typed into it. After a transfer ISPF
    /// repaints its panel, and that repaint can land after the transfer's own result, so the half-second
    /// <see cref="Quiet"/> is not enough to know the repaint is over. The 2 s count from the last screen update, not
    /// from the end of the transfer. In indfile-ispf-roundtrip.jsonl the host blanks the screen as each transfer
    /// starts, so the wait for a "===>" panel holds out for the repaint and the 2 s run from it; a host that left the
    /// panel up through a transfer would make the typed command the last update, and after a transfer longer than 2 s
    /// the quiet would already be over.</summary>
    private static readonly TimeSpan PanelQuiet = TimeSpan.FromSeconds(2);

    public static bool IsAtReady(string text)
    {
        var last = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return last == "READY";
    }

    /// <summary>A full-screen program that has ended (Wally ISPF after PF3) leaves its panel on the screen, and TSO
    /// writes READY over one of its rows instead of below the last line.</summary>
    public static bool IsReadyOverLeftoverPanel(string text) =>
        text.Contains("===>") && text.Split('\n').Any(l => l.Trim() == "READY");

    public async Task LogonAsync(string user, string password)
    {
        await ReachLogonScreenAsync();
        await TypeAndEnterAsync("LOGON " + user);
        await screens.WaitForAsync(t => t.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase), Step, "the password prompt");
        await TypeAndEnterAsync(password);
        await ReachReadyAsync();
    }

    /// <summary>The Hercules TN3270 server answers a connection with a banner of its own that has no input field,
    /// mentions the word "logon" only in its help text, and waits for ENTER or CLEAR before handing the session
    /// to VTAM. The host's logon screen is the first one with a command prompt.</summary>
    private async Task ReachLogonScreenAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var screen = await screens.WaitForAsync(t => t.Contains("===>") || t.Contains("hit ENTER", StringComparison.OrdinalIgnoreCase), Step, "the connection banner or the logon screen");
            var text = screen.ToText();
            if (text.Contains("===>")) return;
            await screens.WaitForUnlockedKeyboardAsync(Step);
            await session.SendKeyAsync(TerminalKey.Enter);
            await screens.WaitForAsync(t => t != text, Step, "the screen to change");
        }
        throw new InvalidOperationException("Could not reach the logon screen. Last screen:\n" + screens.LatestText);
    }

    /// <summary>Presses Enter through "***" pauses and PF3 out of any menu (ISPF, RPF, a logon proc's panel)
    /// until the screen is at READY. A READY line written over a leftover panel
    /// (<see cref="IsReadyOverLeftoverPanel"/>) is answered with Clear, then Enter if the screen stays blank.</summary>
    public async Task ReachReadyAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await screens.WaitForAsync(t => IsAtReady(t) || t.Contains("***") || t.Contains("===>"), Step, "READY, a *** pause, or a menu");
            // TSO keeps writing for a moment after READY appears, and a command typed into a half-painted screen
            // reaches the host as garbage, so the screen has to stand still before it counts as a prompt.
            await screens.WaitForQuietAsync(Quiet, Step);
            var text = screens.LatestText;
            if (IsAtReady(text)) return;
            if (IsReadyOverLeftoverPanel(text))
            {
                // Clear is safe because the program has ended; on a live Wally ISPF panel the host never answers a
                // Clear and the keyboard stays locked. TSO answers the Clear with READY or with nothing, so a screen
                // that stays blank gets an Enter, which TSO answers with READY.
                await screens.WaitForUnlockedKeyboardAsync(Step);
                await session.SendKeyAsync(TerminalKey.Clear);
                await screens.WaitForAsync(t => t != text, Step, "the screen to clear");
                await screens.WaitForQuietAsync(Quiet, Step);
                if (string.IsNullOrWhiteSpace(screens.LatestText))
                {
                    await screens.WaitForUnlockedKeyboardAsync(Step);
                    await session.SendKeyAsync(TerminalKey.Enter);
                }
                continue;
            }
            // The screen may have moved on during the quiet wait; act only on one of the gate screens, otherwise
            // wait for the next one rather than sending PF3 into whatever is there now.
            if (!(text.Contains("***") || text.Contains("===>"))) continue;
            await screens.WaitForUnlockedKeyboardAsync(Step);
            await session.SendKeyAsync(text.Contains("***") ? TerminalKey.Enter : TerminalKey.PF3);
            await screens.WaitForAsync(t => t != text, Step, "the screen to change");
        }
        throw new InvalidOperationException("Could not reach READY. Last screen:\n" + screens.LatestText);
    }

    /// <summary>Types a TSO command at READY and waits for READY to come back.</summary>
    public async Task CommandAsync(string command)
    {
        await TypeAndEnterAsync(command);
        await ReachReadyAsync();
    }

    /// <summary>Types ISPF at READY and waits for its first panel. False when TSO answers that the command was not
    /// found: the host has no ISPF.</summary>
    public async Task<bool> StartIspfAsync()
    {
        await TypeAndEnterAsync("ISPF");
        var screen = await screens.WaitForAsync(t => t.Contains("===>") || t.Contains("NOT FOUND", StringComparison.OrdinalIgnoreCase), Step, "an ISPF panel");
        if (!screen.ToText().Contains("===>")) return false;
        await WaitForIspfPanelAsync();
        return true;
    }

    /// <summary>Waits until an ISPF panel is ready for a transfer: a "===>" prompt, the screen still for
    /// <see cref="PanelQuiet"/>, and the keyboard unlocked. A "***" pause on the way is answered with Enter. Never
    /// sends Clear: Wally ISPF does not answer one.</summary>
    public async Task WaitForIspfPanelAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await screens.WaitForAsync(t => t.Contains("===>") || t.Contains("***"), Step, "an ISPF panel or a *** pause");
            await screens.WaitForQuietAsync(PanelQuiet, Step);
            var text = screens.LatestText;
            if (text.Contains("***"))
            {
                await screens.WaitForUnlockedKeyboardAsync(Step);
                await session.SendKeyAsync(TerminalKey.Enter);
                await screens.WaitForAsync(t => t != text, Step, "the screen to change");
                continue;
            }
            if (!text.Contains("===>")) continue;
            await screens.WaitForUnlockedKeyboardAsync(Step);
            return;
        }
        throw new InvalidOperationException("Could not reach an ISPF panel. Last screen:\n" + screens.LatestText);
    }

    /// <summary>LOGOFF, then wait for the host to drop the line or show the logon screen again. A host that does
    /// neither within a step is reported as a diagnostic rather than silently returning, because the next run is
    /// then refused with USERID IN USE.</summary>
    public async Task LogoffAsync()
    {
        await TypeAndEnterAsync("LOGOFF");
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < Step)
        {
            if (!session.ConnectionState.IsConnected()) return;
            if (screens.LatestText.Contains("LOGON", StringComparison.OrdinalIgnoreCase) && !IsAtReady(screens.LatestText)) return;
            await Task.Delay(200);
        }
        TestContext.Current.SendDiagnosticMessage($"LOGOFF: the host neither dropped the line nor showed the logon screen within {Step.TotalSeconds:0} s. Last screen:\n{screens.LatestText}");
    }

    private async Task TypeAndEnterAsync(string text)
    {
        await screens.WaitForUnlockedKeyboardAsync(Step);
        await screens.WaitForQuietAsync(Quiet, Step);
        await session.TypeTextAsync(text);
        await session.SendKeyAsync(TerminalKey.Enter);
    }
}
