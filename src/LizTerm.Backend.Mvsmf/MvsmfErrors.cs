// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text.Json;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf;

/// <summary>Turns an mvsMF failure into a <see cref="HostFileException"/>. mvsMF uses 500 for a refused open and
/// for a truncated write, so the JSON body's category and reason decide before the HTTP status does, and a server
/// error repeats the host's message.</summary>
internal static class MvsmfErrors
{
    private const int DatasetCategory = 6;
    private const int SecurityCategory = 4;

    public static HostFileException FromResponse(HttpStatusCode status, byte[] body, string what)
    {
        var error = TryParse(body);
        var kind = Classify(status, error?.Category, error?.Rc, error?.Reason);
        return new HostFileException(kind, Describe(kind, status, what, error), error?.Reason, error?.Message);
    }

    internal static HostFileErrorKind Classify(HttpStatusCode status, int? category, int? rc, int? reason)
    {
        if (status == HttpStatusCode.Unauthorized) return HostFileErrorKind.Unauthenticated;
        if (category == DatasetCategory && reason is 4 or 5) return HostFileErrorKind.NotFound;
        if (status == HttpStatusCode.NotFound) return HostFileErrorKind.NotFound;
        // mvsMF-compat: authorization-is-500 — a refused open is 500, category 4, rc 8, reason 0 ("LMOPEN error").
        if (category == SecurityCategory && rc == 8 && reason == 0) return HostFileErrorKind.NotAuthorized;
        if (status == HttpStatusCode.Forbidden) return HostFileErrorKind.NotAuthorized;
        if (status == HttpStatusCode.BadRequest) return HostFileErrorKind.InvalidRequest;
        return HostFileErrorKind.ServerError;
    }

    private static string Describe(HostFileErrorKind kind, HttpStatusCode status, string what, MvsmfError? error) => kind switch
    {
        HostFileErrorKind.Unauthenticated => "The host rejected the userid or password.",
        HostFileErrorKind.NotFound => $"{what}: not found.",
        HostFileErrorKind.NotAuthorized => $"{what}: not authorized.",
        HostFileErrorKind.InvalidRequest => $"{what}: the host refused the request ({error?.Message ?? "bad request"}).",
        _ => error switch
        {
            { Reason: { } reason, Message: { } message } when !string.IsNullOrWhiteSpace(message)
                => $"{what}: {message.Trim().TrimEnd('.')} (reason {reason}).",
            { Reason: { } reason } => $"{what}: server error (reason {reason}).",
            _ => $"{what}: server error (HTTP {(int)status}).",
        },
    };

    private static MvsmfError? TryParse(byte[] body)
    {
        if (body.Length == 0) return null;
        try
        {
            return JsonSerializer.Deserialize(body, MvsmfJsonContext.Default.MvsmfError);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
