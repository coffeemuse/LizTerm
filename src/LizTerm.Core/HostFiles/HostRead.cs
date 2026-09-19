// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>A text read: the records as lines, and the host's stamp of the content (<paramref name="Etag"/>), null
/// when it was not asked for or the host sent none. The stamp is opaque: it is only ever handed back as the
/// <c>ifMatch</c> of a later write, so a write happens only if the content is still what was read.</summary>
public sealed record HostTextRead(IReadOnlyList<string> Lines, string? Etag);

/// <summary>A binary read: the bytes copied, and the host's stamp, as for <see cref="HostTextRead"/>.</summary>
public sealed record HostBinaryRead(long Bytes, string? Etag);
