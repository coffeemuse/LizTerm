using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Files;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One File Transfer dialog: a form that becomes a progress view and then a result view, with the form
/// kept filled behind them. Owns the transfer call; <see cref="SessionViewModel"/> only remembers the last request.
/// Numeric fields are text: blank means unset, so the form can carry values the current direction or host type
/// does not use.</summary>
public partial class FileTransferViewModel : ObservableObject
{
    private readonly IEmulatorSession _session;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;

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
    [NotifyPropertyChangedFor(nameof(IsTso), nameof(RecordFormats), nameof(CanSetBlksize), nameof(CanSetSpace), nameof(CanSetAverageBlock))]
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
    [NotifyPropertyChangedFor(nameof(HasRecordFormat), nameof(CanSetBlksize))]
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
    /// <summary>b3270 emits LRECL and BLKSIZE only with a RECFM, and BLKSIZE and SPACE only for TSO.</summary>
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
        if (path is not null) LocalPath = path;
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
}
