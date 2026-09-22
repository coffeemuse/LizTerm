// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class BrowserOperationsTests
{
    [Fact]
    public async Task One_operation_at_a_time_and_a_second_is_ignored()
    {
        var ops = new BrowserOperations();
        var gate = new TaskCompletionSource();
        var busySeen = new List<bool>();
        ops.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BrowserOperations.IsBusy)) busySeen.Add(ops.IsBusy); };

        var first = ops.RunExclusiveAsync(async _ => { await gate.Task; ops.StatusText = "✓ First done."; });
        var ran = false;
        await ops.RunExclusiveAsync(_ => { ran = true; return Task.CompletedTask; });

        Assert.True(ops.IsBusy);
        Assert.False(ops.IsIdle);
        Assert.False(ran);
        Assert.True(ops.CancelCommand.CanExecute(null));
        gate.SetResult();
        await first;
        Assert.False(ops.IsBusy);
        Assert.Equal(new[] { true, false }, busySeen);
        Assert.Equal("✓ First done.", ops.StatusText);
        Assert.False(ops.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_connection_failure_is_the_banner_with_retry_and_retry_runs_again()
    {
        var ops = new BrowserOperations();
        var attempts = 0;
        Task Attempt() => ops.RunExclusiveAsync(_ =>
        {
            attempts++;
            if (attempts == 1) throw new HostFileException(HostFileErrorKind.Unreachable, "X: cannot reach the host.");
            ops.StatusText = "✓ Listed.";
            return Task.CompletedTask;
        }, Attempt);

        await Attempt();

        Assert.True(ops.HasError);
        Assert.True(ops.CanRetry);
        Assert.Equal("X: cannot reach the host.", ops.ErrorText);
        Assert.Equal("", ops.StatusText);
        await ops.RetryCommand.ExecuteAsync(null);
        Assert.False(ops.HasError);
        Assert.False(ops.CanRetry);
        Assert.Equal("✓ Listed.", ops.StatusText);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Describe_words_the_banner_and_other_failures_are_the_status_line()
    {
        var ops = new BrowserOperations();

        await ops.RunExclusiveAsync(_ => throw new HostFileException(HostFileErrorKind.Unauthenticated, "Sign-in: refused."), describe: ex => "Custom: " + ex.Message);
        Assert.Equal("Custom: Sign-in: refused.", ops.ErrorText);

        await ops.RunExclusiveAsync(_ => throw new HostFileException(HostFileErrorKind.NotFound, "X: not found."));
        Assert.Null(ops.ErrorText);
        Assert.Equal("✗ Not found.", ops.StatusText);
    }

    [Fact]
    public async Task Cancel_stops_the_operation_and_answers_its_question()
    {
        var ops = new BrowserOperations();
        var running = ops.RunExclusiveAsync(async token =>
        {
            var answer = await ops.AskAsync(new ConfirmationRequest("Sure?", "Yes"));
            Assert.Equal(ConfirmChoice.Cancel, answer.Choice);
            await Task.Delay(Timeout.Infinite, token);
        });
        await Wait.UntilAsync(() => ops.HasConfirmation, "the question");

        ops.CancelCommand.Execute(null);
        await running;

        Assert.False(ops.HasConfirmation);
        Assert.Equal("– Cancelled.", ops.StatusText);
        Assert.False(ops.IsBusy);
    }

    [Fact]
    public async Task A_warning_waits_for_the_operation_to_end_or_lands_at_once()
    {
        var ops = new BrowserOperations();
        ops.WarnWhenIdle("The pin was not saved.");
        Assert.Equal("⚠ The pin was not saved.", ops.StatusText);

        var gate = new TaskCompletionSource();
        var running = ops.RunExclusiveAsync(async _ => { await gate.Task; ops.StatusText = "✓ Done."; });
        ops.WarnWhenIdle("Later.");
        Assert.Equal("⚠ The pin was not saved.", ops.StatusText);
        gate.SetResult();
        await running;
        Assert.Equal("⚠ Later.", ops.StatusText);
    }

    [Fact]
    public async Task When_idle_is_null_while_nothing_runs_and_completes_when_the_operation_ends()
    {
        var ops = new BrowserOperations();
        Assert.Null(ops.WhenIdle());
        var gate = new TaskCompletionSource();
        var running = ops.RunExclusiveAsync(async _ => await gate.Task);
        var idle = ops.WhenIdle();
        Assert.NotNull(idle);
        Assert.False(idle!.IsCompleted);
        gate.SetResult();
        await running;
        await idle;
    }

    [Fact]
    public async Task Disposed_runs_nothing_and_answers_cancel()
    {
        var ops = new BrowserOperations();
        var question = new ConfirmationRequest("Sure?", "Yes");
        var asked = ops.AskAsync(question);
        ops.Dispose();
        Assert.True(ops.IsDisposed);
        Assert.Equal(ConfirmChoice.Cancel, (await asked).Choice);
        var ran = false;
        await ops.RunExclusiveAsync(_ => { ran = true; return Task.CompletedTask; });
        Assert.False(ran);
        Assert.Equal(ConfirmChoice.Cancel, (await ops.AskAsync(new ConfirmationRequest("Again?", "Yes"))).Choice);
    }

    [Theory]
    [InlineData(HostFileErrorKind.Unreachable, true)]
    [InlineData(HostFileErrorKind.Unauthenticated, true)]
    [InlineData(HostFileErrorKind.CertificateRejected, true)]
    [InlineData(HostFileErrorKind.Unsupported, true)]
    [InlineData(HostFileErrorKind.NotFound, false)]
    [InlineData(HostFileErrorKind.ServerError, false)]
    public void Connection_failures_are_the_four_kinds(HostFileErrorKind kind, bool expected) =>
        Assert.Equal(expected, BrowserOperations.IsConnectionFailure(new HostFileException(kind, "x")));

    /// <summary>The two tabs share one banner: a tab dropping its own Retry (the Datasets tab's dataset change) must
    /// leave one the other tab's operation left.</summary>
    [Fact]
    public async Task Dropping_a_tabs_own_retry_leaves_the_other_tabs_banner()
    {
        var ops = new BrowserOperations();
        object datasets = new(), uss = new();
        await ops.RunExclusiveAsync(_ => throw new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host."),
            retry: () => Task.CompletedTask, owner: uss);
        Assert.True(ops.CanRetry);

        ops.DropRetry(owner: datasets);
        Assert.True(ops.CanRetry);
        Assert.Equal("cannot reach the host.", ops.ErrorText);

        ops.DropRetry(owner: uss);
        Assert.False(ops.CanRetry);
        Assert.Null(ops.ErrorText);
    }

    [Fact]
    public async Task The_last_run_is_known_to_have_been_cancelled_until_the_next_one()
    {
        var ops = new BrowserOperations();
        var running = ops.RunExclusiveAsync(token => Task.Delay(Timeout.Infinite, token));
        ops.CancelCommand.Execute(null);
        await running;
        Assert.True(ops.LastRunCancelled);

        await ops.RunExclusiveAsync(_ => Task.CompletedTask);
        Assert.False(ops.LastRunCancelled);
    }
}
