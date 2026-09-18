// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfAuthTests
{
    internal static readonly Uri Base = new("http://mvs.test:8080/zosmf");

    /// <summary>A token provider that signs in with <paramref name="credentials"/> the first time and after the host
    /// rejects the token it holds; it records each request's Rejected token for assertions.</summary>
    internal static HostTokenProvider Providing(List<HostSessionToken?> asked, HostCredentials credentials)
    {
        HostSessionToken? held = null;
        return async (request, signIn, ct) =>
        {
            asked.Add(request.Rejected);
            if (held is not null && !ReferenceEquals(request.Rejected, held)) return held;
            held = await signIn(credentials, ct);
            return held;
        };
    }

    private static string Basic(string userid, string password) =>
        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.Latin1.GetBytes($"{userid}:{password}"))).ToString();

    [Fact]
    public async Task Sign_in_posts_basic_and_holds_the_cookie_for_later_requests()
    {
        var handler = new RecordedHandler().Then("login-200").Then("info-200");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        var login = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, login.Method);
        Assert.Equal("http://mvs.test:8080/zosmf/services/authenticate", login.Uri.ToString());
        Assert.Equal(Basic("MVSCE02", "pw"), login.Authorization);
        Assert.Equal("LizTerm", login.Csrf);
        var info = handler.Requests[1];
        Assert.Equal("http://mvs.test:8080/zosmf/info", info.Uri.ToString());
        Assert.Null(info.Authorization);
        Assert.Equal("LtpaToken2=<token>", info.Cookie);
        Assert.Equal("LizTerm", info.Csrf);
    }

    [Fact]
    public async Task A_bad_password_at_sign_in_is_unauthenticated()
    {
        var handler = new RecordedHandler().Then("login-401");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "wrong")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.DoesNotContain("wrong", ex.Message);
    }

    [Fact]
    public async Task A_host_with_no_authenticate_route_is_unsupported()
    {
        var handler = new RecordedHandler().Then("login-404");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unsupported, ex.Kind);
    }

    [Fact]
    public async Task An_expired_token_signs_in_again_and_repeats_the_request_once()
    {
        var handler = new RecordedHandler()
            .Then("login-200")
            .Then(HttpStatusCode.Unauthorized, """{"rc":8}""")
            .Then("login-200")
            .Then("info-200");
        var asked = new List<HostSessionToken?>();
        using var service = new MvsmfFileService(handler, Base, Providing(asked, new HostCredentials("MVSCE02", "pw")));

        await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        // First provider call has no rejected token; the second names the token the host refused.
        Assert.Equal(2, asked.Count);
        Assert.Null(asked[0]);
        Assert.NotNull(asked[1]);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith("/info")), r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task A_second_401_fails_as_unauthenticated()
    {
        var handler = new RecordedHandler()
            .Then("login-200")
            .Then(HttpStatusCode.Unauthorized, """{"rc":8}""")
            .Then("login-200")
            .Then(HttpStatusCode.Unauthorized, """{"rc":8}""");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
    }

    [Fact]
    public async Task A_cancelled_prompt_sends_nothing_after_login()
    {
        var handler = new RecordedHandler();
        HostTokenProvider cancels = (_, _, _) => ValueTask.FromResult<HostSessionToken?>(null);
        using var service = new MvsmfFileService(handler, Base, cancels);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal("Sign-in was cancelled.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Sign_out_deletes_the_session_and_a_dead_token_is_fine()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.NoContent).Then(HttpStatusCode.Unauthorized);
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        await service.SignOutAsync(new HostSessionToken("tok"), TestContext.Current.CancellationToken);
        await service.SignOutAsync(new HostSessionToken("tok"), TestContext.Current.CancellationToken); // 401, still fine

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Delete, r.Method));
        Assert.All(handler.Requests, r => Assert.Equal("services/authenticate", r.Uri.AbsolutePath.TrimStart('/')["zosmf/".Length..]));
        Assert.All(handler.Requests, r => Assert.Equal("LtpaToken2=tok", r.Cookie));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "info-200", true)]
    [InlineData(HttpStatusCode.Unauthorized, null, false)]
    public async Task Probe_answers_the_info_when_anonymous_works_and_null_on_401(HttpStatusCode status, string? fixture, bool hasInfo)
    {
        var handler = fixture is null ? new RecordedHandler().Then(status, "") : new RecordedHandler().Then(fixture);
        var asked = new List<HostSessionToken?>();
        using var service = new MvsmfFileService(handler, Base, Providing(asked, new HostCredentials("MVSCE02", "pw")));

        var info = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(hasInfo, info is not null);
        Assert.Empty(asked); // the probe never signs in
        var request = Assert.Single(handler.Requests);
        Assert.Null(request.Authorization);
        Assert.Null(request.Cookie);
        Assert.Equal("http://mvs.test:8080/zosmf/info", request.Uri.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "<html>404</html>", "text/html")]
    [InlineData(HttpStatusCode.OK, "<html>Welcome</html>", "text/html")]
    public async Task Probe_reports_a_url_that_is_not_mvsmf(HttpStatusCode status, string body, string contentType)
    {
        var handler = new RecordedHandler().Then(status, body, contentType);
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ProbeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unsupported, ex.Kind);
        Assert.Equal("Nothing at this URL answers as mvsMF.", ex.Message);
    }

    /// <summary>Named after the compat tag, as the log's rule requires: /info needs a sign-in, so server info goes
    /// through the token path like everything else.</summary>
    [Fact]
    public async Task Info_requires_auth_so_server_info_signs_in_first()
    {
        var handler = new RecordedHandler().Then("login-200").Then("info-200");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("MVSCE02", "pw")));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new HostServerInfo("mvsMF", "1.1.0", "MVS 3.8j"), info);
        Assert.Equal(new[] { "/zosmf/services/authenticate", "/zosmf/info" }, handler.Requests.Select(r => r.Uri.AbsolutePath));
    }

    [Theory]
    [InlineData("""{"zosmf_version":"1","zosmf_full_version":"1.1.0","zos_version":"MVS 3.8j"}""", "1.1.0")]
    [InlineData("""{"zosmf_version":"1.0.0-dev","zos_version":"MVS 3.8j"}""", "1.0.0-dev")]
    [InlineData("""{"zosmf_version":"1","zosmf_full_version":"  ","zos_version":"MVS 3.8j"}""", "1")]
    [InlineData("""{"zos_version":"MVS 3.8j"}""", "unknown")]
    public async Task Info_version_fields_prefer_the_full_version(string body, string expected)
    {
        using var service = new MvsmfFileService(new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, body), Base,
            Providing([], new HostCredentials("MVSCE02", "pw")));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, info.ProductVersion);
        Assert.Equal("MVS 3.8j", info.SystemVersion);
    }

    [Fact]
    public async Task A_refused_connection_is_unreachable()
    {
        // The first request a service makes is the sign-in, so a refused connection is reported against it.
        var handler = new RecordedHandler().Then((_, _) => throw new HttpRequestException("Connection refused (mvs.test:8080)"));
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal("Sign-in: cannot reach the host (Connection refused (mvs.test:8080)).", ex.Message);
    }

    [Fact]
    public async Task An_unreadable_answer_is_a_server_error()
    {
        var handler = new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, "not json");
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("Server information: the host's answer could not be read.", ex.Message);
    }

    [Fact]
    public void The_production_client_never_times_out_a_whole_transfer()
    {
        using var service = new MvsmfFileService(new MvsmfOptions(Base), Providing([], new HostCredentials("U", "p")));
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
        var handler = new RecordedHandler().Then("login-200").Then("info-200");
        HostTokenProvider slow = async (_, signIn, ct) =>
        {
            await Task.Delay(300, ct);
            return await signIn(new HostCredentials("MVSCE02", "pw"), ct);
        };
        using var service = new MvsmfFileService(handler, Base, slow, idleTimeout: TimeSpan.FromMilliseconds(100));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1.1.0", info.ProductVersion);
    }

    [Fact]
    public async Task A_slow_retry_prompt_is_not_a_host_timeout()
    {
        var handler = new RecordedHandler().Then("login-200").Then("info-401").Then("login-200").Then("info-200");
        HostTokenProvider slow = async (request, signIn, ct) =>
        {
            if (request.Rejected is not null) await Task.Delay(300, ct);
            return await signIn(new HostCredentials("MVSCE02", "pw"), ct);
        };
        using var service = new MvsmfFileService(handler, Base, slow, idleTimeout: TimeSpan.FromMilliseconds(100));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("1.1.0", info.ProductVersion);
    }

    [Fact]
    public async Task A_connect_timeout_is_unreachable()
    {
        var handler = new RecordedHandler().Then((_, _) => throw new TaskCanceledException("The operation was canceled.", new TimeoutException()));
        using var service = new MvsmfFileService(handler, Base, Providing([], new HostCredentials("U", "p")));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal("Sign-in: cannot reach the host (no answer within 10 s).", ex.Message);
    }
}
