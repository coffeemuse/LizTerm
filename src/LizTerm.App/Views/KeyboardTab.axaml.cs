// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;

namespace LizTerm.App.Views;

/// <summary>The Preferences window's Keyboard tab (editable keymap spec §5.3). Pure layout: the host sets the
/// data context, a <see cref="ViewModels.KeymapEditorViewModel"/>, and everything else is binding.</summary>
public partial class KeyboardTab : UserControl
{
    public KeyboardTab() => InitializeComponent();
}
