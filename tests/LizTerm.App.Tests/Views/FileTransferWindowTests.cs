using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

/// <summary>The dialog on the headless platform: panels follow the phase, Start reaches the view model, and
/// closing a running transfer cancels it instead of closing. The transfer itself is the fake session's.</summary>
public class FileTransferWindowTests
{
    private static (FileTransferWindow Window, FileTransferViewModel Vm, FakeEmulatorSession Session) Show()
    {
        var session = new FakeEmulatorSession();
        var vm = new FileTransferViewModel(session, new FakeFilePicker(), action => action(), null);
        var window = new FileTransferWindow { DataContext = vm };
        window.Show();
        return (window, vm, session);
    }

    private static (StackPanel Form, StackPanel Running, StackPanel Done) Panels(FileTransferWindow window) =>
        (window.FindControl<StackPanel>("FormPanel")!, window.FindControl<StackPanel>("RunningPanel")!, window.FindControl<StackPanel>("DonePanel")!);

    private static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [AvaloniaFact]
    public async Task Panels_follow_the_phase()
    {
        var (window, vm, session) = Show();
        var (form, running, done) = Panels(window);
        Assert.True(form.IsVisible);
        Assert.False(running.IsVisible);
        Assert.False(done.IsVisible);

        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);
        Assert.False(form.IsVisible);
        Assert.True(running.IsVisible);
        Assert.False(done.IsVisible);
        Assert.True(window.FindControl<Button>("CancelButton")!.IsEffectivelyEnabled);

        session.TransferCompletion.SetResult();
        await run;
        Assert.False(running.IsVisible);
        Assert.True(done.IsVisible);

        vm.BackCommand.Execute(null);
        Assert.True(form.IsVisible);
        Assert.False(done.IsVisible);
    }

    [AvaloniaFact]
    public void Start_on_an_empty_form_shows_the_validation_message_and_stays_in_form()
    {
        var (window, vm, session) = Show();
        var start = window.FindControl<Button>("StartButton")!;
        Assert.True(start.IsEffectivelyEnabled);
        start.Command!.Execute(null);
        Assert.Equal("Choose a local file.", vm.ValidationMessage);
        Assert.True(vm.IsForm);
        Assert.Empty(session.Calls);
    }

    [AvaloniaFact]
    public async Task Closing_while_running_cancels_instead_of_closing()
    {
        var (window, vm, session) = Show();
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.False(closed);
        Assert.True(vm.IsCancelling);
        Assert.True(session.TransferToken.IsCancellationRequested);

        session.TransferException = new OperationCanceledException();
        session.TransferCompletion.SetResult();
        await run;
        Assert.True(vm.IsDone);
        Assert.Equal("Transfer cancelled.", vm.ResultMessage);

        window.Close();
        Assert.True(closed);
    }

    [Theory]
    [InlineData(TransferHostType.Tso, "TSO")]
    [InlineData(TransferHostType.Vm, "VM")]
    [InlineData(TransferHostType.Cics, "CICS")]
    [InlineData(AllocationUnits.AvBlock, "AVBLOCK")]
    [InlineData(AllocationUnits.Default, "Default")]
    [InlineData(AllocationUnits.Tracks, "Tracks")]
    [InlineData(RecordFormat.Default, "Default")]
    [InlineData(RecordFormat.Undefined, "Undefined")]
    public void Combo_box_labels(object value, string expected) =>
        Assert.Equal(expected, TransferLabels.Converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture));
}
