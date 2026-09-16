// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http.Headers;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfAuthTests
{
    internal static readonly Uri Base = new("http://mvs.test:8080/zosmf");

    internal static HostCredentialProvider Answering(List<HostCredentialRequest> asked, params HostCredentials?[] answers)
    {
        var queue = new Queue<HostCredentials?>(answers);
        return (request, _) =>
        {
            asked.Add(request);
            return ValueTask.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        };
    }

    private static string Basic(string userid, string password) =>
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Latin1.GetBytes($"{userid}:{password}"))).ToString();

    [Fact]
    public async Task Info_requires_auth_so_server_info_is_asked_with_credentials()
    {
        var handler = new RecordedHandler().Then("info-200");
        var asked = new List<HostCredentialRequest>();
        using var service = new MvsmfFileService(handler, Base, Answering(asked, new HostCredentials("MVSCE02", "pw")));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new HostServerInfo("mvsMF", "1.0.0-dev", "MVS 3.8j"), info);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://mvs.test:8080/zosmf/info", request.Uri.ToString());
        Assert.Equal(Basic("MVSCE02", "pw"), request.Authorization);
        Assert.Equal(new[] { new HostCredentialRequest(false) }, asked);
    }

    [Fact]
    public async Task Basic_auth_every_request_sends_credentials_each_time()
    {
        var handler = new RecordedHandler().Then("info-200").Then("info-200");
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("MVSCE02", "pw")));

        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);
        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, r => Assert.Equal(Basic("MVSCE02", "pw"), r.Authorization));
    }

    [Fact]
    public async Task A_401_asks_again_and_repeats_the_request_once()
    {
        var handler = new RecordedHandler().Then("info-401").Then("info-200");
        var asked = new List<HostCredentialRequest>();
        var wrong = new HostCredentials("MVSCE02", "wrong");
        using var service = new MvsmfFileService(handler, Base,
            Answering(asked, wrong, new HostCredentials("MVSCE02", "right")));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1.0.0-dev", info.ProductVersion);
        Assert.Equal(new[] { false, true }, asked.Select(r => r.IsRetry));
        Assert.Null(asked[0].Rejected);
        Assert.Same(wrong, asked[1].Rejected);
        Assert.Equal(new[] { Basic("MVSCE02", "wrong"), Basic("MVSCE02", "right") }, handler.Requests.Select(r => r.Authorization!));
    }

    [Fact]
    public async Task A_second_401_fails_as_unauthenticated_without_the_password_in_the_message()
    {
        var handler = new RecordedHandler().Then("info-401").Then("info-401");
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("MVSCE02", "hunter22")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("hunter22", ex.ToString());
    }

    [Fact]
    public async Task A_cancelled_prompt_sends_nothing()
    {
        var handler = new RecordedHandler();
        using var service = new MvsmfFileService(handler, Base, Answering([], (HostCredentials?)null));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal("Sign-in was cancelled.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_refused_connection_is_unreachable()
    {
        var handler = new RecordedHandler().Then((_, _) => throw new HttpRequestException("Connection refused (mvs.test:8080)"));
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal("Server information: cannot reach the host (Connection refused (mvs.test:8080)).", ex.Message);
    }

    [Fact]
    public async Task An_unreadable_answer_is_a_server_error()
    {
        var handler = new RecordedHandler().Then(System.Net.HttpStatusCode.OK, "not json");
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("Server information: the host's answer could not be read.", ex.Message);
    }

    [Fact]
    public void The_production_client_never_times_out_a_whole_transfer()
    {
        using var service = new MvsmfFileService(new MvsmfOptions(Base), Answering([], new HostCredentials("U", "p")));
        Assert.Equal(Timeout.InfiniteTimeSpan, service.HttpClientTimeout);
    }

    [Fact]
    public void The_production_handler_connects_within_ten_seconds_and_keeps_no_cookies()
    {
        using var handler = MvsmfFileService.CreateHandler(new MvsmfCertificateCheck(null));
        Assert.Equal(TimeSpan.FromSeconds(10), handler.ConnectTimeout);
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task A_slow_sign_in_is_not_a_host_timeout()
    {
        var handler = new RecordedHandler().Then("info-200");
        HostCredentialProvider slow = async (request, ct) =>
        {
            await Task.Delay(300, ct);
            return new HostCredentials("MVSCE02", "pw");
        };
        using var service = new MvsmfFileService(handler, Base, slow, idleTimeout: TimeSpan.FromMilliseconds(100));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1.0.0-dev", info.ProductVersion);
    }

    [Fact]
    public async Task A_slow_retry_prompt_is_not_a_host_timeout()
    {
        var handler = new RecordedHandler().Then("info-401").Then("info-200");
        HostCredentialProvider slow = async (request, ct) =>
        {
            if (request.IsRetry) await Task.Delay(300, ct);
            return new HostCredentials("MVSCE02", "pw");
        };
        using var service = new MvsmfFileService(handler, Base, slow, idleTimeout: TimeSpan.FromMilliseconds(100));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1.0.0-dev", info.ProductVersion);
    }

    [Fact]
    public async Task A_connect_timeout_is_unreachable()
    {
        var handler = new RecordedHandler().Then((_, _) => throw new TaskCanceledException("The operation was canceled.", new TimeoutException()));
        using var service = new MvsmfFileService(handler, Base, Answering([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal("Server information: cannot reach the host (no answer within 10 s).", ex.Message);
    }
}
