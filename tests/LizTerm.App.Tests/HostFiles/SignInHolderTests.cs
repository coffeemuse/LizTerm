// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

public class SignInHolderTests
{
    private static readonly HostTokenRequest First = new(null);

    /// <summary>A signIn that mints a token from the password it is given, and counts its calls.</summary>
    private sealed class Signer
    {
        public int Count;
        public HostSignIn SignIn => (credentials, _) =>
        {
            Interlocked.Increment(ref Count);
            return Task.FromResult(new HostSessionToken($"tok:{credentials.Userid}:{credentials.Password}"));
        };
    }

    /// <summary>A signIn the host refuses <paramref name="refusals"/> times before it takes the password.</summary>
    private static HostSignIn Refusing(int refusals)
    {
        var left = refusals;
        return (credentials, _) => Interlocked.Decrement(ref left) >= 0
            ? Task.FromException<HostSessionToken>(
                new HostFileException(HostFileErrorKind.Unauthenticated, "The userid or password was not accepted."))
            : Task.FromResult(new HostSessionToken($"tok:{credentials.Userid}:{credentials.Password}"));
    }

    private static (SignInHolder Holder, FakeCredentialPrompt Prompt, HostTokenProvider Provider, Signer Signer) Create(string? userid = "MVSCE02")
    {
        var holder = new SignInHolder("MVS/CE", "http://mvs:8080/zosmf", userid);
        var prompt = new FakeCredentialPrompt();
        var signer = new Signer();
        return (holder, prompt, holder.ProviderFor(prompt), signer);
    }

