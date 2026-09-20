// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>New dataset (spec §4.4): the form is <see cref="Views.NewDatasetWindow"/>, an owned modal dialog over
/// the browser window, bound to this view model, sent as an explicit allocation. The form is one instance for the
/// life of the window, so the space it was last sent with is kept.</summary>
public sealed partial class MvsmfBrowserViewModel
{
    public NewDatasetFormViewModel Form { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseDataset))]
    private bool _isCreating;

    private bool CanNewDataset => !IsBusy && !IsReviewingUpload && !IsCreating;
    private bool CanCreate => !IsBusy && IsCreating && Form.CanCreate;
    private bool CanCloseForm => !IsBusy && IsCreating;

    partial void OnIsCreatingChanged(bool value) => NotifyCommands();

    /// <summary>Called once, by the constructor: the Create button follows the form's own rules.</summary>
    private void WatchForm() => Form.PropertyChanged += OnFormPropertyChanged;

    private void OnFormPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewDatasetFormViewModel.CanCreate)) CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Opens the form: type and DCB from the chosen dataset, the name from the filter's first qualifier, the
    /// space as it was last sent.</summary>
    [RelayCommand(CanExecute = nameof(CanNewDataset))]
    private void NewDataset()
    {
        // A banner's Retry belongs to the operation that failed, and nothing else runs while the form is open.
        DropRetry();
        Form.Message = null;
        Form.Name = FirstQualifier(Filter) is { } hlq ? hlq + "." : "";
        if (SelectedDataset is { } dataset) Form.PrefillFrom(dataset.Attributes);
        IsCreating = true;
    }

    /// <summary>The filter's first qualifier when it is a plain one (<c>MVSCE02</c> of <c>MVSCE02.**</c>), else null.</summary>
    private static string? FirstQualifier(string filter)
    {
        var first = filter.Trim().ToUpperInvariant().Split('.')[0];
        return first.Length > 0 && !first.Contains('*') && !first.Contains('%') ? first : null;
    }

    /// <summary>A Retry left by a failed create belongs to this form, so it goes with it.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseForm))]
    private void CloseForm()
    {
        DropRetry();
        HideForm();
    }

    private void HideForm()
    {
        IsCreating = false;
        Form.Message = null;
    }

    /// <summary>A retry sends the form again, as it now reads; once the host has created the dataset, only the
    /// listing is retried.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateAsync() => RunThenListAsync(CreateCoreAsync, () => CreateAsync());

    private async Task CreateCoreAsync(Action<Func<Task>> retryWith, CancellationToken token)
    {
        if (!IsCreating) return;
        if (!Form.CanCreate)
        {
            // A Retry after the form was edited into an invalid state.
            StatusText = "✗ Fix the fields marked ✗ first.";
            return;
        }
        var path = HostPath.ForDataset(Form.Name);
        var allocation = Form.Allocation;
        Form.Message = null;
        StatusText = $"⟳ Creating {path}…";
        try
        {
            await _connection.RunAsync(service => service.CreateDatasetAsync(path, allocation, token));
        }
        catch (HostFileException ex) when (ex.Kind is HostFileErrorKind.CannotAllocate or HostFileErrorKind.InvalidRequest)
        {
            // The values can be corrected and sent again, so the answer stays with them. The host cannot say which
            // value it disliked (compatibility log, create-failure-is-one-500), and the message says as much.
            Form.Message = "✗ " + HostFileMessages.Describe(ex);
            StatusText = "";
            return;
        }
        var what = $"Created {path}";
        retryWith(() => ListAgainAsync(path.Dataset, what));
        HideForm();
        await ShowAfterChangeAsync(path.Dataset, what, token);
    }
}
