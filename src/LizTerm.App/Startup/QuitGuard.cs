// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Dialogs;
using LizTerm.App.Sessions;

namespace LizTerm.App.Startup;

/// <summary>Quit's half of #151: one question for every connected session, asked from the first window a Quit
/// tries to close. Avalonia's TryShutdown makes one pass over the owner-less windows, closing each with
/// ApplicationShutdown and giving up if any refuses; every window's Closing asks <see cref="Holds"/>, so the
/// whole pass is held while the question is up and Keep Connected leaves every window where it was. A forced
/// Shutdown, and an OS shutdown (OSShutdown), are never held. On Disconnect the quit is repeated with the guard
/// lowered for that one pass. App owns one, over its session list, and attaches it to every session window and
/// to the picker.</summary>
/// <param name="sessions">The open sessions, counted for the question and searched for its owner.</param>
/// <param name="confirmEnabled">The preference, read at each pass.</param>
/// <param name="promptFor">The prompt to ask over the given session's window: the current session, so the
/// question appears where the user is looking.</param>
/// <param name="quit">What Disconnect does: App.Quit, whose TryShutdown makes the pass again.</param>
internal sealed class QuitGuard(SessionList sessions, Func<bool> confirmEnabled, Func<SessionEntry, IClosePrompt> promptFor, Action quit)
{
    private bool _confirmed;
    private bool _asking;

    /// <summary>Whether a window closing for <paramref name="reason"/> must refuse, because a Quit is being
    /// questioned. Called from Closing; true means set Cancel.</summary>
    public bool Holds(WindowCloseReason reason)
    {
        var connected = sessions.Entries.Count(entry => entry.Session.IsConnected);
        if (!ClosePolicy.ConfirmsQuit(reason, connected, confirmEnabled(), _confirmed)) return false;
        // The second and later windows of the pass, and a second Cmd+Q while the question is up, ask nothing
        // more; the one answer decides. The question outlives the pass because the dialog is modal and asynchronous,
        // so no answer can land between two windows of the same pass.
        if (!_asking) _ = AskAsync(promptFor(sessions.Current ?? sessions.Entries[0]), connected);
        return true;
    }

    private async Task AskAsync(IClosePrompt prompt, int connected)
    {
        _asking = true;
        try
        {
            if (!await prompt.ConfirmAsync(ClosePromptRequest.ForQuit(connected))) return;
        }
        catch
        {
            // A prompt that failed to open answered nothing, and nothing is the safe answer.
            return;
        }
        finally
        {
            _asking = false;
        }
        // For this one pass only: TryShutdown closes the windows synchronously, and if one refuses (a running
        // transfer's dialog), the next Cmd+Q must ask again rather than slip through.
        _confirmed = true;
        try { quit(); }
        finally { _confirmed = false; }
    }
}
