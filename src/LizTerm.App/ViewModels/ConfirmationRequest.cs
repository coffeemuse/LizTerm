// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LizTerm.App.ViewModels;

public enum ConfirmChoice { Cancel, Primary, Secondary }

public sealed record ConfirmOutcome(ConfirmChoice Choice, bool ApplyToAll);

/// <summary>A question an inline confirmation strip is waiting on — Manage Tags' pattern, but awaited by the
/// operation that asked. The first answer wins.</summary>
public sealed partial class ConfirmationRequest(string message, string primaryLabel, string? secondaryLabel = null, bool offersApplyToAll = false)
    : ObservableObject
{
    private readonly TaskCompletionSource<ConfirmOutcome> _answer = new();

    public string Message { get; } = message;
    public string PrimaryLabel { get; } = primaryLabel;
    public string? SecondaryLabel { get; } = secondaryLabel;
    public bool HasSecondary => SecondaryLabel is not null;
    public bool OffersApplyToAll { get; } = offersApplyToAll;

    [ObservableProperty] private bool _applyToAll;

    public Task<ConfirmOutcome> Answer => _answer.Task;

    [RelayCommand]
    private void Primary() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Primary, ApplyToAll));

    [RelayCommand]
    private void Secondary() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Secondary, ApplyToAll));

    [RelayCommand]
    private void Cancel() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Cancel, false));
}
