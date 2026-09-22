// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The one operation at a time the mvsMF Access window runs (USS spec §4.4): the busy flag and its
/// cancellation, the status line, the error banner with Retry, and the confirmation strip. Shared by the tabs' view
/// models, so an operation started on either holds the whole window and its result lands on the one status line.
/// It raises property changes for the window's bindings; the owners forward them or notify their commands.
/// Connection failures (<see cref="IsConnectionFailure"/>) are the red banner with Retry; everything else is the
/// status line. A closed window (<see cref="Dispose"/>) runs nothing more and answers every question Cancel.</summary>
public sealed partial class BrowserOperations : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cts;
    private Func<Task>? _retry;
    private string? _pinSaveWarning;
    private readonly object _idleLock = new();
    private TaskCompletionSource? _idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(CanRetry))]
    private string? _errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmation))]
    private ConfirmationRequest? _confirmation;

    public bool IsIdle => !IsBusy;
    public bool HasError => ErrorText is not null;
    public bool CanRetry => HasError && _retry is not null;
    public bool HasConfirmation => Confirmation is not null;
    public bool IsDisposed { get; private set; }

    partial void OnIsBusyChanged(bool value)
    {
        if (!value) SignalIdle();
    }

    /// <summary>Runs one operation with the busy flag, its own cancellation, and the failure rules in the class
    /// summary. A second operation while one runs is ignored; the commands are disabled anyway.
    /// <paramref name="describe"/> words the banner; the default is <see cref="HostFileMessages.Describe"/>.</summary>
    public async Task RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null,
        Func<Exception, string>? describe = null)
    {
        if (IsBusy || IsDisposed) return;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsBusy = true;
        ErrorText = null;
        _retry = null;
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusText = "– Cancelled.";
        }
        catch (HostFileException ex) when (IsConnectionFailure(ex))
        {
            _retry = retry;
            StatusText = "";
            ErrorText = (describe ?? HostFileMessages.Describe)(ex);
        }
        catch (Exception ex)
        {
            StatusText = "✗ " + HostFileMessages.Describe(ex);
        }
        finally
        {
            _cts = null;
            if (_pinSaveWarning is { } warning)
            {
                _pinSaveWarning = null;
                StatusText = "⚠ " + warning;
            }
            IsBusy = false;
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    /// <summary>A closed window has no one to ask, so the answer is Cancel.</summary>
    public async Task<ConfirmOutcome> AskAsync(ConfirmationRequest request)
    {
        if (IsDisposed) return new ConfirmOutcome(ConfirmChoice.Cancel, false);
        Confirmation = request;
        try
        {
            return await request.Answer;
        }
        finally
        {
            Confirmation = null;
        }
    }

    /// <summary>Drops a pending Retry with its banner: the operation it belongs to has been closed away from.</summary>
    public void DropRetry()
    {
        _retry = null;
        ErrorText = null;
        OnPropertyChanged(nameof(CanRetry));
    }

    /// <summary>A warning for the status line: at once when nothing runs, else once the running operation ends,
    /// so it replaces that operation's own line rather than being overwritten by it (the pin save).</summary>
    public void WarnWhenIdle(string message)
    {
        if (IsBusy) _pinSaveWarning = message;
        else StatusText = "⚠ " + message;
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        var retry = _retry;
        _retry = null;
        ErrorText = null;
        if (retry is not null) await retry();
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        StatusText = "⟳ Cancelling…";
        _cts?.Cancel();
        Confirmation?.CancelCommand.Execute(null);
    }

    /// <summary>Null while nothing runs; otherwise a task that completes when the running operation ends, so a
    /// waiting filter never polls.</summary>
    internal Task? WhenIdle()
    {
        lock (_idleLock)
        {
            if (!IsBusy) return null;
            return (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private void SignalIdle()
    {
        lock (_idleLock)
        {
            _idle?.TrySetResult();
            _idle = null;
        }
    }

    /// <summary>The failures that stop a whole operation and earn the banner: the host cannot be reached, the
    /// sign-in failed, the certificate was refused, or the host is not one this release supports.</summary>
    public static bool IsConnectionFailure(HostFileException ex) =>
        ex.Kind is HostFileErrorKind.Unreachable or HostFileErrorKind.Unauthenticated or HostFileErrorKind.CertificateRejected
            or HostFileErrorKind.Unsupported;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _cts?.Cancel();
        Confirmation?.CancelCommand.Execute(null);
    }
}
