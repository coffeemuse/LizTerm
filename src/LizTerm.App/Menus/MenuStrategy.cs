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

    /// <summary>A stored style as the style the platform actually gets. Kept pure, taking the platform rather
    /// than reading it, so every combination is testable on every machine: the shape EngineRequirement.Decide
    /// uses for the same reason. Never answers Auto.
    ///
    /// Off macOS the answer is always InWindow, whatever the file says. Both draws two bars stacked there
    /// (NativeMenuBar renders in-window where there is no exporter, and both are docked Top), and Native hands
    /// the definition to a global-menu registrar or to a renderer nobody has reviewed (#22) — states
    /// MenuStyleChoosable gives no control to leave, so they must not be reachable at all. A settings file
    /// carried between platforms, or hand-edited, is exactly how they otherwise would be.
    ///
    /// This is also the one place an out-of-range value is normalised. A settings file is text a user can edit
    /// and JsonStringEnumConverter accepts integers, so `"menuStyle": 9` reads back as (MenuStyle)9 rather than
    /// falling to Auto the way an unknown *name* does; left alone it names no renderer at all.</summary>
    public static MenuStyle Resolve(MenuStyle style, bool isMacOS) => isMacOS
        ? style switch
        {
            MenuStyle.Native or MenuStyle.InWindow or MenuStyle.Both => style,
            _ => MenuStyle.Native,
        }
        : MenuStyle.InWindow;

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

    /// <summary>Whether the menu style is a choice worth offering here, and so whether Preferences shows the
    /// group. Only macOS draws the two renderers in different places; elsewhere Resolve answers InWindow for
    /// every style, so there is nothing to choose. The two answers are the same rule and belong in the same
    /// file — a group offered where Resolve ignores it, or ignored where it is offered, is the bug.</summary>
    public static bool MenuStyleChoosable(bool isMacOS) => isMacOS;

    /// <summary>macOS puts About in the application menu, so the Help item must not also carry one.</summary>
    public static bool AboutInHelpMenu(bool isMacOS) => !isMacOS;

    /// <summary>macOS puts Preferences in the application menu, with Cmd-comma, so the Edit menu carries one
    /// only elsewhere — the same shape as About in Help.</summary>
    public static bool PreferencesInEditMenu(bool isMacOS) => !isMacOS;
}
