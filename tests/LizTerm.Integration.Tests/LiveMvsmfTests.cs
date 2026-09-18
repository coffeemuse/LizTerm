// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.Mvsmf;
using LizTerm.Core.HostFiles;

namespace LizTerm.Integration.Tests;

/// <summary>Runs only when LIZTERM_MVSMF_URL, LIZTERM_MVSMF_USER, LIZTERM_MVSMF_PASSWORD and
/// LIZTERM_MVSMF_SCRATCH_PDS are all set. Writes, verifies and deletes the member LIZITEST in the scratch PDS. The
/// two <see cref="Connect"/> tests leave their one session to the host's idle timeout, as the browser does when the
/// app is killed.</summary>
public class LiveMvsmfTests
{
    private const int LiveTimeout = 120_000;
    private const string ScratchMember = "LIZITEST";

    private sealed record Live(Uri Url, HostCredentials Credentials, string ScratchPds);

    private static Live Require()
    {
        var url = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_URL");
        var user = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_USER");
        var password = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_PASSWORD");
        var scratch = Environment.GetEnvironmentVariable("LIZTERM_MVSMF_SCRATCH_PDS");
        Assert.SkipWhen(new[] { url, user, password, scratch }.Any(string.IsNullOrWhiteSpace),
            "LIZTERM_MVSMF_URL, LIZTERM_MVSMF_USER, LIZTERM_MVSMF_PASSWORD and LIZTERM_MVSMF_SCRATCH_PDS are not all set");
        Assert.True(MvsmfOptions.TryNormalizeBaseUrl(url, out var baseUrl, out var error), error);
        return new Live(baseUrl!, new HostCredentials(user!, password!), scratch!);
    }

    /// <summary>Signs in once and keeps the token, signing in again only when the host refuses the one it holds:
    /// the App holder's contract, without a prompt. <see cref="Asked"/> records each request's Rejected token.</summary>
    private sealed class Holding(HostCredentials credentials)
    {
        public HostSessionToken? Held { get; private set; }
        public List<HostSessionToken?> Asked { get; } = [];

        public HostTokenProvider Provider => async (request, signIn, ct) =>
        {
            Asked.Add(request.Rejected);
            if (Held is not null && !ReferenceEquals(request.Rejected, Held)) return Held;
            return Held = await signIn(credentials, ct);
        };
    }

    private static MvsmfFileService Connect(Live live) => new(new MvsmfOptions(live.Url), new Holding(live.Credentials).Provider);

    [Fact(Timeout = LiveTimeout)]
    public async Task Reports_the_server_and_lists_the_scratch_pds()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        using var service = Connect(live);

        var info = await service.GetServerInfoAsync(ct);
        var datasets = await service.ListDatasetsAsync(live.ScratchPds, ct);

        Assert.Equal("mvsMF", info.Product);
        Assert.False(string.IsNullOrWhiteSpace(info.ProductVersion));
        var scratch = Assert.Single(datasets, d => d.Name == live.ScratchPds.ToUpperInvariant());
        Assert.True(scratch.Attributes!.IsPartitioned, $"{live.ScratchPds} is {scratch.Attributes.Dsorg}, not a PDS");
    }

    [Fact(Timeout = LiveTimeout)]
    public async Task Round_trips_a_scratch_member_and_deletes_it()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        using var service = Connect(live);
        var target = (await service.ListDatasetsAsync(live.ScratchPds, ct)).Single(d => d.Name == live.ScratchPds.ToUpperInvariant()).Attributes!;
        var path = HostPath.ForMember(live.ScratchPds, ScratchMember);
        var local = Path.Combine(Path.GetTempPath(), $"lizitest-{Guid.NewGuid():N}.jcl");
        await File.WriteAllTextAsync(local, "//LIZITEST JOB (ACCT),LIZTERM\n\n//* ¬ ¢ | ~ end\n\tTABBED\n", ct);
        try
        {
            var checkedText = HostFileTransfer.CheckTextFile(local, target);
            Assert.True(checkedText.CanUpload, string.Join(" | ", checkedText.Errors.Select(e => e.Message)));

            var outcome = await HostFileTransfer.UploadTextAsync(service, path, checkedText, verify: true, ct);
            Assert.Equal(UploadOutcome.Matches, outcome);

            var members = await service.ListMembersAsync(HostPath.ForDataset(live.ScratchPds), ct);
            Assert.Contains(members, m => m.Name == ScratchMember);

            var back = Path.Combine(Path.GetTempPath(), $"lizitest-{Guid.NewGuid():N}.txt");
            try
            {
                await HostFileTransfer.DownloadAsync(service, path, back, new DownloadOptions(HostTransferMode.Text, LineEnding: "\n"), cancellationToken: ct);
                Assert.Equal("//LIZITEST JOB (ACCT),LIZTERM\n\n//* ¬ ¢ | ~ end\n        TABBED\n", await File.ReadAllTextAsync(back, ct));
            }
            finally
            {
                File.Delete(back);
            }

            await service.DeleteAsync(path, ct);
            var gone = await Assert.ThrowsAsync<HostFileException>(() => service.ReadTextAsync(path, cancellationToken: ct));
            Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
        }
        finally
        {
            File.Delete(local);
            try
            {
                await service.DeleteAsync(path, CancellationToken.None);
            }
            catch (HostFileException)
            {
                // Already gone, which is the expected case.
            }
        }
    }

    [Fact(Timeout = LiveTimeout)]
    public async Task A_rejected_password_fails_as_unauthenticated()
    {
        var live = Require();
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url),
            new Holding(new HostCredentials(live.Credentials.Userid, "not-the-password")).Provider);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
    }

    [Fact(Timeout = LiveTimeout)]
    public async Task Signs_in_lists_signs_out_and_the_dead_token_is_refused()
    {
        var live = Require();
        var ct = TestContext.Current.CancellationToken;
        var holding = new Holding(live.Credentials);
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url), holding.Provider);

        await service.ListDatasetsAsync(live.ScratchPds, ct);
        var first = holding.Held!;
        await service.SignOutAsync(first, ct); // 204
        await service.SignOutAsync(first, ct); // 401 for a token the host has forgotten: still fine

        await service.GetServerInfoAsync(ct);  // 401 on the dead token, then a fresh sign-in

        // Each of the two provider-driving calls asks once with no rejected token before it ever tries the
        // request: ListDatasetsAsync's ask (Held is still null) signs in, and GetServerInfoAsync's own leading
        // ask (Held is "first") is answered from the cache before the request comes back 401 and it asks again
        // naming "first" (MvsmfAuthTests.An_expired_token_signs_in_again_and_repeats_the_request_once pins the
        // same two-ask shape for a single call).
        Assert.Equal(new HostSessionToken?[] { null, null, first }, holding.Asked);
        Assert.NotSame(first, holding.Held);
        await service.SignOutAsync(holding.Held!, ct);
    }

    [Fact(Timeout = LiveTimeout)]
    public async Task Probe_reports_the_host_needs_sign_in()
    {
        var live = Require();
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url),
            (_, _, _) => ValueTask.FromResult<HostSessionToken?>(null));

        var info = await service.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.Null(info); // /zosmf/info needs auth on this build (compat: info-requires-auth)
    }
}
