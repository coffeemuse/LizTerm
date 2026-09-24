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
/// Shutdown, and a system logout, restart or shutdown, are never held: the platform reports them as OSShutdown,
/// the macOS backend only since Avalonia 12.1.3 (#188), and <paramref name="isSystemShutdown"/> is the second
/// reading LizTerm made while it reported none (see <see cref="ClosePolicy.ConfirmsQuit"/>). On Disconnect the
/// quit is repeated with the guard lowered for that
/// one pass. App owns one, over its session list, and attaches it to every session window and to the
/// picker.</summary>
/// <param name="sessions">The open sessions, counted for the question and searched for its owner.</param>
/// <param name="confirmEnabled">The preference, read at each pass.</param>
/// <param name="isSystemShutdown">Whether the login session is ending, read at each pass: Platform/MacQuitReason
/// in the app, which answers false off macOS, and a plain flag in tests (#169).</param>
/// <param name="promptFor">The prompt to ask over the given session's window: the current session, so the
/// question appears where the user is looking.</param>
/// <param name="quit">What Disconnect does: App.Quit, whose TryShutdown makes the pass again.</param>
internal sealed class QuitGuard(SessionList sessions, Func<bool> confirmEnabled, Func<bool> isSystemShutdown, Func<SessionEntry, IClosePrompt> promptFor, Action quit)
{
    private readonly CloseQuestion _question = new();
    private int _windowQuestions;

    /// <summary>The Quit question is up. A session window closing meanwhile refuses rather than asking its own.</summary>
    public bool IsAsking => _question.IsOpen;

    /// <summary>A session window's own question, counted here while it is up: a Quit meanwhile holds every window
    /// without putting a second question on the screen, and the open one decides for its window.</summary>
    public CloseQuestion NewWindowQuestion() => new(() => _windowQuestions++, () => _windowQuestions--);

    /// <summary>Whether this close is the system logging out, restarting or shutting down (#169), for a caller
    /// that must let it past a question it has already put on the screen. The macOS half is read here, at each
    /// pass, exactly as <see cref="Holds"/> reads it.</summary>
    public bool IsSystemShutdown(WindowCloseReason reason) => ClosePolicy.IsSystemShutdown(reason, isSystemShutdown());

    /// <summary>Whether a window closing for <paramref name="reason"/> must refuse, because a Quit is being
    /// questioned. Called from Closing; true means set Cancel.</summary>
    public bool Holds(WindowCloseReason reason)
    {
        var connected = sessions.Entries.Count(entry => entry.Session.IsConnected);
        if (!ClosePolicy.ConfirmsQuit(reason, isSystemShutdown(), connected, confirmEnabled(), _question.IsConfirmed)) return false;
        // The second and later windows of the pass, and a second Cmd+Q while the question is up, ask nothing
        // more; the one answer decides. The question outlives the pass because the dialog is modal and asynchronous,
        // so no answer can land between two windows of the same pass. A window's own question already up holds the
        // pass the same way, and asks nothing: two questions never share a screen.
        if (_windowQuestions == 0)
        {
            var owner = sessions.Current ?? sessions.Entries[0];
            _question.Ask(
                () => promptFor(owner).ConfirmAsync(ClosePromptRequest.ForQuit(connected)),
                quit,
                ex => owner.Session.ErrorMessage = "Could not ask before quitting: " + ex.Message);
        }
        return true;
    }
}
