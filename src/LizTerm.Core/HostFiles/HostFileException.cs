// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Security;

namespace LizTerm.Core.HostFiles;

public enum HostFileErrorKind
{
    NotFound,
    /// <summary>The host could not open it: missing, not authorised, or in use. Some hosts cannot say which.</summary>
    CannotOpen,
    NotAuthorized,
    InvalidRequest,
    /// <summary>The credentials were rejected, or the user cancelled the prompt.</summary>
    Unauthenticated,
    /// <summary>The host is not an mvsMF that supports sign-in (no authenticate route); it is too old.</summary>
    Unsupported,
    /// <summary>TLS: the certificate is neither trusted nor pinned. <see cref="HostFileException.Certificate"/> says
    /// what was presented.</summary>
    CertificateRejected,
    ServerError,
    /// <summary>No connection, a dropped connection, or no data for too long.</summary>
    Unreachable,
}

/// <summary>A host outcome that is not success. <see cref="Exception.Message"/> is plain words fit to show;
/// <see cref="ServerMessage"/> is the host's own text, when it sent one.</summary>
public sealed class HostFileException(
    HostFileErrorKind kind,
    string message,
    int? reason = null,
    string? serverMessage = null,
    PresentedCertificate? certificate = null,
    Exception? inner = null) : Exception(message, inner)
{
    public HostFileErrorKind Kind { get; } = kind;

    public int? Reason { get; } = reason;

    public string? ServerMessage { get; } = serverMessage;

    public PresentedCertificate? Certificate { get; } = certificate;
}
