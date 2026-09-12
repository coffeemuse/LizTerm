// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.B3270.Protocol;

/// <summary>A message from b3270 to the UI. Names and fields follow include/b3270proto.h in x3270 4.5.</summary>
public abstract record Indication;

public sealed record InitializeIndication(IReadOnlyList<Indication> Items) : Indication;
public sealed record HelloIndication(string Version, string Build) : Indication;
public sealed record ScreenModeIndication(int Model, int Rows, int Columns, bool Color, bool Oversize, bool Extended) : Indication;
public sealed record EraseIndication(int? LogicalRows, int? LogicalColumns, string? Fg, string? Bg) : Indication;
public sealed record CursorIndication(bool Enabled, int? Row, int? Column);
/// <summary>One run of cells starting at Column (1-based). Text runs carry Text; attribute-only runs carry Count.</summary>
public sealed record ChangeIndication(int Column, string? Text, int? Count, string? Fg, string? Bg, string? Gr);
public sealed record RowIndication(int Row, IReadOnlyList<ChangeIndication> Changes);
public sealed record ScreenIndication(CursorIndication? Cursor, IReadOnlyList<RowIndication>? Rows) : Indication;
/// <summary>Value is normalized to a string ("true"/"false" for booleans); null means the field is cleared.</summary>
public sealed record OiaIndication(string Field, string? Value) : Indication;
public sealed record ConnectionIndication(string State, string? Host, string? Cause) : Indication;
public sealed record TlsIndication(bool Secure, bool? Verified, string? Session, string? HostCert) : Indication;
/// <summary>The engine's own statement of its TLS capability, sent once inside the startup <c>initialize</c>
/// block (spec: plan 3d task 8). Options is the list of Set() toggle names this engine actually accepts for
/// TLS, exactly as it reported them — a Schannel engine's list is far smaller than an OpenSSL one's and does
/// not include "caFile". Empty (never null) when TLS is not supported at all, or when "options" is missing or
/// malformed.</summary>
public sealed record TlsHelloIndication(bool Supported, string? Provider, IReadOnlyList<string> Options) : Indication;
public sealed record RunResultIndication(string? Tag, bool Success, IReadOnlyList<string> Text, IReadOnlyList<bool> TextErr, bool Abort) : Indication;
public sealed record PopupIndication(string Type, string Text, bool Retrying, bool Error) : Indication;
/// <summary>The host rang the 3270 alarm. b3270 sends an empty body; there is nothing to carry.</summary>
public sealed record BellIndication : Indication;
public sealed record UiErrorIndication(bool Fatal, string Text, string? Operation, string? Member) : Indication;
public sealed record FtIndication(string State, bool? Success, string? Text, long? Bytes, string? Cause) : Indication;
public sealed record SettingIndication(string Name, string? Value, string? Cause) : Indication;
public sealed record UnknownIndication(string Name) : Indication;
