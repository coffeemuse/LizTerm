// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App.HostFiles;

/// <summary>Builds the service for one URL and pin. <see cref="HostFileServiceFactory.Create"/> in the app; a fake in
/// tests.</summary>
public delegate IHostFileService HostFileServiceCreator(Uri baseUrl, CertificatePin? pin, HostCredentialProvider credentials);

/// <summary>A session window's REST side (spec §3.3): the URL from its profile, the sign-in, and the certificate the
/// user trusts for it. Lives as long as the session window; each browser window connects through it.</summary>
public sealed class HostFileAccess
{
    private readonly HostFileServiceCreator _create;
    private readonly Action<CertificatePin>? _savePin;
    private readonly object _lock = new();
    private CertificatePin? _pin;

    /// <param name="savePin">Stores a remembered pin in the profile file; null for a profile that has no file.</param>
    public HostFileAccess(SessionProfile profile, HostFileServiceCreator create, Action<CertificatePin>? savePin)
    {
        _create = create;
        _savePin = savePin;
        ProfileName = profile.Name;
        Userid = profile.HostFilesUserid;
        if (HostFileServiceFactory.TryNormalizeUrl(profile.HostFilesUrl, out var url, out var error)) Url = url;
        else UrlError = error;
        _pin = profile.HostFilesPinnedCertificate;
        Credentials = new CredentialHolder(profile.Name, Url?.ToString() ?? profile.HostFilesUrl ?? "", profile.HostFilesUserid);
    }

    public string ProfileName { get; }
    public string? Userid { get; }
    /// <summary>Null when the profile's URL is not usable; <see cref="UrlError"/> says why.</summary>
    public Uri? Url { get; }
    public string? UrlError { get; }
    public CredentialHolder Credentials { get; }
    public bool CanRememberPin => _savePin is not null;

    /// <summary>The certificate trusted for this session: the profile's pin, or one the user accepted since.</summary>
    public CertificatePin? Pin
    {
        get { lock (_lock) return _pin; }
    }

    /// <summary>A connection for one browser window, whose prompts are that window's.</summary>
    /// <exception cref="InvalidOperationException">The profile's URL is not usable.</exception>
    public HostFileConnection Connect(ICredentialPrompt credentials, ICertificatePrompt? certificates) =>
        Url is { } url
            ? new HostFileConnection(this, url, Credentials.ProviderFor(credentials), certificates)
            : throw new InvalidOperationException(UrlError);

    public void Forget() => Credentials.Forget();

    internal IHostFileService CreateService(Uri url, CertificatePin? pin, HostCredentialProvider credentials) =>
        _create(url, pin, credentials);

    internal void AcceptPin(CertificatePin pin, bool remember)
    {
        lock (_lock) _pin = pin;
        if (remember) _savePin?.Invoke(pin);
    }
}
