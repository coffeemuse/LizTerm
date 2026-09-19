// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Startup;

/// <summary>The shape both askers of #151 share, a session window's close and Quit. Avalonia's Closing is
/// synchronous, so the asker cancels the close, puts the question up through <see cref="Ask"/>, and on Disconnect
/// repeats the close (or the quit) with <see cref="IsConfirmed"/> set for that one attempt. A second ask while the
/// question is up asks nothing more; the one answer decides. Written once so the two askers cannot drift.</summary>
/// <param name="opened">Called as the question goes up, and <paramref name="closed"/> as it comes down: how a
/// window's question is counted by the Quit guard, so a Quit meanwhile holds without a second question.</param>
internal sealed class CloseQuestion(Action? opened = null, Action? closed = null)
{
    public bool IsOpen { get; private set; }
    public bool IsConfirmed { get; private set; }

    /// <param name="ask">Puts the question up; true is Disconnect.</param>
    /// <param name="repeat">The close, or the quit, made again. It runs synchronously with IsConfirmed set, so a
    /// close something else refuses (a transfer's cancel-first rule) leaves the next attempt asking again.</param>
    /// <param name="failed">Told when the question could not be put up. It is answered Keep Connected, since a
    /// failed question is not the user choosing to disconnect, but the user must hear why nothing happened.</param>
    public void Ask(Func<Task<bool>> ask, Action repeat, Action<Exception> failed)
    {
        if (IsOpen) return;
        _ = AskAsync(ask, repeat, failed);
    }

    private async Task AskAsync(Func<Task<bool>> ask, Action repeat, Action<Exception> failed)
    {
        IsOpen = true;
        opened?.Invoke();
        try
        {
            if (!await ask()) return;
        }
        catch (Exception ex)
        {
            failed(ex);
            return;
        }
        finally
        {
            IsOpen = false;
            closed?.Invoke();
        }
        IsConfirmed = true;
        try { repeat(); }
        finally { IsConfirmed = false; }
    }
}
