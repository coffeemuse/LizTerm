// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Startup;

/// <summary>What launch tells someone whose keymap.json this build cannot read at all (#168). Every binding in it
/// is out of force and nothing in the app would say so until Preferences > Keyboard was opened, so the notice goes
/// up over whatever the plan opened — the picker included, since a user may never open a session. A file that
/// merely holds an entry or two this build skipped gets no notice: the rest of it works, the editor still saves,
/// and the tab's own note names what was passed over. Pure, so the decision is tested without a window.</summary>
/// <param name="Message">Why the file would not read and what stands in for it, KeymapViewModel.LoadError's words.</param>
/// <param name="Path">The file to go and fix.</param>
public sealed record KeymapNotice(string Message, string Path)
{
    public const string Title = "Keyboard bindings";

    /// <summary>Where the way out is. The notice offers none itself: it opens before the user has settled on
    /// anything, and a reset is not what a stray comma needs.</summary>
    public const string Pointer = "Preferences > Keyboard says the same thing, and offers a reset that starts again from the defaults.";

    public const string DismissLabel = "OK";

    /// <summary>The notice for this launch, or null when there is nothing to say: the keymap read, there is no file
    /// behind it, or the plan is the startup error window, which quits when it closes and needs no second message.</summary>
    public static KeymapNotice? For(StartupPlan plan, string? loadError, string? filePath) =>
        plan is StartupPlan.ShowError || loadError is null || filePath is null ? null : new(loadError, filePath);
}
