// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

public class CredentialHolderTests
{
    private static readonly HostCredentialRequest First = new(false);

    private static (CredentialHolder Holder, FakeCredentialPrompt Prompt, HostCredentialProvider Provider) Create(string? userid = "MVSCE02")
    {
        var holder = new CredentialHolder("MVS/CE", "http://mvs:8080/zosmf", userid);
        var prompt = new FakeCredentialPrompt();
        return (holder, prompt, holder.ProviderFor(prompt));
    }

    [Fact]
    public async Task Asks_once_and_answers_the_same_pair_afterwards()
    {
        var (holder, prompt, provider) = Create();

        var first = await provider(First, CancellationToken.None);
        var second = await provider(First, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, prompt.AskCount);
        Assert.True(holder.HasCredentials);
        Assert.Equal(new CredentialPromptRequest("MVS/CE", "http://mvs:8080/zosmf", "MVSCE02", false), prompt.LastRequest);
    }

    [Fact]
    public async Task A_refusal_of_the_current_pair_asks_again_as_a_retry()
    {
        var (_, prompt, provider) = Create();
        var wrong = new HostCredentials("MVSCE02", "wrong");
        var right = new HostCredentials("MVSCE02", "right");
        prompt.Answers.Enqueue(wrong);
        prompt.Answers.Enqueue(right);

        var first = await provider(First, CancellationToken.None);
        var retried = await provider(new HostCredentialRequest(true, first), CancellationToken.None);

        Assert.Same(wrong, first);
        Assert.Same(right, retried);
        Assert.Equal(new[] { "ask:MVSCE02:False", "ask:MVSCE02:True" }, prompt.Calls);
    }

    [Fact]
    public async Task A_refusal_of_a_pair_already_replaced_answers_the_newer_pair_without_asking()
    {
        var (_, prompt, provider) = Create();
        var old = new HostCredentials("MVSCE02", "old");
        var newer = new HostCredentials("MVSCE02", "new");
        prompt.Answers.Enqueue(old);
        prompt.Answers.Enqueue(newer);
        var first = await provider(First, CancellationToken.None);
        var replaced = await provider(new HostCredentialRequest(true, first), CancellationToken.None);

        var late = await provider(new HostCredentialRequest(true, old), CancellationToken.None);

        Assert.Same(newer, replaced);
        Assert.Same(newer, late);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task Concurrent_first_requests_share_one_prompt()
    {
        var (_, prompt, provider) = Create();
        prompt.Gate = new TaskCompletionSource();

        var a = provider(First, CancellationToken.None).AsTask();
        var b = provider(First, CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 1, "the first prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, prompt.AskCount);
    }

    [Fact]
    public async Task Concurrent_refusals_of_the_same_pair_prompt_once()
    {
        var (_, prompt, provider) = Create();
        var refused = await provider(First, CancellationToken.None);
        prompt.Answer = new HostCredentials("MVSCE02", "fixed");
        prompt.Gate = new TaskCompletionSource();

        var a = provider(new HostCredentialRequest(true, refused), CancellationToken.None).AsTask();
        var b = provider(new HostCredentialRequest(true, refused), CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 2, "the retry prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Same(results[0], results[1]);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_cancelled_prompt_answers_null_and_keeps_nothing()
    {
        var (holder, prompt, provider) = Create();
        prompt.Answer = null;

        Assert.Null(await provider(First, CancellationToken.None));
        Assert.False(holder.HasCredentials);
    }

    [Fact]
    public async Task Operations_waiting_on_a_cancelled_prompt_fail_with_it()
    {
        var (_, prompt, provider) = Create();
        prompt.Answer = null;
        prompt.Gate = new TaskCompletionSource();

        var a = provider(First, CancellationToken.None).AsTask();
        var b = provider(First, CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 1, "the first prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Equal(new HostCredentials?[] { null, null }, results);
        Assert.Equal(1, prompt.AskCount);

        await provider(First, CancellationToken.None);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_cancelled_retry_keeps_nothing()
    {
        var (holder, prompt, provider) = Create();
        var refused = await provider(First, CancellationToken.None);
        prompt.Answer = null;

        Assert.Null(await provider(new HostCredentialRequest(true, refused), CancellationToken.None));
        Assert.False(holder.HasCredentials);
    }

    [Fact]
    public async Task The_userid_typed_last_prefills_the_next_prompt()
    {
        var (holder, prompt, provider) = Create(userid: null);
        prompt.Answer = new HostCredentials("IBMUSER", "pw");
        await provider(First, CancellationToken.None);
        holder.Forget();

        await provider(First, CancellationToken.None);

        Assert.Equal(new[] { "ask::False", "ask:IBMUSER:False" }, prompt.Calls);
    }

    [Fact]
    public async Task Forget_drops_the_pair()
    {
        var (holder, prompt, provider) = Create();
        await provider(First, CancellationToken.None);

        holder.Forget();

        Assert.False(holder.HasCredentials);
        await provider(First, CancellationToken.None);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_cancelled_wait_throws_before_asking()
    {
        var (_, prompt, provider) = Create();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider(First, cancelled.Token).AsTask());
        Assert.Equal(0, prompt.AskCount);
    }
}
