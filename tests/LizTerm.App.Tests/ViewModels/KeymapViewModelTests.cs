// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Editable keymap spec §5.1: applies in memory first, raises Changed, then writes through.</summary>
public class KeymapViewModelTests : IDisposable
{
    private static readonly KeyChord CtrlHome = new(Key.Home, KeyModifiers.Control);
    private static readonly KeymapAction Pa1 = new KeymapAction.SendKey(TerminalKey.PA1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "keymap.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Bind_raises_Changed_and_the_composed_map_follows()
    {
        var vm = new KeymapViewModel();
        var changes = 0;
        vm.Changed += (_, _) => changes++;

        vm.Bind(CtrlHome, Pa1);

        Assert.Equal(1, changes);
        Assert.True(vm.Compose(destructiveBackspace: true).TryMap(CtrlHome, out var key));
        Assert.Equal(TerminalKey.PA1, key);
    }

    [Fact]
    public void Row_queries_answer_from_the_composed_map()
    {
        var vm = new KeymapViewModel();

        vm.Bind(CtrlHome, Pa1);

        Assert.Equal(Pa1, vm.ActionOf(CtrlHome));
        Assert.Equal(new KeymapAction.TypeText("¬"), vm.ActionOf(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control)));
        Assert.Null(vm.ActionOf(new KeyChord(Key.X, KeyModifiers.Control)));
        Assert.Equal([new KeyChord(Key.D2, KeyModifiers.Alt)], vm.ChordsFor(new KeymapAction.SendKey(TerminalKey.PA2)));
        Assert.Contains(CtrlHome, vm.ChordsFor(Pa1));
        Assert.Empty(vm.ChordsFor(KeymapAction.Unbound.Instance));
    }

    [Fact]
    public void A_store_is_written_through_and_read_at_construction()
    {
        new KeymapViewModel(new KeymapStore(FilePath)).Bind(CtrlHome, Pa1);

        var reopened = new KeymapViewModel(new KeymapStore(FilePath));

        Assert.Equal(Pa1, reopened.Overlay.Entries[CtrlHome]);
        Assert.Null(reopened.LastSaveError);
    }

    [Fact]
    public void Unbind_and_ResetToDefaults_write_through_too()
    {
        var vm = new KeymapViewModel(new KeymapStore(FilePath));
        vm.Unbind(CtrlHome);
        Assert.Contains("\"Ctrl+Home\": null", File.ReadAllText(FilePath));

        vm.ResetToDefaults();

        Assert.Empty(new KeymapStore(FilePath).Load().Bindings);
        Assert.True(vm.Compose(destructiveBackspace: true).TryMap(CtrlHome, out _));
    }

    [Fact]
    public void A_failed_save_keeps_the_change_in_memory_and_reports_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var vm = new KeymapViewModel(new KeymapStore(FilePath));
        string? reported = null;
        vm.SaveFailed += (_, message) => reported = message;

        vm.Bind(CtrlHome, Pa1);

        Assert.Equal(Pa1, vm.Overlay.Entries[CtrlHome]);
        Assert.NotNull(vm.LastSaveError);
        Assert.Equal(vm.LastSaveError, reported);
        Assert.Contains("keymap.json", reported);
        Assert.Equal("not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Unreadable_entries_are_counted_and_notified()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Cmd+K": "PA1", "F2": 7}}""");
        var vm = new KeymapViewModel(new KeymapStore(FilePath));
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        Assert.Equal(2, vm.UnreadableEntries);
        vm.ResetToDefaults();

        Assert.Equal(0, vm.UnreadableEntries);
        Assert.Contains(nameof(KeymapViewModel.UnreadableEntries), notified);
    }

    [Fact]
    public void A_throwing_Changed_subscriber_does_not_cost_the_save()
    {
        var vm = new KeymapViewModel(new KeymapStore(FilePath));
        vm.Changed += (_, _) => throw new InvalidOperationException("subscriber");

        Assert.Throws<InvalidOperationException>(() => vm.Bind(CtrlHome, Pa1));

        Assert.Equal(new KeymapEntry.SendKey("PA1"), new KeymapStore(FilePath).Load().Bindings["Ctrl+Home"]);
    }
}
