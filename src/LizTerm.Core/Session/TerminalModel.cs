// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>A 3270 model and the screen geometry it means. The geometry is what a user is actually choosing — a
/// model 5 is picked because it is 27x132, not because it is 5 — so it travels as data rather than as display
/// text, which is also what #30 (oversize geometry) will want.
///
/// b3270 reports this in its initialize block, but the profile editor opens from the picker with no engine
/// running, so the table is static and an integration test asserts it against a live engine.
/// Measured against b3270 4.5ga6 on 2026-09-09.</summary>
public sealed record TerminalModel(int Number, int Rows, int Columns)
{
    public static IReadOnlyList<TerminalModel> All { get; } =
    [
        new(2, 24, 80),
        new(3, 32, 80),
        new(4, 43, 80),
        new(5, 27, 132),
    ];

    public static TerminalModel? Find(int number) => All.FirstOrDefault(m => m.Number == number);

    /// <summary>What the editor's drop-down shows. A model seeded from a hand-edited profile has no geometry
    /// (see ProfileEditorViewModel), and "9 — 0x0" would be a lie, so an unknown one renders as the bare
    /// number.</summary>
    public override string ToString() => Rows == 0 || Columns == 0 ? Number.ToString() : $"{Number} — {Rows}x{Columns}";
}
