// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Dialogs;

/// <summary>Asked before a session window closes, or the app quits, while a session is connected (#151). True is
/// Disconnect; false, Keep Connected, is what Escape and the title bar answer too. Injected like
/// <see cref="IWireLogPrompt"/> so the window's close path runs against a fake.</summary>
public interface IClosePrompt
{
    Task<bool> ConfirmAsync(ClosePromptRequest request);
}

/// <summary>What the dialog says. The words live here rather than in the window so the two askers, a window's
/// close and Quit, differ only in the request, and a test can read them without a window.</summary>
/// <param name="Title">The window title, naming what is about to happen.</param>
/// <param name="Message">What is connected and what closing does to it.</param>
/// <param name="DisconnectLabel">The destructive button's verb.</param>
public sealed record ClosePromptRequest(string Title, string Message, string DisconnectLabel)
{
    /// <summary>The safe button, the same on both dialogs: it is the default, and Escape.</summary>
    public const string KeepLabel = "Keep Connected";

    public static ClosePromptRequest ForWindow(string profileName) => new(
        $"Close {profileName}?",
        "This session is still connected. Closing the window will disconnect it immediately.",
        "Disconnect");

    public static ClosePromptRequest ForQuit(int connectedSessions) => new(
        "Quit LizTerm?",
        connectedSessions == 1
            ? "1 session is still connected. Quitting will disconnect it immediately."
            : $"{connectedSessions} sessions are still connected. Quitting will disconnect them immediately.",
        "Disconnect and Quit");
}
