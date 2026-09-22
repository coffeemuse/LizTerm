// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Startup;

namespace LizTerm.App.Views;

/// <summary>Shown once at launch when keymap.json would not read at all (#168), over whatever the startup plan
/// opened, so someone who never opens a session still hears that their bindings are not in force. It tells and
/// nothing more; KeymapNotice says why.</summary>
public partial class KeymapNoticeWindow : Window
{
    /// <summary>Design-time only.</summary>
    public KeymapNoticeWindow() : this(new KeymapNotice("keymap.json could not be read.", "/config/keymap.json")) { }

    public KeymapNoticeWindow(KeymapNotice notice)
    {
        InitializeComponent();
        Title = KeymapNotice.Title;
        MessageText.Text = notice.Message;
        PathText.Text = notice.Path;
        PointerText.Text = KeymapNotice.Pointer;
        DismissButton.Content = KeymapNotice.DismissLabel;
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e) => Close();
}
