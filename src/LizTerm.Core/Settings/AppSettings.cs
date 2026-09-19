// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;

namespace LizTerm.Core.Settings;

/// <summary>App-wide settings: what the user chose, as opposed to how to reach a host (that is SessionProfile).
/// Positional with a default on every parameter, as SessionProfile is, so a field added later reads as its
/// default from an older file and an older build ignores a newer file's extra key. Flat by policy:
/// SettingsLayers merges top-level keys and would replace a nested object whole. The enums are written by name so
/// the file is hand-readable and a reordering of the enum can never change a saved meaning.</summary>
public sealed record AppSettings(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CrosshairMode>))] CrosshairMode Crosshair = CrosshairMode.None,
    bool Blink = true,
    bool VisualBell = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BellSound>))] BellSound BellSound = BellSound.None,
    bool Keypad = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter<KeypadDock>))] KeypadDock KeypadDock = KeypadDock.Bottom,
    [property: JsonConverter(typeof(JsonStringEnumConverter<MenuStyle>))] MenuStyle MenuStyle = MenuStyle.Auto,
    bool ShowTagsInStatusBar = false,
    bool KeypadPfKeys = true,
    bool CheckForUpdatesAutomatically = true,
    string? SkippedUpdateVersion = null,
    bool ShowSplashOnLaunch = true,
    // Whether closing a session window, or quitting, asks first while a session is connected (#151). On by
    // default: the accident it guards against is a stray Cmd/Ctrl+W or Cmd/Ctrl+Q ending a live host session.
    bool ConfirmCloseWhileConnected = true,
    // Hidden on purpose: no Preferences row writes this one. A wire log records every keystroke and every screen
    // the host painted, and LizTerm cannot tell a hobbyist's MVS 3.8 from a production z/OS, so the warning is
    // not one checkbox away from being silenced forever. Someone recording fixtures all day can set it by hand.
    bool WarnBeforeWireLog = true);
