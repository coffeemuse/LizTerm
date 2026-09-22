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
    private const int AllocationCategory = 8;
    private const int AllocationRc = 900;
    private const int RenameTargetExistsReason = 7;

    public static HostFileException FromResponse(HttpStatusCode status, byte[] body, string what)
    {
        var error = TryParse(body);
        var kind = Classify(status, error?.Category, error?.Rc, error?.Reason, error?.Message);
        return new HostFileException(kind, Describe(kind, status, what, error), error?.Reason, error?.Message);
    }

    internal static HostFileErrorKind Classify(HttpStatusCode status, int? category, int? rc, int? reason, string? message = null)
    {
        if (status == HttpStatusCode.Unauthorized) return HostFileErrorKind.Unauthenticated;
        if (category == DatasetCategory && reason is 4 or 5) return HostFileErrorKind.NotFound;
        // mvsMF-compat: cannot-open-is-500 — an open that fails before any record is read or written is 500,
        // category 6, reason 3, the same shape as a write that failed part way; only the message tells them apart.
        if (category == DatasetCategory && reason == 3 && message?.StartsWith("Cannot open", StringComparison.OrdinalIgnoreCase) == true)
            return HostFileErrorKind.CannotOpen;
        if (status == HttpStatusCode.NotFound) return HostFileErrorKind.NotFound;
        // mvsMF-compat: authorization-is-500 — a refused open is 500, category 4, rc 8, reason 0 ("LMOPEN error").
        if (category == SecurityCategory && rc == 8 && reason == 0) return HostFileErrorKind.NotAuthorized;
        if (status == HttpStatusCode.Forbidden) return HostFileErrorKind.NotAuthorized;
        // mvsMF-compat: create-failure-is-one-500 — every allocation failure (the name exists, no space, a DCB the
        // volume cannot hold, no authority) is the same 500, category 8, rc 900, byte for byte what z/OSMF sends.
        if (category == AllocationCategory && rc == AllocationRc) return HostFileErrorKind.CannotAllocate;
        // mvsMF-compat: etag — a stale If-Match is 412 reason 10 and nothing is written.
        if (status == HttpStatusCode.PreconditionFailed) return HostFileErrorKind.Conflict;
        // mvsMF-compat: rename-target-exists-400 — a member rename onto an existing name is 400 reason 7.
        if (status == HttpStatusCode.BadRequest && category == DatasetCategory && reason == RenameTargetExistsReason)
            return HostFileErrorKind.AlreadyExists;
        // mvsMF-compat: uss-create-errors-400 — a file system create onto an existing name is 400, category 4 (the
        // security category, reused), reason 1, told from the route's other 400s only by its message.
        if (status == HttpStatusCode.BadRequest && message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true)
            return HostFileErrorKind.AlreadyExists;
        if (status == HttpStatusCode.BadRequest) return HostFileErrorKind.InvalidRequest;
        return HostFileErrorKind.ServerError;
    }

    private static string Describe(HostFileErrorKind kind, HttpStatusCode status, string what, MvsmfError? error) => kind switch
    {
        HostFileErrorKind.Unauthenticated => "The host rejected the userid or password.",
        HostFileErrorKind.NotFound => $"{what}: not found.",
        HostFileErrorKind.CannotOpen => $"{what}: {Quote(error?.Message) ?? "cannot be opened"}.",
        HostFileErrorKind.NotAuthorized => $"{what}: not authorized.",
        HostFileErrorKind.CannotAllocate => $"{what}: the host could not allocate it (it may already exist, there may be no space, or you may not be authorized).",
        HostFileErrorKind.Conflict => $"{what}: changed on the host since it was read.",
        HostFileErrorKind.AlreadyExists => error?.Category == SecurityCategory
            ? $"{what}: a file or directory of that name already exists."
            : $"{what}: a member of that name already exists.",
        HostFileErrorKind.InvalidRequest => $"{what}: the host refused the request ({error?.Message ?? "bad request"}).",
        _ => (Quote(error?.Message), error?.Reason) switch
        {
            ({ } message, { } reason) => $"{what}: {message} (reason {reason}).",
            ({ } message, null) => $"{what}: {message} (HTTP {(int)status}).",
            (null, { } reason) => $"{what}: server error (reason {reason}).",
            _ => $"{what}: server error (HTTP {(int)status}).",
        },
    };

    /// <summary>The host's message without surrounding blanks or a final full stop; null when it says nothing.</summary>
    private static string? Quote(string? message) =>
        message?.Trim().TrimEnd('.').TrimEnd() is { Length: > 0 } text ? text : null;

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
