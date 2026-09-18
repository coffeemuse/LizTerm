// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Media;
using Avalonia.Media.Immutable;
using LizTerm.Core.Screen;

namespace LizTerm.App.Rendering;

/// <summary>Fixed v1 color scheme following IBM host color names. Theming is deferred.</summary>
public static class Palette
{
    public static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromRgb(0, 0, 0));

    /// <summary>Overlay for the mouse selection: muted blue at 40% opacity so host colors stay readable under it.</summary>
    public static readonly IBrush Selection = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0x60, 0x90, 0xE0));

    /// <summary>The crosshair ruler. Dimmer than Selection because it is on screen continuously rather than
    /// for as long as a drag lasts, and host text has to stay readable straight through it.</summary>
    public static readonly IBrush Crosshair = new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0xE0, 0xE0, 0x60));

    /// <summary>A find match, and the one the user is on. Amber rather than Selection's blue so a search over a
    /// selected region stays readable, and the current match is opaque enough to pick out of a screenful.</summary>
    public static readonly IBrush FindMatch = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0xE0, 0xA0, 0x20));
    public static readonly IBrush FindCurrent = new ImmutableSolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xD0, 0x40));

    /// <summary>The visual bell: painted over the whole screen for TerminalScreen.BellFlashDuration. White at about
    /// 35% so the screen visibly lights up without inverting to a white slab (bell spec §4).</summary>
    public static readonly IBrush BellFlash = new ImmutableSolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));

    /// <summary>Which of the two find brushes paints one match: <see cref="FindCurrent"/> for the match the user
    /// is on, else <see cref="FindMatch"/>. Extracted out of TerminalScreen.DrawFindMatches's ternary as a pure
    /// predicate: this project asserts overlay painting through geometry helpers (CrosshairGeometry,
    /// FindMatchGeometry) rather than pixels, and neither of those can see which brush a rectangle got, so the
    /// choice itself has to be reachable some other way to be testable at all.</summary>
    public static IBrush FindMatchBrush(bool isCurrent) => isCurrent ? FindCurrent : FindMatch;

    private static readonly Dictionary<HostColor, Color> Colors = new()
    {
        [HostColor.Default] = Color.FromRgb(0xF0, 0xF0, 0xF0),
        [HostColor.NeutralBlack] = Color.FromRgb(0x00, 0x00, 0x00),
        [HostColor.Blue] = Color.FromRgb(0x5C, 0x8A, 0xFF),
        [HostColor.Red] = Color.FromRgb(0xFF, 0x50, 0x50),
        [HostColor.Pink] = Color.FromRgb(0xFF, 0x70, 0xFF),
        [HostColor.Green] = Color.FromRgb(0x50, 0xFF, 0x50),
        [HostColor.Turquoise] = Color.FromRgb(0x50, 0xFF, 0xFF),
        [HostColor.Yellow] = Color.FromRgb(0xFF, 0xFF, 0x50),
        [HostColor.NeutralWhite] = Color.FromRgb(0xF0, 0xF0, 0xF0),
        [HostColor.Black] = Color.FromRgb(0x00, 0x00, 0x00),
        [HostColor.DeepBlue] = Color.FromRgb(0x20, 0x20, 0xB0),
        [HostColor.Orange] = Color.FromRgb(0xFF, 0xA0, 0x40),
        [HostColor.Purple] = Color.FromRgb(0xB0, 0x60, 0xFF),
        [HostColor.PaleGreen] = Color.FromRgb(0xA0, 0xFF, 0xA0),
        [HostColor.PaleTurquoise] = Color.FromRgb(0xA0, 0xFF, 0xFF),
        [HostColor.Grey] = Color.FromRgb(0xA0, 0xA0, 0xA0),
        [HostColor.White] = Color.FromRgb(0xFF, 0xFF, 0xFF),
    };

    private static readonly Dictionary<(HostColor, bool), IBrush> Cache = new();

    public static Color ColorOf(HostColor color) => Colors[color];

    /// <summary>The one colour a mono (3278) screen is drawn in: a green phosphor, with intensified text blended
    /// brighter exactly as the colour path blends. b3270 reports no colour at all for a 3278, so a mono session
    /// never reaches the table above; this is the single value #78's themes override for amber or white.</summary>
    public static readonly Color Phosphor = Color.FromRgb(0x33, 0xFF, 0x33);

    private static readonly IBrush PhosphorPlain = new ImmutableSolidColorBrush(Phosphor);
    private static readonly IBrush PhosphorBright = new ImmutableSolidColorBrush(Color.FromRgb(Blend(Phosphor.R), Blend(Phosphor.G), Blend(Phosphor.B)));

    public static IBrush PhosphorBrush(bool bright) => bright ? PhosphorBright : PhosphorPlain;

    /// <param name="bright">Intensified (highlight) rendition: blend 35% toward white.</param>
    public static IBrush Brush(HostColor color, bool bright)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((color, bright), out var brush)) return brush;
            var c = ColorOf(color);
            if (bright)
                c = Color.FromRgb(Blend(c.R), Blend(c.G), Blend(c.B));
            brush = new ImmutableSolidColorBrush(c);
            Cache[(color, bright)] = brush;
            return brush;
        }
    }

    private static byte Blend(byte channel) => (byte)(channel + (255 - channel) * 0.35);

    /// <summary>The rule above the status bar: x3270 draws its OIA line in the 3279 palette's blue. Declared last
    /// because it is built from the palette above it, and static fields initialise in order.</summary>
    public static readonly IBrush OiaRule = Brush(HostColor.Blue, false);

    /// <summary>The status bar's colours, taken from the same palette so the bar and the screen agree: text in
    /// neutral white, as x3270 draws its message area; operator errors, and the wire log marker, in red, as x3270
    /// paints operator errors; the padlock green when the certificate was verified and orange when not, with a mark
    /// that says the same so the colour never carries it alone.</summary>
    public static readonly IBrush OiaText = Brush(HostColor.NeutralWhite, false);
    public static readonly IBrush OiaError = Brush(HostColor.Red, false);
    public static readonly IBrush TlsVerified = Brush(HostColor.Green, false);
    public static readonly IBrush TlsUnverified = Brush(HostColor.Orange, false);

    /// <summary>About's link to the photo of Liz, in the palette's orange for her fur. It is underlined and led by a
    /// picture icon so the colour never carries it alone. <see cref="TlsUnverified"/> is the same orange; the two
    /// never share a window.</summary>
    public static readonly IBrush DedicationLink = Brush(HostColor.Orange, false);
}
