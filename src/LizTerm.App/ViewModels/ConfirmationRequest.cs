// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LizTerm.App.ViewModels;

public enum ConfirmChoice { Cancel, Primary, Secondary }

public sealed record ConfirmOutcome(ConfirmChoice Choice, bool ApplyToAll);

/// <summary>A question an inline confirmation strip is waiting on — Manage Tags' pattern, but awaited by the
/// operation that asked. The first answer wins. With <paramref name="input"/> the strip shows a text box (a
/// rename): prefilled, checked by <paramref name="inputRule"/> (null for an acceptable value), and the primary is
/// allowed only for an acceptable value that differs from the original, ignoring case and surrounding blanks. The
/// asker reads the answer from <see cref="Input"/>.</summary>
public sealed partial class ConfirmationRequest(string message, string primaryLabel, string? secondaryLabel = null,
    bool offersApplyToAll = false, string? input = null, Func<string, string?>? inputRule = null, string inputLabel = "")
    : ObservableObject
{
    private readonly TaskCompletionSource<ConfirmOutcome> _answer = new();
    private readonly string _original = (input ?? "").Trim();

    public string Message { get; } = message;
    public string PrimaryLabel { get; } = primaryLabel;
    public string? SecondaryLabel { get; } = secondaryLabel;
    public bool HasSecondary => SecondaryLabel is not null;
    public bool OffersApplyToAll { get; } = offersApplyToAll;
    public bool HasInput { get; } = input is not null;
    public string InputLabel { get; } = inputLabel;

    [ObservableProperty] private bool _applyToAll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputProblem), nameof(CanAnswerPrimary))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string _input = input ?? "";

    /// <summary>Why the input is not acceptable, in the asker's words, or null.</summary>
    public string? InputProblem => HasInput ? inputRule?.Invoke(Input) : null;

    /// <summary>A plain question can always be answered; an input question needs an acceptable, changed value.</summary>
    public bool CanAnswerPrimary =>
        !HasInput || (InputProblem is null && !string.Equals(Input.Trim(), _original, StringComparison.OrdinalIgnoreCase));

    public Task<ConfirmOutcome> Answer => _answer.Task;

    [RelayCommand(CanExecute = nameof(CanAnswerPrimary))]
    private void Primary()
    {
        // Enter in the text box reaches here through Execute, which does not consult CanExecute.
        if (CanAnswerPrimary) _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Primary, ApplyToAll));
    }

    [RelayCommand]
    private void Secondary() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Secondary, ApplyToAll));

    [RelayCommand]
    private void Cancel() => _answer.TrySetResult(new ConfirmOutcome(ConfirmChoice.Cancel, false));
}
