using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Files;
using LizTerm.Core.Session;
using System.Globalization;

namespace LizTerm.App.ViewModels;

public enum TransferPhase { Form, Running, Done }

/// <summary>One File Transfer dialog: a form that becomes a progress view and then a result view, with the form
/// kept filled behind them. Owns the transfer call; <see cref="SessionViewModel"/> only remembers the last request.
/// Numeric fields are text: blank means unset, so the form can carry values the current direction or host type
/// does not use.</summary>
public partial class FileTransferViewModel : ObservableObject
{
    private readonly IEmulatorSession _session;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;
    private CancellationTokenSource? _cts;
    /// <summary>The path the OS Save dialog last returned. That dialog asks before overwriting, so it is the one
    /// source of consent to replace an existing local file; a typed or remembered path has none.</summary>
    private string? _replaceConsentPath;

    /// <summary>Raised with the request each time Start passes validation, before the transfer begins.</summary>
    public event Action<FileTransferRequest>? Started;

    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    /// <param name="initial">The last request started from this session window, or null for the defaults.</param>
    public FileTransferViewModel(IEmulatorSession session, IFilePicker picker, Action<Action> dispatch, FileTransferRequest? initial)
    {
        _session = session;
        _picker = picker;
        _dispatch = dispatch;
        if (initial is null) return;
        _isSend = initial.Direction == TransferDirection.Send;
        _localPath = initial.LocalPath;
        _hostFile = initial.HostFile;
        _hostType = initial.HostType;
        _isText = initial.Mode == TransferMode.Text;
        _crLf = initial.CrLf;
        _remap = initial.Remap;
        _append = initial.Append;
        _recordFormat = initial.RecordFormat;
        _lreclText = Text(initial.Lrecl);
        _blksizeText = Text(initial.Blksize);
        _allocationUnits = initial.AllocationUnits;
        _primarySpaceText = Text(initial.PrimarySpace);
        _secondarySpaceText = Text(initial.SecondarySpace);
        _averageBlockText = Text(initial.AverageBlock);
        _bufferSizeText = Text(initial.BufferSize);
        _extraOptions = initial.ExtraOptions ?? "";
    }

    private static string Text(int? value) => value?.ToString() ?? "";

    // ---- form ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReceive), nameof(ShowAdvanced))]
    private bool _isSend = true;

