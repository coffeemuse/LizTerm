// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    [AvaloniaFact]
    public void Record_format_and_lrecl_controls_are_disabled_for_cics()
    {
        var (window, vm, _) = Show();
        var recordFormat = window.FindControl<ComboBox>("RecordFormatBox")!;
        var lrecl = window.FindControl<TextBox>("LreclBox")!;
        vm.RecordFormat = RecordFormat.Fixed;
        Assert.True(recordFormat.IsEnabled);
        Assert.True(lrecl.IsEnabled);

        vm.HostType = TransferHostType.Cics;
        Assert.False(recordFormat.IsEnabled);
        Assert.False(lrecl.IsEnabled);

        vm.HostType = TransferHostType.Vm;
        Assert.True(recordFormat.IsEnabled);
        Assert.True(lrecl.IsEnabled);
    }

    [AvaloniaFact]
    public async Task Closing_again_while_the_cancel_is_unanswered_closes_the_window()
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

        window.Close();
        Assert.True(closed);

        session.TransferException = new OperationCanceledException();
        session.TransferCompletion.SetResult();
        await run;
        Assert.True(vm.IsDone);
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_form_and_cancels_a_running_transfer_first()
    {
        var (form, _, _) = Show();
        var formClosed = false;
        form.Closed += (_, _) => formClosed = true;
        form.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(formClosed);

        var (window, vm, session) = Show();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        vm.LocalPath = "/nonexistent/a.txt";
        vm.HostFile = "A.B";
        session.TransferCompletion = Pending();
        var run = vm.StartCommand.ExecuteAsync(null);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(closed);
        Assert.True(vm.IsCancelling);
        Assert.True(session.TransferToken.IsCancellationRequested);

        // A held Escape auto-repeats; the repeats must not become the second close that lets the window go.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(closed);

        session.TransferResult = new FileTransferResult(false, "Transfer canceled by user");
        session.TransferCompletion.SetResult();
        await run;
        Assert.True(vm.IsDone);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
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

    [AvaloniaFact]
    public void Labels_pass_null_through_and_never_convert_back()
    {
        Assert.Null(TransferLabels.Converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Throws<NotSupportedException>(() => TransferLabels.Converter.ConvertBack("TSO", typeof(TransferHostType), null, CultureInfo.InvariantCulture));
    }
}
