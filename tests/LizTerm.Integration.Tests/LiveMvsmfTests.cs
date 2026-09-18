// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.Mvsmf;
using LizTerm.Core.HostFiles;

namespace LizTerm.Integration.Tests;

/// <summary>Runs only when LIZTERM_MVSMF_URL, LIZTERM_MVSMF_USER, LIZTERM_MVSMF_PASSWORD and
/// LIZTERM_MVSMF_SCRATCH_PDS are all set. Writes, verifies and deletes the member LIZITEST in the scratch PDS.</summary>
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

    private static MvsmfFileService Connect(Live live) =>
        new(new MvsmfOptions(live.Url), (_, _) => ValueTask.FromResult<HostCredentials?>(live.Credentials));

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
    public async Task A_rejected_password_is_asked_for_again_then_fails()
    {
        var live = Require();
        var asked = new List<bool>();
        using var service = new MvsmfFileService(new MvsmfOptions(live.Url), (request, _) =>
        {
            asked.Add(request.IsRetry);
            return ValueTask.FromResult<HostCredentials?>(new HostCredentials(live.Credentials.Userid, "not-the-password"));
        });

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unauthenticated, ex.Kind);
        Assert.Equal(new[] { false, true }, asked);
    }
}
