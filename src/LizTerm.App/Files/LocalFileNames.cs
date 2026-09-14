// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.Files;

/// <summary>Suggests a local file name for a received host file: the member name or last qualifier of a TSO
/// dataset (TSO and ISPF alike), FN.FT for a VM file, the name as typed for CICS. Case is preserved.</summary>
public static class LocalFileNames
{
    public const string Fallback = "received";

    public static string Suggest(string hostFile, TransferHostType hostType)
    {
        var name = hostFile.Trim().Trim('\'').Trim();
        if (name.Length == 0) return Fallback;
        switch (hostType)
        {
            case TransferHostType.Tso or TransferHostType.Ispf:
                var open = name.IndexOf('(');
                if (open >= 0)
                {
                    var close = name.IndexOf(')', open + 1);
                    var member = (close > open ? name[(open + 1)..close] : name[(open + 1)..]).Trim();
                    return member.Length == 0 ? Fallback : member;
                }
                var qualifier = name[(name.LastIndexOf('.') + 1)..].Trim();
                return qualifier.Length == 0 ? Fallback : qualifier;
            case TransferHostType.Vm:
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? parts[0] + "." + parts[1] : parts[0];
            default:
                return name;
        }
    }
}
