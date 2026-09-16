// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class ProfileEditorWindow : Window
{
    private readonly ProfileEditorViewModel _vm;
    private Control[] _invalid = [];

    /// <summary>Whether the tag box's drop-down was last closed by Escape, which must not add what was highlighted.</summary>
    private bool _suggestionsEscaped;

    /// <summary>Set while a suggestion is being added: clearing the box closes and reselects inside the drop-down,
    /// which would otherwise add the same suggestion again, without end.</summary>
    private bool _choosing;

    public ProfileEditorWindow() : this(null) { }

    /// <param name="tags">The tag colors to draw chips in and offer as suggestions.</param>
    public ProfileEditorWindow(SessionProfile? existing, TagRegistry? tags = null)
    {
        InitializeComponent();
        _vm = new ProfileEditorViewModel(existing, tags);
        DataContext = _vm;
        Title = existing is null ? "New Session Profile" : $"Edit {existing.Name}";
        _vm.PropertyChanged += OnViewModelChanged;

        // Tunnelled: AutoCompleteBox handles Enter and Backspace itself, and Enter must add a tag rather than reach
        // Save, which is the default button.
        TagBox.AddHandler(KeyDownEvent, OnTagBoxKeyDown, RoutingStrategies.Tunnel);
        TagBox.LostFocus += (_, _) =>
        {
            if (!TagBox.IsDropDownOpen && _vm.TagEntry.Trim().Length > 0) _vm.CommitTagEntry();
        };
        TagBox.GotFocus += (_, _) =>
        {
            if (_vm.CanAddTag && _vm.TagSuggestions.Count > 0) TagBox.IsDropDownOpen = true;
        };
        // A click on a suggestion puts its text in the box and closes the list; that choice is the tag.
        TagBox.DropDownClosed += (_, _) =>
        {
            if (_choosing) return;
            if (!_suggestionsEscaped && TagBox.SelectedItem is TagChip chip) AddSuggestion(chip);
            _suggestionsEscaped = false;
        };
        Opened += (_, _) => NameBox.Focus();
    }

    private void OnTagBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when TagBox.IsDropDownOpen && TagBox.SelectedItem is TagChip chip:
                AddSuggestion(chip);
                TagBox.IsDropDownOpen = false;
                e.Handled = true;
                break;
            // An empty box lets Enter through to Save, the default button.
            case Key.Enter when _vm.TagEntry.Trim().Length > 0:
                _vm.CommitTagEntry();
                TagBox.IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Back when string.IsNullOrEmpty(TagBox.Text) && e.KeyModifiers == KeyModifiers.None:
                _vm.RemoveLastTag();
                e.Handled = true;
                break;
            case Key.Escape when TagBox.IsDropDownOpen:
                _suggestionsEscaped = true;
                break;
        }
    }

    private void AddSuggestion(TagChip chip)
    {
        if (_choosing) return;
        _choosing = true;
        try
        {
            _vm.AddTag(chip.Text);
            TagBox.IsDropDownOpen = false;
            TagBox.SelectedItem = null;
            TagBox.Text = "";
            _vm.TagEntry = "";
        }
        finally
        {
            _choosing = false;
        }
    }

    /// <summary>A click anywhere in the drawn field, chips aside, types into the box.</summary>
    private void OnTagFieldPressed(object? sender, PointerPressedEventArgs e) => TagBox.Focus();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A comma typed in the box makes the view model rewrite TagEntry from inside the change the binding is
        // still writing, and a value changed there never reaches the box (the Wire Log correction's problem, App
        // CLAUDE.md). Put it back once that write has finished.
        if (e.PropertyName == nameof(ProfileEditorViewModel.TagEntry))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (TagBox.Text != _vm.TagEntry) TagBox.Text = _vm.TagEntry;
            });
            return;
        }
        if (e.PropertyName != nameof(ProfileEditorViewModel.ValidationField)) return;
        foreach (var control in _invalid) control.Classes.Remove("invalid");
        _invalid = _vm.ValidationField switch
        {
            ProfileEditorField.Name => [NameBox],
            ProfileEditorField.Host => [HostBox],
            ProfileEditorField.Port => [PortBox],
            ProfileEditorField.KeepAlive => [KeepAliveBox],
            ProfileEditorField.ScreenSize => [ColumnsBox, RowsBox],
            ProfileEditorField.CodePage => [CodePageBox],
            ProfileEditorField.Tags => [TagField],
            _ => [],
        };
        foreach (var control in _invalid) control.Classes.Add("invalid");
    }

    /// <summary>The tab holding a field.</summary>
    internal TabItem TabOf(ProfileEditorField field) => field switch
    {
        ProfileEditorField.ScreenSize or ProfileEditorField.CodePage => TerminalTab,
        ProfileEditorField.Tags => OrganizeTab,
        _ => ConnectionTab,
    };

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.TryBuild() is { } profile)
        {
            Close(new ProfileEdit(profile, _vm.PinCleared));
            return;
        }
        if (_vm.ValidationField is { } field) Tabs.SelectedItem = TabOf(field);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
