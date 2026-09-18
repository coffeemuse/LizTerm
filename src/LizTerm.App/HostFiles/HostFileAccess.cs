// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App.HostFiles;

/// <summary>Builds the service for one URL and pin. <see cref="HostFileServiceFactory.Create"/> in the app; a fake in
/// tests.</summary>
public delegate IHostFileService HostFileServiceCreator(Uri baseUrl, CertificatePin? pin, HostTokenProvider tokens);

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
        SignIn = new SignInHolder(profile.Name, Url?.ToString() ?? profile.HostFilesUrl ?? "", profile.HostFilesUserid);
    }

    public string ProfileName { get; }
    public string? Userid { get; }
    /// <summary>Null when the profile's URL is not usable; <see cref="UrlError"/> says why.</summary>
    public Uri? Url { get; }
    public string? UrlError { get; }
    public SignInHolder SignIn { get; }
    public bool CanRememberPin => _savePin is not null;

    /// <summary>Remember was chosen but the profile file could not be written; the pin still holds for the
    /// session. Raised on the thread that accepted the pin, with a sentence for the user.</summary>
    public event EventHandler<string>? PinSaveFailed;

    /// <summary>The certificate trusted for this session: the profile's pin, or one the user accepted since.</summary>
    public CertificatePin? Pin
    {
        get { lock (_lock) return _pin; }
    }

    /// <summary>A connection for one browser window, whose prompts are that window's.</summary>
    /// <exception cref="InvalidOperationException">The profile's URL is not usable.</exception>
    public HostFileConnection Connect(ICredentialPrompt credentials, ICertificatePrompt? certificates) =>
        Url is { } url
            ? new HostFileConnection(this, url, SignIn.ProviderFor(credentials), certificates)
            : throw new InvalidOperationException(UrlError);

    /// <summary>Ends the session, through a service built for the URL, and drops the token. Best effort.</summary>
    /// <remarks>The token is dropped **here**, on the caller's thread, so <see cref="SignInHolder.IsSignedIn"/> is
    /// false the moment the session window closes. Only the DELETE goes to the pool, and it goes there because
    /// nothing in this codebase uses <c>ConfigureAwait(false)</c>: called from <c>OnClosed</c> on the UI thread, its
    /// continuations would be posted to a dispatcher that stops with the last window, and the response would never
    /// be read (<c>B3270Session.ConnectAsync</c> escapes the same trap the same way).</remarks>
    /// <param name="cancellationToken">Gives up on the DELETE before <see cref="SignInHolder.SignOutCap"/> does. The
    /// token is dropped either way; only the network half is cancelled, and it comes back as an
    /// <see cref="OperationCanceledException"/> for the caller to observe, since the backend and the cap swallow
    /// everything but the caller's own cancellation.</param>
    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        if (Url is not { } url || SignIn.Take() is not { } token) return Task.CompletedTask;
        return Task.Run(() => EndSessionAsync(url, token, cancellationToken));
    }

    /// <summary>The network half of <see cref="SignOutAsync"/>. <see cref="NullPrompt"/> can never be reached: the
    /// token is passed in, so the service never asks the provider for one.</summary>
    private async Task EndSessionAsync(Uri url, HostSessionToken token, CancellationToken cancellationToken)
    {
        using var service = CreateService(url, Pin, SignIn.ProviderFor(NullPrompt.Instance));
        await SignInHolder.EndAsync(service, token, cancellationToken);
    }

    internal IHostFileService CreateService(Uri url, CertificatePin? pin, HostTokenProvider tokens) =>
        _create(url, pin, tokens);

    internal void AcceptPin(CertificatePin pin, bool remember)
    {
        lock (_lock) _pin = pin;
        if (!remember || _savePin is null) return;
        try
        {
            _savePin(pin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The operation goes ahead on the pin accepted for the session; only remembering it failed.
            PinSaveFailed?.Invoke(this, $"Could not save the certificate to the profile: {ex.Message}");
        }
    }
}