    [Fact]
    public async Task Signs_in_once_and_answers_the_same_token_afterwards()
    {
        var (holder, prompt, provider, signer) = Create();

        var first = await provider(First, signer.SignIn, CancellationToken.None);
        var second = await provider(First, signer.SignIn, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(1, signer.Count);
        Assert.True(holder.IsSignedIn);
        Assert.Equal(new CredentialPromptRequest("MVS/CE", "http://mvs:8080/zosmf", "MVSCE02", SignInReason.First), prompt.LastRequest);
    }

    [Fact]
    public async Task A_rejected_token_signs_in_again_as_expired()
    {
        var (_, prompt, provider, signer) = Create();

        var first = await provider(First, signer.SignIn, CancellationToken.None);
        var again = await provider(new HostTokenRequest(first), signer.SignIn, CancellationToken.None);

        Assert.NotSame(first, again);
        Assert.Equal(2, signer.Count);
        Assert.Equal(new[] { "ask:MVSCE02:First", "ask:MVSCE02:Expired" }, prompt.Calls);
    }

    [Fact]
    public async Task A_rejected_token_already_replaced_answers_the_newer_one_without_asking()
    {
        var (_, prompt, provider, signer) = Create();
        var stale = await provider(First, signer.SignIn, CancellationToken.None);
        var current = await provider(new HostTokenRequest(stale), signer.SignIn, CancellationToken.None);

        var answer = await provider(new HostTokenRequest(stale), signer.SignIn, CancellationToken.None);

        Assert.Same(current, answer);
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_refused_password_asks_again_as_rejected()
    {
        var (holder, prompt, provider, _) = Create();

        var token = await provider(First, Refusing(1), CancellationToken.None);

        Assert.NotNull(token);
        Assert.True(holder.IsSignedIn);
        Assert.Equal(new[] { "ask:MVSCE02:First", "ask:MVSCE02:Rejected" }, prompt.Calls);
    }

    [Fact]
    public async Task Cancelling_after_a_refused_password_answers_null_and_holds_no_token()
    {
        var (holder, prompt, provider, _) = Create();
        prompt.Answers.Enqueue(new HostCredentials("MVSCE02", "wrong"));
        prompt.Answer = null;

        Assert.Null(await provider(First, Refusing(1), CancellationToken.None));

        Assert.False(holder.IsSignedIn);
        Assert.Equal(new[] { "ask:MVSCE02:First", "ask:MVSCE02:Rejected" }, prompt.Calls);
    }

    [Fact]
    public async Task Concurrent_first_requests_share_one_prompt()
    {
        var (_, prompt, provider, signer) = Create();
        prompt.Gate = new TaskCompletionSource();

        var a = provider(First, signer.SignIn, CancellationToken.None).AsTask();
        var b = provider(First, signer.SignIn, CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 1, "the first prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, prompt.AskCount);
        Assert.Equal(1, signer.Count);
    }

    [Fact]
    public async Task Concurrent_refusals_of_the_same_token_prompt_once()
    {
        var (_, prompt, provider, signer) = Create();
        var refused = await provider(First, signer.SignIn, CancellationToken.None);
        prompt.Gate = new TaskCompletionSource();

        var a = provider(new HostTokenRequest(refused), signer.SignIn, CancellationToken.None).AsTask();
        var b = provider(new HostTokenRequest(refused), signer.SignIn, CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 2, "the second prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Same(results[0], results[1]);
        Assert.Equal(2, prompt.AskCount);
        Assert.Equal(2, signer.Count);
    }

    [Fact]
    public async Task A_cancelled_prompt_answers_null_and_holds_no_token()
    {
        var (holder, prompt, provider, signer) = Create();
        prompt.Answer = null;

        Assert.Null(await provider(First, signer.SignIn, CancellationToken.None));
        Assert.False(holder.IsSignedIn);
        Assert.Equal(0, signer.Count);
    }

    [Fact]
    public async Task Operations_waiting_on_a_cancelled_prompt_fail_with_it()
    {
        var (_, prompt, provider, signer) = Create();
        prompt.Answer = null;
        prompt.Gate = new TaskCompletionSource();

        var a = provider(First, signer.SignIn, CancellationToken.None).AsTask();
        var b = provider(First, signer.SignIn, CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 1, "the first prompt");
        prompt.Gate.SetResult();
        var results = await Task.WhenAll(a, b);

        Assert.Equal(new HostSessionToken?[] { null, null }, results);
        Assert.Equal(1, prompt.AskCount);

        await provider(First, signer.SignIn, CancellationToken.None);
        Assert.Equal(2, prompt.AskCount);
    }

    /// <summary>The prompt's outcome is shared whatever it is: a sign-in that fails for anything but the password
    /// fails the operations that were waiting on it too, rather than asking each for the password just typed.</summary>
    [Fact]
    public async Task Operations_waiting_on_a_failed_sign_in_fail_with_it()
    {
        var (holder, prompt, provider, signer) = Create();
        prompt.Gate = new TaskCompletionSource();
        var failure = new HostFileException(HostFileErrorKind.Unreachable, "Sign-in: cannot reach the host.");
        HostSignIn unreachable = (_, _) => Task.FromException<HostSessionToken>(failure);

        var a = provider(First, unreachable, CancellationToken.None).AsTask();
        var b = provider(First, unreachable, CancellationToken.None).AsTask();
        await Wait.UntilAsync(() => prompt.AskCount == 1, "the first prompt");
        prompt.Gate.SetResult();

        Assert.Same(failure, await Assert.ThrowsAsync<HostFileException>(() => a));
        Assert.Same(failure, await Assert.ThrowsAsync<HostFileException>(() => b));
        Assert.Equal(1, prompt.AskCount);
        Assert.False(holder.IsSignedIn);

        await provider(First, signer.SignIn, CancellationToken.None); // a later operation asks afresh
        Assert.Equal(2, prompt.AskCount);
    }

    [Fact]
    public async Task A_cancelled_sign_in_after_an_expired_token_holds_nothing()
    {
        var (holder, prompt, provider, signer) = Create();
        var refused = await provider(First, signer.SignIn, CancellationToken.None);
        prompt.Answer = null;

        Assert.Null(await provider(new HostTokenRequest(refused), signer.SignIn, CancellationToken.None));
        Assert.False(holder.IsSignedIn);
    }

    [Fact]
    public async Task A_sign_in_that_fails_for_anything_else_still_prefills_the_userid_just_typed()
    {
        var (_, prompt, provider, signer) = Create(userid: null);
        prompt.Answers.Enqueue(new HostCredentials("IBMUSER", "pw"));
        HostSignIn unreachable = (_, _) => Task.FromException<HostSessionToken>(
            new HostFileException(HostFileErrorKind.Unreachable, "Cannot reach the host."));

        await Assert.ThrowsAsync<HostFileException>(() => provider(First, unreachable, CancellationToken.None).AsTask());
        await provider(First, signer.SignIn, CancellationToken.None);

        // Not only after the host takes the password: a certificate refusal or an unreachable host must not throw
        // the typed userid away.
        Assert.Equal(new[] { "ask::First", "ask:IBMUSER:First" }, prompt.Calls);
    }

    [Fact]
    public async Task The_userid_typed_last_prefills_the_next_prompt()
    {
        var (_, prompt, provider, signer) = Create(userid: null);
        prompt.Answer = new HostCredentials("IBMUSER", "pw");
        var held = await provider(First, signer.SignIn, CancellationToken.None);

        await provider(new HostTokenRequest(held), signer.SignIn, CancellationToken.None);

        Assert.Equal(new[] { "ask::First", "ask:IBMUSER:Expired" }, prompt.Calls);
    }

    [Fact]
    public async Task Sign_out_ends_the_session_once_and_drops_the_token()
    {
        var (holder, _, provider, signer) = Create();
        await provider(First, signer.SignIn, CancellationToken.None);
        var service = new FakeHostFileService();

        await holder.SignOutAsync(service, TestContext.Current.CancellationToken);
        await holder.SignOutAsync(service, TestContext.Current.CancellationToken); // no token now, no second call

        Assert.False(holder.IsSignedIn);
        Assert.Equal(1, service.CallsSnapshot().Count(c => c.StartsWith("signout")));
    }

    [Fact]
    public async Task A_sign_out_the_host_never_answers_is_given_up_on_after_the_cap()
    {
        var (holder, _, provider, signer) = Create();
        await provider(First, signer.SignIn, CancellationToken.None);
        var service = new FakeHostFileService { Gate = new TaskCompletionSource() }; // never answers
        var token = holder.Take()!;

        await SignInHolder.EndAsync(service, token, TestContext.Current.CancellationToken, cap: TimeSpan.FromMilliseconds(50));

        Assert.Contains("signout", service.CallsSnapshot());
        Assert.False(holder.IsSignedIn);
    }

    [Fact]
    public async Task A_cancelled_wait_throws_before_asking()
    {
        var (_, prompt, provider, signer) = Create();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider(First, signer.SignIn, cancelled.Token).AsTask());
        Assert.Equal(0, prompt.AskCount);
    }
}
