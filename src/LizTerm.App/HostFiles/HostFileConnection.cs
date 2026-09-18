// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.HostFiles;

/// <summary>One browser window's service (spec §3.4). An operation refused for an untrusted certificate asks the
/// certificate prompt once; Connect Anyway trusts that certificate for the session (Remember also stores it) and the
/// operation runs once more. Refusals that arrive together share one prompt, and a certificate another window of
/// the same session accepted is picked up without asking.</summary>
public sealed class HostFileConnection : IDisposable
{
    private const string NotTrusted = "The mvsMF host's certificate is not trusted.";

    private readonly HostFileAccess _access;
    private readonly Uri _url;
    private readonly HostTokenProvider _tokens;
    private readonly ICertificatePrompt? _certificates;
    private readonly SemaphoreSlim _trust = new(1, 1);
    private readonly object _lock = new();
    private readonly List<IHostFileService> _made = [];
    private IHostFileService _service;
    private CertificatePin? _servicePin;
    /// <summary>The service whose refusal the user declined, so the refusals already in flight with it are not asked
    /// again. A decline replaces the service, so later operations are asked afresh.</summary>
    private IHostFileService? _declined;
    private bool _disposed;

    internal HostFileConnection(HostFileAccess access, Uri url, HostTokenProvider tokens, ICertificatePrompt? certificates)
    {
        _access = access;
        _url = url;
        _tokens = tokens;
        _certificates = certificates;
        _servicePin = access.Pin;
        _service = access.CreateService(url, _servicePin, tokens);
        _made.Add(_service);
    }

    public async Task<T> RunAsync<T>(Func<IHostFileService, Task<T>> operation)
    {
        var service = Current();
        try
        {
            return await operation(service);
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.CertificateRejected && ex.Certificate is { } presented)
        {
            if (!await TrustAsync(presented, service)) throw;
            return await operation(Current());
        }
    }

    public Task RunAsync(Func<IHostFileService, Task> operation) => RunAsync(async service =>
    {
        await operation(service);
        return true;
    });

    public void Dispose()
    {
        IHostFileService[] made;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            made = [.. _made];
            _made.Clear();
        }
        foreach (var service in made) service.Dispose();
    }

    /// <summary>The service for the session's current pin, remade when another window changed it. A replaced
    /// service is kept until Dispose, since an operation may still be using it.</summary>
    private IHostFileService Current()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var pin = _access.Pin;
            if (!Equals(pin, _servicePin)) Replace(pin);
            return _service;
        }
    }

    private void Replace(CertificatePin? pin)
    {
        _servicePin = pin;
        _service = _access.CreateService(_url, pin, _tokens);
        _made.Add(_service);
    }

    private async Task<bool> TrustAsync(PresentedCertificate presented, IHostFileService refused)
    {
        await _trust.WaitAsync();
        try
        {
            lock (_lock)
            {
                // Another window accepted a pin since: retry, and Current() picks it up.
                if (!Equals(_access.Pin, _servicePin)) return true;
                // Declined already, for an operation that was in flight at the time.
                if (ReferenceEquals(_declined, refused)) return false;
                // Accepted already, for an operation that was refused at the same time.
                if (!ReferenceEquals(_service, refused)) return true;
                // The pin in force already trusts exactly this certificate, so the refusal is not about trust (an
                // expired certificate, say) and asking again cannot help.
                if (_servicePin is { } inForce && CertificateReader.SameFingerprint(inForce.Sha256, presented.Sha256)) return false;
            }
            if (_certificates is null) return false;
            var canPin = _access.CanRememberPin && presented.Pinnable;
            var cannotPin = _access.CanRememberPin && !presented.Pinnable
                ? $"This certificate cannot be pinned: {presented.NotPinnableReason ?? "it cannot be verified on its own"}. Connect Anyway applies to this session only."
                : null;
            var decision = await _certificates.AskAsync(new CertificatePromptRequest(
                _url.Host, [NotTrusted], presented, null, _access.Pin, canPin, cannotPin));
            if (!decision.ConnectAnyway)
            {
                lock (_lock)
                {
                    _declined = refused;
                    if (!_disposed) Replace(_servicePin);
                }
                return false;
            }
            var pin = new CertificatePin(presented.Sha256, presented.Subject, presented.Pem);
            _access.AcceptPin(pin, canPin && decision.Remember);
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                Replace(pin);
            }
            return true;
        }
        finally
        {
            _trust.Release();
        }
    }
}
