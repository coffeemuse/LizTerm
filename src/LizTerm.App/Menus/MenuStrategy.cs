// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.Menus;

/// <summary>Which renderers a platform gets by default, and where About belongs on them.
///
/// The classic in-window menu and the NativeMenuBar both render the same definition. The default is native on
/// macOS and in-window on Windows and Linux, because NativeMenuBar's in-window rendering has not been looked at
/// on either of those platforms — this project has no Windows or Linux GUI, and CI has no GUI at all — and
/// replacing a menu that works with one nobody has seen is the wrong default. #22 is where that default gets
/// flipped. Both renderers are permanent (#70): the in-window one is the only menu anything driving the visual
/// tree can reach, because a NativeMenuItem is not a Control.
///
/// The style a window actually gets is the user's preference, seeded at launch by LIZTERM_MENU and resolved
/// here; SettingsViewModel.MenuStyle holds it and SessionWindow.ApplyMenuStyle acts on it.</summary>
internal static class MenuStrategy
{
    public const string Variable = "LIZTERM_MENU";

    /// <summary>Auto — an untouched settings file — as the style the platform actually gets. Kept pure, taking
    /// the platform rather than reading it, so every combination is testable on every machine: the shape
    /// EngineRequirement.Decide uses for the same reason. Never answers Auto.</summary>
    public static MenuStyle Resolve(MenuStyle style, bool isMacOS) => style switch
    {
        MenuStyle.Auto => isMacOS ? MenuStyle.Native : MenuStyle.InWindow,
        _ => style,
    };

    /// <summary>The style LIZTERM_MENU names, or null for a variable that names none — unset, blank, or a typo.
    /// Null means "no seed", leaving the saved preference to decide, because a value that left the app with no
    /// menu bar would be a worse outcome than one that quietly draws the usual one. "auto" names none either:
    /// Auto is the absence of a choice, so seeding it would seed nothing.</summary>
    public static MenuStyle? FromVariable(string? variable) => variable?.Trim().ToLowerInvariant() switch
    {
        "native" => MenuStyle.Native,
        // The name this variable has used for the in-window menu since it existed, kept working.
        "classic" => MenuStyle.InWindow,
        "both" => MenuStyle.Both,
        _ => null,
    };

    /// <summary>macOS puts About in the application menu, so the Help item must not also carry one.</summary>
    public static bool AboutInHelpMenu(bool isMacOS) => !isMacOS;

    /// <summary>macOS puts Preferences in the application menu, with Cmd-comma, so the Edit menu carries one
    /// only elsewhere — the same shape as About in Help.</summary>
    public static bool PreferencesInEditMenu(bool isMacOS) => !isMacOS;
}
