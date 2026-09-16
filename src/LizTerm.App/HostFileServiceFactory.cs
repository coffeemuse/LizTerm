// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.HostFiles;
using LizTerm.Backend.Mvsmf;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App;

/// <summary>A one-shot connection check for the profile editor: signs in through a prompt, asks the host what it is,
/// and forgets the sign-in.</summary>
public delegate Task<HostServerInfo> HostFileTester(string profileName, Uri url, string? userid, CertificatePin? pin, CancellationToken cancellationToken);

/// <summary>The only place the app names the mvsMF backend, as <see cref="SessionFactory"/> is for b3270. Everything
/// else in App talks to <see cref="IHostFileService"/>.</summary>
public static class HostFileServiceFactory
{
    /// <summary>Reads a URL as typed: <c>/zosmf</c> added to an empty path, credentials and queries refused.</summary>
    public static bool TryNormalizeUrl(string? text, out Uri? url, out string? error) =>
        MvsmfOptions.TryNormalizeBaseUrl(text, out url, out error);

    /// <exception cref="ArgumentException">The URL is not a usable base (see <see cref="TryNormalizeUrl"/>).</exception>
    public static IHostFileService Create(Uri baseUrl, CertificatePin? pin, HostCredentialProvider credentials) =>
        new MvsmfFileService(new MvsmfOptions(baseUrl, pin), credentials);

    public static HostFileTester CreateTester(ICredentialPrompt prompt) => async (profileName, url, userid, pin, token) =>
    {
        var holder = new CredentialHolder(profileName, url.ToString(), userid);
        using var service = Create(url, pin, holder.ProviderFor(prompt));
        return await service.GetServerInfoAsync(token);
    };
}
