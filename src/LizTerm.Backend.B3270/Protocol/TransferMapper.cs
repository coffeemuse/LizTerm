// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Protocol;

/// <summary>Turns a Core request into b3270's Transfer(keyword=value,...) action. Keywords b3270 would reject for
/// the direction, mode, or host type are omitted rather than passed through, and keywords its IND$FILE command
/// builder would ignore (LRECL and BLKSIZE without a RECFM, SPACE without units) are omitted so the wire log
/// stays honest. Values are raw strings; RunOperation quotes them as JSON, so spaces need no escaping.</summary>
public static class TransferMapper
{
    public static readonly B3270Action CancelAction = new("Transfer", "Cancel");

    public static B3270Action ToAction(FileTransferRequest request)
    {
        var send = request.Direction == TransferDirection.Send;
        var text = request.Mode == TransferMode.Text;
        var tso = request.HostType == TransferHostType.Tso;
        var vm = request.HostType == TransferHostType.Vm;

        var args = new List<string>
        {
            "direction=" + (send ? "send" : "receive"),
            "hostfile=" + request.HostFile,
            "localfile=" + request.LocalPath,
            "host=" + HostKeyword(request.HostType),
            "mode=" + (text ? "ascii" : "binary"),
        };

        if (text)
        {
            args.Add("cr=" + (request.CrLf ? (send ? "remove" : "add") : "keep"));
            args.Add("remap=" + (request.Remap ? "yes" : "no"));
        }

        if (request.Append) args.Add("exist=append");
        else if (!send) args.Add("exist=replace");

        if (send && (tso || vm))
        {
            var recfm = RecordFormatKeyword(request.RecordFormat, tso);
            if (recfm is not null)
            {
                args.Add("recfm=" + recfm);
                if (request.Lrecl is { } lrecl) args.Add("lrecl=" + lrecl);
                if (tso && request.Blksize is { } blksize) args.Add("blksize=" + blksize);
            }
            if (tso && request.AllocationUnits != AllocationUnits.Default)
            {
                args.Add("allocation=" + UnitsKeyword(request.AllocationUnits));
                if (request.PrimarySpace is { } primary) args.Add("primaryspace=" + primary);
                if (request.SecondarySpace is { } secondary) args.Add("secondaryspace=" + secondary);
                if (request.AllocationUnits == AllocationUnits.AvBlock && request.AverageBlock is { } avblock) args.Add("avblock=" + avblock);
            }
        }

        if (request.BufferSize is { } buffer) args.Add("buffersize=" + buffer);
        if (!string.IsNullOrWhiteSpace(request.ExtraOptions)) args.Add("otheroptions=" + request.ExtraOptions.Trim());

        return new B3270Action("Transfer", args.ToArray());
    }

    private static string HostKeyword(TransferHostType type) => type switch
    {
        TransferHostType.Tso => "tso",
        TransferHostType.Vm => "vm",
        _ => "cics",
    };

    /// <summary>Null for Default, and for Undefined on VM, which has no undefined-length records.</summary>
    private static string? RecordFormatKeyword(RecordFormat format, bool tso) => format switch
    {
        RecordFormat.Fixed => "fixed",
        RecordFormat.Variable => "variable",
        RecordFormat.Undefined when tso => "undefined",
        _ => null,
    };

    private static string UnitsKeyword(AllocationUnits units) => units switch
    {
        AllocationUnits.Tracks => "tracks",
        AllocationUnits.Cylinders => "cylinders",
        _ => "avblock",
    };
}
