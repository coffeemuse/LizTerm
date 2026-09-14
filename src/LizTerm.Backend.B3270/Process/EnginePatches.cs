// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

namespace LizTerm.Backend.B3270.Process;

/// <summary>What LizTerm's own b3270 patches (<c>native/patches</c>) add, and how to tell whether an engine binary
/// carries them. The marker is the string <c>native/build/shared-verify-patches.sh</c> checks every built engine for;
/// <c>EnginePatchesTests</c> holds the two to the same name.</summary>
internal static class EnginePatches
{
    /// <summary>The Transfer keyword <c>b3270-transfer-commandprefix.patch</c> adds. A stock 4.5ga6 b3270 does not
    /// contain the string at all.</summary>
    public const string CommandPrefixMarker = "CommandPrefix";

    /// <summary>What an ISPF transfer fails with on an engine without that patch. Such an engine cannot be relied on to
    /// refuse the keyword: 4.5ga6 silently ignores a Transfer keyword it does not know, so it would type a bare
    /// IND$FILE into the ISPF command line and time out.</summary>
    public const string MissingCommandPrefixMessage =
        "This engine can't transfer from ISPF: it wasn't built with LizTerm's patch. The engine that ships with LizTerm can.";

    /// <summary>Whether the file's bytes contain the marker's ASCII bytes. Throws whatever reading the file throws.</summary>
    public static bool Carries(string path, string marker)
    {
        ReadOnlySpan<byte> needle = Encoding.ASCII.GetBytes(marker);
        return File.ReadAllBytes(path).AsSpan().IndexOf(needle) >= 0;
    }
}
