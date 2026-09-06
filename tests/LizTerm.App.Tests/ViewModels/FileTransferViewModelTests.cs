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
    public void Record_format_and_lrecl_are_only_settable_for_tso_and_vm()
    {
        var (vm, _, _) = Create();
        vm.RecordFormat = RecordFormat.Fixed;
        Assert.True(vm.CanSetRecordFormat);
        Assert.True(vm.CanSetLrecl);

        vm.HostType = TransferHostType.Vm;
        Assert.True(vm.CanSetRecordFormat);
        Assert.True(vm.CanSetLrecl);

        // The mapper sends neither keyword to CICS, so the form must not offer them.
        vm.HostType = TransferHostType.Cics;
        Assert.False(vm.CanSetRecordFormat);
        Assert.False(vm.CanSetLrecl);
        Assert.False(vm.CanSetBlksize);

        vm.HostType = TransferHostType.Tso;
        vm.RecordFormat = RecordFormat.Default;
        Assert.True(vm.CanSetRecordFormat);
        Assert.False(vm.CanSetLrecl);
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
        vm.ValidationMessage = "stale";
        picker.Result = null;
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Null(vm.ValidationMessage);
        Assert.Equal(["open", "save:JOB1"], picker.Calls);
        Assert.Equal("/tmp/job.jcl", vm.LocalPath);
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

    private static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Start_records_the_request_runs_shows_progress_and_lands_in_done()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        FileTransferRequest? started = null;
        vm.Started += r => started = r;
        session.TransferCompletion = Pending();

        var run = vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(TransferPhase.Running, vm.Phase);
        Assert.True(vm.IsRunning);
        Assert.False(vm.IsForm);
        Assert.Equal("Waiting for the host...", vm.StatusText);
        Assert.Equal("A.B", started?.HostFile);
        Assert.Equal(["transfer:Send:A.B"], session.Calls);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.True(vm.CancelTransferCommand.CanExecute(null));
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.True(vm.IsProgressIndeterminate);

        session.TransferProgress!.Report(2048);
        Assert.Equal("2,048 bytes", vm.StatusText);
        Assert.Equal(2048, vm.BytesTransferred);
        Assert.Equal(2048, vm.ProgressValue);

        session.TransferResult = new FileTransferResult(true, "Transfer complete, 2048 bytes transferred");
        session.TransferCompletion.SetResult();
        await run;

        Assert.Equal(TransferPhase.Done, vm.Phase);
        Assert.True(vm.IsDone);
        Assert.True(vm.Succeeded);
        Assert.False(vm.Failed);
        Assert.Equal("Transfer complete, 2048 bytes transferred", vm.ResultMessage);
        Assert.True(vm.BackCommand.CanExecute(null));
        Assert.False(vm.CancelTransferCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sending_a_real_file_shows_a_determinate_bar()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "hello world", TestContext.Current.CancellationToken);
        try
        {
            var (vm, session, _) = Create();
            vm.LocalPath = path;
            vm.HostFile = "A.B";
            session.TransferCompletion = Pending();
            var run = vm.StartCommand.ExecuteAsync(null);
            await Wait.UntilAsync(() => vm.TotalBytes is not null, "the file length");
            Assert.Equal(11, vm.TotalBytes);
            Assert.False(vm.IsProgressIndeterminate);
            Assert.Equal(11, vm.ProgressMaximum);
            session.TransferCompletion.SetResult();
            await run;
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The length is read off the calling thread, so a slow volume holds up neither the request nor the
    /// UI; the request is therefore already on its way when the length arrives.</summary>
    [Fact]
    public async Task The_send_is_requested_before_the_local_file_length_is_known()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "hello world", TestContext.Current.CancellationToken);
        try
        {
            var (vm, session, _) = Create();
            vm.LocalPath = path;
            vm.HostFile = "A.B";
            session.TransferCompletion = Pending();
            var callsWhenLengthArrived = -1;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.TotalBytes) && vm.TotalBytes is not null) callsWhenLengthArrived = session.Calls.Count;
            };

            var run = vm.StartCommand.ExecuteAsync(null);
            await Wait.UntilAsync(() => vm.TotalBytes is not null, "the file length");

            Assert.Equal(1, callsWhenLengthArrived);
            session.TransferCompletion.SetResult();
            await run;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Receiving_shows_an_indeterminate_bar()
    {
        var (vm, session, _) = Create();
        vm.IsReceive = true;
        vm.LocalPath = "/tmp/out";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);
        Assert.Null(vm.TotalBytes);
        Assert.True(vm.IsProgressIndeterminate);
        Assert.Equal(1, vm.ProgressMaximum);
        Assert.Equal(["transfer:Receive:A.B"], session.Calls);
        session.TransferCompletion.SetResult();
        await run;
    }

    [Fact]
    public async Task Cancel_marks_cancelling_cancels_the_token_and_lands_in_done()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);

        vm.CancelTransferCommand.Execute(null);

        Assert.True(vm.IsCancelling);
        Assert.Equal("Cancelling...", vm.StatusText);
        Assert.True(session.TransferToken.IsCancellationRequested);
        Assert.False(vm.CancelTransferCommand.CanExecute(null));
        session.TransferProgress!.Report(4096);
        Assert.Equal("Cancelling...", vm.StatusText);
        Assert.Equal(4096, vm.BytesTransferred);

        session.TransferResult = new FileTransferResult(false, "Transfer canceled by user");
        session.TransferCompletion.SetResult();
        await run;

        Assert.True(vm.IsDone);
        Assert.False(vm.Succeeded);
        Assert.Equal("Transfer canceled by user", vm.ResultMessage);
    }

    [Fact]
    public async Task A_failed_result_and_an_exception_both_land_in_done_as_failures_and_back_keeps_the_form()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferResult = new FileTransferResult(false, "TRANS17 Miscellaneous I/O error");

        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        Assert.False(vm.Succeeded);
        Assert.True(vm.Failed);
        Assert.Equal("TRANS17 Miscellaneous I/O error", vm.ResultMessage);

        vm.BackCommand.Execute(null);
        Assert.True(vm.IsForm);
        Assert.Equal("/nonexistent/a.txt", vm.LocalPath);
        Assert.Equal("A.B", vm.HostFile);
        Assert.True(vm.StartCommand.CanExecute(null));

        session.TransferException = new BackendUnavailableException("The emulator engine (b3270) exited unexpectedly.");
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        Assert.False(vm.Succeeded);
        Assert.Equal("The emulator engine (b3270) exited unexpectedly.", vm.ResultMessage);
        Assert.Equal(2, session.Calls.Count);
    }

    [Fact]
    public async Task Start_with_an_invalid_form_stays_in_form_and_calls_nothing()
    {
        var (vm, session, _) = Create();
        var started = 0;
        vm.Started += _ => started++;
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsForm);
        Assert.Equal("Choose a local file.", vm.ValidationMessage);
        Assert.Empty(session.Calls);
        Assert.Equal(0, started);
    }

    [Fact]
    public async Task Progress_reports_are_marshalled_through_the_dispatcher()
    {
        var session = new FakeEmulatorSession();
        var dispatched = new List<Action>();
        var vm = new FileTransferViewModel(session, new FakeFilePicker(), dispatched.Add, null) { LocalPath = "/nonexistent/a.txt", HostFile = "A.B" };
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);

        session.TransferProgress!.Report(10);
        Assert.Equal("Waiting for the host...", vm.StatusText);
        Assert.Single(dispatched);
        dispatched[0]();
        Assert.Equal("10 bytes", vm.StatusText);

        session.TransferCompletion.SetResult();
        await run;
    }

    [Fact]
    public async Task A_failed_result_with_no_message_gets_a_fallback()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferResult = new FileTransferResult(false, "");

        await vm.StartCommand.ExecuteAsync(null);

        Assert.True(vm.IsDone);
        Assert.True(vm.Failed);
        Assert.Equal("Transfer failed with no message from the host.", vm.ResultMessage);
    }

    // ---- closing ----

    [Fact]
    public async Task Closing_a_running_transfer_cancels_first_and_lets_a_second_close_through()
    {
        var (vm, session, _) = Create();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);

        Assert.False(vm.TryClose());
        Assert.True(vm.IsCancelling);
        Assert.True(session.TransferToken.IsCancellationRequested);

        // The engine has not answered the cancel; the user may still leave rather than wait forever.
        Assert.True(vm.TryClose());
        Assert.True(vm.IsRunning);

        session.TransferException = new OperationCanceledException();
        session.TransferCompletion.SetResult();
        await run;
        Assert.True(vm.IsDone);
    }

    [Fact]
    public async Task Closing_outside_running_is_always_allowed()
    {
        var (vm, session, _) = Create();
        Assert.True(vm.TryClose());

        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        Assert.True(vm.TryClose());
        Assert.False(vm.IsCancelling);
    }

    // ---- overwrite consent on receive ----

    private static string ExistingFile() => Path.GetTempFileName();

    [Fact]
    public async Task Receiving_into_an_existing_file_that_was_typed_is_refused()
    {
        var existing = ExistingFile();
        try
        {
            var (vm, session, _) = Create();
            vm.IsReceive = true;
            vm.LocalPath = existing;
            vm.HostFile = "A.B";

            await vm.StartCommand.ExecuteAsync(null);

            Assert.True(vm.IsForm);
            Assert.Contains("already exists", vm.ValidationMessage);
            Assert.Empty(session.Calls);
        }
        finally { File.Delete(existing); }
    }

    [Fact]
    public async Task Receiving_into_an_existing_file_remembered_from_the_last_request_is_refused()
    {
        var existing = ExistingFile();
        try
        {
            var initial = new FileTransferRequest { Direction = TransferDirection.Receive, LocalPath = existing, HostFile = "A.B" };
            var (vm, session, _) = Create(initial);

            await vm.StartCommand.ExecuteAsync(null);

            Assert.True(vm.IsForm);
            Assert.Contains("already exists", vm.ValidationMessage);
            Assert.Empty(session.Calls);
        }
        finally { File.Delete(existing); }
    }

    [Fact]
    public async Task Receiving_into_an_existing_file_chosen_in_the_save_dialog_replaces_it()
    {
        var existing = ExistingFile();
        try
        {
            var (vm, session, picker) = Create();
            vm.IsReceive = true;
            vm.HostFile = "A.B";
            picker.Result = existing;
            await vm.BrowseCommand.ExecuteAsync(null);

            await vm.StartCommand.ExecuteAsync(null);

            Assert.Equal(["transfer:Receive:A.B"], session.Calls);
            Assert.True(vm.IsDone);
        }
        finally { File.Delete(existing); }
    }

    [Fact]
    public async Task Editing_the_path_after_the_save_dialog_drops_its_consent()
    {
        var chosen = ExistingFile();
        var typed = ExistingFile();
        try
        {
            var (vm, session, picker) = Create();
            vm.IsReceive = true;
            vm.HostFile = "A.B";
            picker.Result = chosen;
            await vm.BrowseCommand.ExecuteAsync(null);
            vm.LocalPath = typed;

            await vm.StartCommand.ExecuteAsync(null);

            Assert.True(vm.IsForm);
            Assert.Contains("already exists", vm.ValidationMessage);
            Assert.Empty(session.Calls);
        }
        finally { File.Delete(chosen); File.Delete(typed); }
    }

    [Fact]
    public async Task Appending_to_an_existing_typed_file_needs_no_consent()
    {
        var existing = ExistingFile();
        try
        {
            var (vm, session, _) = Create();
            vm.IsReceive = true;
            vm.LocalPath = existing;
            vm.HostFile = "A.B";
            vm.Append = true;

            await vm.StartCommand.ExecuteAsync(null);

            Assert.Equal(["transfer:Receive:A.B"], session.Calls);
            Assert.True(vm.IsDone);
        }
        finally { File.Delete(existing); }
    }

    /// <summary>The window binds the derived flags; each must be announced when its inputs change, not only computed.</summary>
    [Fact]
    public void Derived_flags_raise_property_changed()
    {
        var (vm, _, _) = Create();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.HostType = TransferHostType.Vm;
        Assert.Superset(new HashSet<string> { nameof(vm.IsTso), nameof(vm.RecordFormats), nameof(vm.CanSetRecordFormat), nameof(vm.CanSetLrecl), nameof(vm.CanSetBlksize), nameof(vm.CanSetSpace), nameof(vm.CanSetAverageBlock) }, changed.ToHashSet());
        changed.Clear();
        vm.RecordFormat = RecordFormat.Fixed;
        Assert.Superset(new HashSet<string> { nameof(vm.HasRecordFormat), nameof(vm.CanSetLrecl), nameof(vm.CanSetBlksize) }, changed.ToHashSet());
        changed.Clear();
        vm.AllocationUnits = AllocationUnits.AvBlock;
        Assert.Superset(new HashSet<string> { nameof(vm.HasAllocation), nameof(vm.IsAvBlock), nameof(vm.CanSetSpace), nameof(vm.CanSetAverageBlock) }, changed.ToHashSet());
        changed.Clear();
        vm.IsReceive = true;
        Assert.Superset(new HashSet<string> { nameof(vm.IsReceive), nameof(vm.ShowAdvanced) }, changed.ToHashSet());
        changed.Clear();
        vm.IsBinary = true;
        Assert.Contains(nameof(vm.IsBinary), changed);
    }
}
