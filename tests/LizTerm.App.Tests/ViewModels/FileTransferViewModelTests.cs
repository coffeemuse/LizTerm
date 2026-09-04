using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class FileTransferViewModelTests
{
    private static (FileTransferViewModel Vm, FakeEmulatorSession Session, FakeFilePicker Picker) Create(FileTransferRequest? initial = null)
    {
        var session = new FakeEmulatorSession();
        var picker = new FakeFilePicker();
        return (new FileTransferViewModel(session, picker, action => action(), initial), session, picker);
    }

    [Fact]
    public void Defaults_are_send_tso_text_with_crlf_and_remap()
    {
        var (vm, _, _) = Create();
        Assert.True(vm.IsSend);
        Assert.False(vm.IsReceive);
        Assert.Equal(TransferHostType.Tso, vm.HostType);
        Assert.True(vm.IsTso);
        Assert.True(vm.IsText);
        Assert.False(vm.IsBinary);
        Assert.True(vm.CrLf);
        Assert.True(vm.Remap);
        Assert.False(vm.Append);
        Assert.Equal(RecordFormat.Default, vm.RecordFormat);
        Assert.False(vm.HasRecordFormat);
        Assert.Equal(AllocationUnits.Default, vm.AllocationUnits);
        Assert.False(vm.HasAllocation);
        Assert.False(vm.IsAvBlock);
        Assert.True(vm.ShowAdvanced);
        Assert.Equal("", vm.LocalPath);
        Assert.Equal("", vm.HostFile);
        Assert.Equal("", vm.LreclText);
        Assert.Equal("", vm.ExtraOptions);
        Assert.Null(vm.ValidationMessage);
        Assert.Equal([TransferHostType.Tso, TransferHostType.Vm, TransferHostType.Cics], vm.HostTypes);
        Assert.Equal([RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable, RecordFormat.Undefined], vm.RecordFormats);
        Assert.Equal([AllocationUnits.Default, AllocationUnits.Tracks, AllocationUnits.Cylinders, AllocationUnits.AvBlock], vm.AllocationUnitsList);
    }

    [Fact]
    public void An_initial_request_fills_every_field_and_round_trips()
    {
        var initial = new FileTransferRequest
        {
            Direction = TransferDirection.Receive, LocalPath = "/tmp/x", HostFile = "A.B", HostType = TransferHostType.Vm,
            Mode = TransferMode.Binary, CrLf = false, Remap = false, Append = true, RecordFormat = RecordFormat.Variable,
            Lrecl = 255, Blksize = 3120, AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5, SecondarySpace = 1,
            AverageBlock = 4096, BufferSize = 8192, ExtraOptions = "NOTRUNC",
        };
        var (vm, _, _) = Create(initial);
        Assert.False(vm.IsSend);
        Assert.True(vm.IsReceive);
        Assert.Equal("/tmp/x", vm.LocalPath);
        Assert.Equal("A.B", vm.HostFile);
        Assert.Equal(TransferHostType.Vm, vm.HostType);
        Assert.False(vm.IsText);
        Assert.False(vm.CrLf);
        Assert.False(vm.Remap);
        Assert.True(vm.Append);
        Assert.Equal(RecordFormat.Variable, vm.RecordFormat);
        Assert.Equal("255", vm.LreclText);
        Assert.Equal("3120", vm.BlksizeText);
        Assert.Equal(AllocationUnits.AvBlock, vm.AllocationUnits);
        Assert.Equal("5", vm.PrimarySpaceText);
        Assert.Equal("1", vm.SecondarySpaceText);
        Assert.Equal("4096", vm.AverageBlockText);
        Assert.Equal("8192", vm.BufferSizeText);
        Assert.Equal("NOTRUNC", vm.ExtraOptions);
        Assert.Equal(initial, vm.TryBuildRequest());
    }

    [Fact]
    public void Derived_flags_follow_the_form()
    {
        var (vm, _, _) = Create();
        vm.IsReceive = true;
        Assert.False(vm.IsSend);
        Assert.False(vm.ShowAdvanced);
        vm.IsSend = true;
        Assert.True(vm.ShowAdvanced);

        vm.IsBinary = true;
        Assert.False(vm.IsText);
        vm.IsText = true;
        Assert.False(vm.IsBinary);

        vm.RecordFormat = RecordFormat.Fixed;
        Assert.True(vm.HasRecordFormat);
        Assert.True(vm.CanSetBlksize);
        vm.AllocationUnits = AllocationUnits.AvBlock;
        Assert.True(vm.HasAllocation);
        Assert.True(vm.IsAvBlock);
        Assert.True(vm.CanSetSpace);
        Assert.True(vm.CanSetAverageBlock);

        vm.HostType = TransferHostType.Vm;
        Assert.False(vm.IsTso);
        Assert.False(vm.CanSetBlksize);
        Assert.False(vm.CanSetSpace);
        Assert.False(vm.CanSetAverageBlock);
        Assert.Equal([RecordFormat.Default, RecordFormat.Fixed, RecordFormat.Variable], vm.RecordFormats);
    }

    [Fact]
    public void Switching_to_vm_drops_an_undefined_record_format()
    {
        var (vm, _, _) = Create();
        vm.RecordFormat = RecordFormat.Undefined;
        vm.HostType = TransferHostType.Vm;
        Assert.Equal(RecordFormat.Default, vm.RecordFormat);
        vm.HostType = TransferHostType.Cics;
        vm.RecordFormat = RecordFormat.Undefined;
        Assert.Equal(RecordFormat.Undefined, vm.RecordFormat);
    }

    [Fact]
    public async Task Browse_opens_for_send_and_saves_with_a_suggested_name_for_receive()
    {
        var (vm, _, picker) = Create();
        picker.Result = "/tmp/job.jcl";
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(["open"], picker.Calls);
        Assert.Equal("/tmp/job.jcl", vm.LocalPath);

        vm.IsReceive = true;
        vm.HostFile = "'MVSCE02.LIZTERM.JCL(JOB1)'";
        picker.Result = null;
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(["open", "save:JOB1"], picker.Calls);
        Assert.Equal("/tmp/job.jcl", vm.LocalPath);
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task Browse_failure_shows_a_message()
    {
        var (vm, _, picker) = Create();
        picker.Exception = new InvalidOperationException("no display");
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal("Could not open the file dialog: no display", vm.ValidationMessage);
    }

    [Fact]
    public void Validation_reports_blank_fields_then_bad_numbers_then_core_rules()
    {
        var (vm, _, _) = Create();
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Choose a local file.", vm.ValidationMessage);

        vm.LocalPath = "/tmp/a";
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Enter the host file name.", vm.ValidationMessage);

        vm.HostFile = "A.B";
        vm.LreclText = "eighty";
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("LRECL must be a whole number.", vm.ValidationMessage);

        vm.LreclText = "80";
        vm.BufferSizeText = "1.5";
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Buffer size must be a whole number.", vm.ValidationMessage);

        vm.BufferSizeText = "";
        vm.AllocationUnits = AllocationUnits.Tracks;
        Assert.Null(vm.TryBuildRequest());
        Assert.Equal("Primary space is required when allocation units are set.", vm.ValidationMessage);

        vm.PrimarySpaceText = " 5 ";
        vm.ExtraOptions = "  ";
        var request = vm.TryBuildRequest();
        Assert.NotNull(request);
        Assert.Null(vm.ValidationMessage);
        Assert.Equal(5, request!.PrimarySpace);
        Assert.Equal(80, request.Lrecl);
        Assert.Null(request.Blksize);
        Assert.Null(request.BufferSize);
        Assert.Null(request.ExtraOptions);
        Assert.Equal(TransferDirection.Send, request.Direction);
    }

    [Fact]
    public void Names_are_trimmed_into_the_request()
    {
        var (vm, _, _) = Create();
        vm.LocalPath = " /tmp/a ";
        vm.HostFile = " A.B ";
        var request = vm.TryBuildRequest();
        Assert.Equal("/tmp/a", request!.LocalPath);
        Assert.Equal("A.B", request.HostFile);
    }
}
