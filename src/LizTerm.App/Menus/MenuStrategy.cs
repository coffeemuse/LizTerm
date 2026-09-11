// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Menus;

/// <summary>Which menu renderer a platform gets, and where About belongs on it.
///
/// The classic in-window menu and the NativeMenuBar both render the same definition, and this picks between
/// them. The default is native on macOS and classic on Windows and Linux, because NativeMenuBar's in-window
/// rendering has not been looked at on either of those platforms — this project has no Windows or Linux GUI,
/// and CI has no GUI at all — and replacing a menu that works with one nobody has seen is the wrong default.
/// LIZTERM_MENU=native is how that gets looked at, without a rebuild. This whole class is a staging device;
/// see section 8 of the spec for what has to be true before the classic menu is deleted.</summary>
internal static class MenuStrategy
{
    public const string Variable = "LIZTERM_MENU";

    public static bool UseNativeMenu =>
        Decide(Environment.GetEnvironmentVariable(Variable), OperatingSystem.IsMacOS());

    /// <summary>Kept pure, taking the platform rather than reading it, so every combination is testable on
    /// every machine — the shape EngineRequirement.Decide uses for the same reason.</summary>
    public static bool Decide(string? variable, bool isMacOS) => variable?.Trim().ToLowerInvariant() switch
    {
        "native" => true,
        "classic" => false,
        // Anything else, blank included: the platform default. A typo that left the app with no menu bar
        // would be a worse outcome than one that quietly draws the usual one.
        _ => isMacOS,
    };

    /// <summary>macOS puts About in the application menu, so the Help item must not also carry one.</summary>
    public static bool AboutInHelpMenu(bool isMacOS) => !isMacOS;

    /// <summary>macOS puts Preferences in the application menu, with Cmd-comma, so the Edit menu carries one
    /// only elsewhere — the same shape as About in Help.</summary>
    public static bool PreferencesInEditMenu(bool isMacOS) => !isMacOS;
}
