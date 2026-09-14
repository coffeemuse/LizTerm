// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LizTerm.Core.Updates;

internal sealed record GitHubReleaseResponse(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl);

[JsonSerializable(typeof(GitHubReleaseResponse))]
internal partial class GitHubReleaseJsonContext : JsonSerializerContext;

/// <summary>The app's first HTTP call of any kind. The headers are set on each request, so a test using a fake
/// handler sees exactly what production sends; the 10 s timeout is on the client CreateHttpClient builds, which no
/// handler can observe, so its test reads it off that client.</summary>
public sealed class GitHubReleaseChecker(HttpClient httpClient) : IReleaseChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/coffeemuse/LizTerm/releases/latest";
    private static readonly string ProductVersion = typeof(GitHubReleaseChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The production instance.</summary>
    public static IReleaseChecker Create() => new GitHubReleaseChecker(CreateHttpClient());

    /// <summary>A check with no route to the internet must give up rather than leave Help &gt; Check for Updates...
    /// showing nothing, hence the timeout.</summary>
    internal static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LizTerm", ProductVersion));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync(body, GitHubReleaseJsonContext.Default.GitHubReleaseResponse, cancellationToken)
            ?? throw new JsonException("The releases API returned an empty body.");
        if (release.TagName is null || release.HtmlUrl is null)
            throw new JsonException("The releases API returned no tag_name or html_url.");

        var version = release.TagName.StartsWith('v') ? release.TagName[1..] : release.TagName;
        return new ReleaseInfo(version, release.HtmlUrl);
    }
}