    /// <summary>The other half of the direction radio pair; settable so both buttons can bind two-way.</summary>
    public bool IsReceive
    {
        get => !IsSend;
        set => IsSend = !value;
    }

    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private string _hostFile = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTso), nameof(RecordFormats), nameof(CanSetRecordFormat), nameof(CanSetLrecl), nameof(CanSetBlksize), nameof(CanSetSpace), nameof(CanSetAverageBlock))]
    private TransferHostType _hostType = TransferHostType.Tso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBinary))]
    private bool _isText = true;

    /// <summary>The other half of the mode radio pair.</summary>
    public bool IsBinary
    {
        get => !IsText;
        set => IsText = !value;
    }

    [ObservableProperty] private bool _crLf = true;
    [ObservableProperty] private bool _remap = true;
    [ObservableProperty] private bool _append;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecordFormat), nameof(CanSetLrecl), nameof(CanSetBlksize))]
    private RecordFormat _recordFormat = RecordFormat.Default;

    [ObservableProperty] private string _lreclText = "";
    [ObservableProperty] private string _blksizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAllocation), nameof(IsAvBlock), nameof(CanSetSpace), nameof(CanSetAverageBlock))]
    private AllocationUnits _allocationUnits = AllocationUnits.Default;

    [ObservableProperty] private string _primarySpaceText = "";
    [ObservableProperty] private string _secondarySpaceText = "";
    [ObservableProperty] private string _averageBlockText = "";
    [ObservableProperty] private string _bufferSizeText = "";
    [ObservableProperty] private string _extraOptions = "";
    [ObservableProperty] private string? _validationMessage;

    public bool IsTso => HostType == TransferHostType.Tso;
    /// <summary>The Advanced expander describes the host file a send creates, so it hides on receive.</summary>
    public bool ShowAdvanced => IsSend;
    public bool HasRecordFormat => RecordFormat != RecordFormat.Default;
    public bool HasAllocation => AllocationUnits != AllocationUnits.Default;
    public bool IsAvBlock => AllocationUnits == AllocationUnits.AvBlock;
    /// <summary>Mirrors what <c>TransferMapper</c> puts on the wire, so the form never accepts a value it would
    /// drop: RECFM goes to TSO and VM only, LRECL and BLKSIZE only with a RECFM, and BLKSIZE and SPACE only to
    /// TSO. A value entered for one host type stays in the (disabled) control when another is chosen.</summary>
    public bool CanSetRecordFormat => HostType != TransferHostType.Cics;
    public bool CanSetLrecl => HasRecordFormat && CanSetRecordFormat;
    public bool CanSetBlksize => HasRecordFormat && IsTso;
    public bool CanSetSpace => HasAllocation && IsTso;
    public bool CanSetAverageBlock => IsAvBlock && IsTso;

    public TransferHostType[] HostTypes { get; } = [TransferHostType.Tso, TransferHostType.Vm, TransferHostType.Cics];
    /// <summary>VM has no undefined-length records, so the list shrinks for it.</summary>
    public RecordFormat[] RecordFormats => HostType == TransferHostType.Vm
        ? [RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable]
        : [RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable, RecordFormat.Undefined];
    public AllocationUnits[] AllocationUnitsList { get; } = [AllocationUnits.Default, AllocationUnits.Tracks, AllocationUnits.Cylinders, AllocationUnits.AvBlock];

    partial void OnHostTypeChanged(TransferHostType value)
    {
        if (value == TransferHostType.Vm && RecordFormat == RecordFormat.Undefined) RecordFormat = RecordFormat.Default;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        ValidationMessage = null;
        string? path;
        try
        {
            path = IsSend
                ? await _picker.PickFileToSendAsync()
                : await _picker.PickSaveLocationAsync(LocalFileNames.Suggest(HostFile, HostType));
        }
        catch (Exception ex)
        {
            ValidationMessage = "Could not open the file dialog: " + ex.Message;
            return;
        }
        if (path is null) return;
        LocalPath = path;
        if (!IsSend) _replaceConsentPath = path;
    }

    /// <summary>A receive replaces the local file (the request carries exist=replace), so unless the transfer
    /// appends, an existing file needs the consent the Save dialog collects. Null when the request may go ahead.</summary>
    private string? CheckOverwriteConsent(FileTransferRequest request)
    {
        if (request.Direction != TransferDirection.Receive || request.Append) return null;
        if (string.Equals(request.LocalPath, _replaceConsentPath, StringComparison.Ordinal)) return null;
        if (!File.Exists(request.LocalPath)) return null;
        return Path.GetFileName(request.LocalPath) + " already exists. Choose it with Browse... to replace it, or turn on Append.";
    }

    /// <summary>The request the form describes, or null with <see cref="ValidationMessage"/> set. Non-integer text
    /// is reported before the Core rules run.</summary>
    public FileTransferRequest? TryBuildRequest()
    {
        if (!TryNumber(LreclText, "LRECL", out var lrecl)
            || !TryNumber(BlksizeText, "BLKSIZE", out var blksize)
            || !TryNumber(PrimarySpaceText, "Primary space", out var primary)
            || !TryNumber(SecondarySpaceText, "Secondary space", out var secondary)
            || !TryNumber(AverageBlockText, "Average block size", out var average)
            || !TryNumber(BufferSizeText, "Buffer size", out var buffer))
            return null;

        var request = new FileTransferRequest
        {
            Direction = IsSend ? TransferDirection.Send : TransferDirection.Receive,
            LocalPath = LocalPath.Trim(),
            HostFile = HostFile.Trim(),
            HostType = HostType,
            Mode = IsText ? TransferMode.Text : TransferMode.Binary,
            CrLf = CrLf,
            Remap = Remap,
            Append = Append,
            RecordFormat = RecordFormat,
            Lrecl = lrecl,
            Blksize = blksize,
            AllocationUnits = AllocationUnits,
            PrimarySpace = primary,
            SecondarySpace = secondary,
            AverageBlock = average,
            BufferSize = buffer,
            ExtraOptions = string.IsNullOrWhiteSpace(ExtraOptions) ? null : ExtraOptions.Trim(),
        };
        ValidationMessage = request.Validate();
        return ValidationMessage is null ? request : null;
    }

    private bool TryNumber(string text, string field, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), out var number))
        {
            value = number;
            return true;
        }
        ValidationMessage = field + " must be a whole number.";
        return false;
    }

    // ---- phase ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForm), nameof(IsRunning), nameof(IsDone))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CancelTransferCommand), nameof(BackCommand))]
    private TransferPhase _phase = TransferPhase.Form;

    public bool IsForm => Phase == TransferPhase.Form;
    public bool IsRunning => Phase == TransferPhase.Running;
    public bool IsDone => Phase == TransferPhase.Done;

    // ---- running ----

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    private long _bytesTransferred;

    /// <summary>The local file's length when sending; null when receiving or when it cannot be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate), nameof(ProgressMaximum))]
    private long? _totalBytes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelTransferCommand))]
    private bool _isCancelling;

    public bool IsProgressIndeterminate => TotalBytes is null;
    public double ProgressValue => BytesTransferred;
    public double ProgressMaximum => TotalBytes ?? 1;

    // ---- done ----

    [ObservableProperty] private string _resultMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Failed))]
    private bool _succeeded;

    public bool Failed => !Succeeded;

    [RelayCommand(CanExecute = nameof(IsForm))]
    private async Task StartAsync()
    {
        if (!IsForm) return;
        if (TryBuildRequest() is not { } request) return;
        if (CheckOverwriteConsent(request) is { } refusal)
        {
            ValidationMessage = refusal;
            return;
        }
        Started?.Invoke(request);

        TotalBytes = null;
        BytesTransferred = 0;
        StatusText = "Waiting for the host...";
        IsCancelling = false;
        Phase = TransferPhase.Running;

        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var transfer = _session.TransferAsync(request, new DispatchedProgress(this), cts.Token);
            // The bar stays indeterminate until the length is known. The stat runs after the request is away and
            // off this thread, because a path on a sleeping network volume can block it for the mount timeout.
            if (request.Direction == TransferDirection.Send)
                TotalBytes = await Task.Run(() => TryFileLength(request.LocalPath));
            var result = await transfer;
            if (result.Succeeded)
                Finish(true, result.Message);
            else if (string.IsNullOrWhiteSpace(result.Message))
                Finish(false, "Transfer failed with no message from the host.");
            else
                Finish(false, result.Message);
        }
        catch (OperationCanceledException)
        {
            // Only a token cancelled before the call: a cancel of a running transfer comes back as a failed
            // result carrying the engine's own text.
            Finish(false, "Transfer cancelled.");
        }
        catch (Exception ex)
        {
            Finish(false, ex.Message);
        }
        finally
        {
            _cts = null;
            cts.Dispose();
        }
    }

    private void Finish(bool succeeded, string message)
    {
        Succeeded = succeeded;
        ResultMessage = message;
        Phase = TransferPhase.Done;
    }

    private static long? TryFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool CanCancel => IsRunning && !IsCancelling;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelTransfer()
    {
        if (!CanCancel) return;
        IsCancelling = true;
        StatusText = "Cancelling...";
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(IsDone))]
    private void Back()
    {
        if (IsDone) Phase = TransferPhase.Form;
    }

    /// <summary>The window's Closing policy. A running transfer is cancelled and the window kept, so the outcome
    /// is seen; a second close while the engine has still not answered lets the window go, because a host that
    /// never answers must not pin the dialog, the session window, and Quit behind it. The cancel is already on
    /// its way and the backend frees its transfer slot when the run finally ends.</summary>
    public bool TryClose()
    {
        if (!IsRunning || IsCancelling) return true;
        CancelTransfer();
        return false;
    }

    private void OnProgress(long bytes)
    {
        if (!IsRunning) return;
        BytesTransferred = bytes;
        if (!IsCancelling) StatusText = bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes";
    }

    /// <summary>The backend reports on its reader thread; every report goes through the dispatch delegate.</summary>
    private sealed class DispatchedProgress(FileTransferViewModel owner) : IProgress<long>
    {
        public void Report(long value) => owner._dispatch(() => owner.OnProgress(value));
    }
}
