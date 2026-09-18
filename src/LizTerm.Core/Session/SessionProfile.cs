// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;

namespace LizTerm.Core.Session;

/// <summary>A saved connection. Positional, with a default on every parameter, on purpose: the System.Text.Json
/// source generator that <c>ProfileJsonContext</c> uses honours constructor defaults for a field missing from a
/// file, where it ignores the initializer of an init-only property and reads the CLR default instead (verified on
/// .NET 10). Object initializers and <c>with</c> expressions work as before, and the properties stay init-only, so
/// the one instance a session, the picker, and the store share cannot be changed under any of them.</summary>
/// <param name="PinnedCertificate">The certificate trusted for this host, or null for the engine's default trust.
/// Independent of <paramref name="VerifyCertificate"/>: a pinned profile verifies, against the pin only;
/// verification off ignores the pin without removing it (spec 3.1).</param>
/// <param name="Model">3278/3279 model number, 2 through 5.</param>
/// <param name="Display">Colour (a 3279, the default) or mono (a 3278). Written by name, like the settings enums, so a
/// reordering of <see cref="TerminalDisplay"/> can never change a saved meaning. Independent of
/// <paramref name="Extended"/>: <c>3278-n-E</c> is a valid b3270 model.</param>
/// <param name="DestructiveBackspace">When true (the default, as in x3270's and wc3270's own base keymaps and Vista
/// TN3270), the Backspace key erases the character to the left of the cursor (x3270's Erase action); when false it
/// only moves the cursor left (BackSpace). The editor writes the field explicitly, so a saved choice survives.</param>
/// <param name="KeepAliveSeconds">Seconds between TELNET NOPs (b3270's <c>nopSeconds</c>); 0 is off. On by default,
/// and that default is retroactive: a profile file written before this field existed reads 60 here and gains the
/// keep-alive without being rewritten. It keeps the network connection open and does NOT prevent a host idle
/// logoff — a NOP is not 3270 data, so TSO's own timer never sees it.</param>
/// <param name="AutoReconnect">Whether b3270's <c>reconnect</c> is armed after a connect succeeds, so a session the
/// host drops comes back. Off by default: a reconnect re-establishes the host session rather than just the socket,
/// so an LU can move.</param>
/// <param name="Oversize">An oversize geometry as <c>columns x rows</c>, or null for the model's own. Validated by
/// <see cref="OversizeGeometry"/>, whose order is the opposite of <see cref="TerminalModel.ToString"/>'s.</param>
/// <param name="Tags">The tag names this profile carries, or none. Names only: a tag's COLOUR belongs to its
/// definition in <c>TagRegistry</c>, so <c>PROD</c> is one colour everywhere rather than one per profile. A
/// <see cref="TagSet"/> rather than a list because this is a record, and a record compares a collection member
/// by reference — see TagSet's own remarks. <c>FAVORITE</c> is an ordinary member here and is drawn as a gold
/// star rather than a chip.</param>
/// <param name="Note">A short line the name cannot carry — "no live data", "LAN only" — shown under the host in
/// the session list, or null. Capped and single-line at the editor, so a pasted paragraph cannot reshape the
/// list.</param>
/// <param name="HostFilesUrl">The z/OSMF REST base URL (normalised, e.g. <c>http://host:8080/zosmf</c>) that opens the
/// dataset browser for this profile, or null for none.</param>
/// <param name="HostFilesUserid">The userid the REST sign-in prompt starts with, or null. Never a password: the
/// password is asked for at the prompt, used once to sign in, and dropped; only the session token it buys is held
/// in memory, and only until the session window closes.</param>
/// <param name="HostFilesPinnedCertificate">The certificate trusted for <paramref name="HostFilesUrl"/> when it is https,
/// independent of <paramref name="PinnedCertificate"/>, which belongs to the 3270 host and port.</param>
public sealed record SessionProfile(
    string Name = "",
    string Host = "",
    int Port = 23,
    bool UseTls = false,
    bool VerifyCertificate = true,
    CertificatePin? PinnedCertificate = null,
    int Model = 2,
    bool Extended = true,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TerminalDisplay>))] TerminalDisplay Display = TerminalDisplay.Color,
    string CodePage = "cp037",
    string? LuName = null,
    bool DestructiveBackspace = true,
    int KeepAliveSeconds = 60,
    bool AutoReconnect = false,
    string? Oversize = null,
    [property: JsonConverter(typeof(TagSetJsonConverter))] TagSet Tags = default,
    string? Note = null,
    string? HostFilesUrl = null,
    string? HostFilesUserid = null,
    CertificatePin? HostFilesPinnedCertificate = null);
