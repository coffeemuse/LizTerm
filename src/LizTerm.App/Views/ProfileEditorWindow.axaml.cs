// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class ProfileEditorWindow : Window
{
    private readonly ProfileEditorViewModel _vm;
    private Control[] _invalid = [];

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
        // Posted: a click on a suggestion takes focus from the box before the click is handled, and the box takes it
        // back afterwards. Committing at once would add the half-typed text instead of the suggestion.
        TagBox.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!TagBox.IsKeyboardFocusWithin && _vm.TagEntry.Trim().Length > 0) _vm.CommitTagEntry();
        });
        TagBox.GotFocus += (_, _) =>
        {
            if (_vm.CanAddTag && _vm.TagSuggestions.Count > 0) TagBox.IsDropDownOpen = true;
        };
        // A click on a suggestion is the choice. Read from the clicked row rather than SelectedItem: the drop-down
        // closes (focus has left the box) before the box records the selection, so on the first click SelectedItem
        // is still empty. The release bubbles out of the drop-down's popup to the box; posted so the box finishes
        // its own commit, which writes the suggestion's text into it, before this clears it.
        TagBox.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: TagChip chip })
                Dispatcher.UIThread.Post(() => AddSuggestion(chip));
        }, RoutingStrategies.Bubble, handledEventsToo: true);
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
        }
    }

    /// <summary>Adds the tag itself rather than through the box's text, then empties the box.</summary>
    private void AddSuggestion(TagChip chip)
    {
        _vm.AddTag(chip.Text);
        TagBox.IsDropDownOpen = false;
        TagBox.SelectedItem = null;
        TagBox.Text = "";
        _vm.TagEntry = "";
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
