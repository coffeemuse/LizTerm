// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests.Protocol;

/// <summary>Pins the Transfer() argument list exactly. The omission rules mirror what b3270 rejects (cr and remap
/// in binary mode, allocation keywords on receive or on the wrong host type) and what its IND$FILE command
/// builder ignores (LRECL and BLKSIZE without a RECFM, SPACE without units).</summary>
public class TransferMapperTests
{
    private static FileTransferRequest Send(TransferHostType host = TransferHostType.Tso) =>
        new() { Direction = TransferDirection.Send, LocalPath = "/tmp/job.jcl", HostFile = "LIZTERM.JCL(JOB1)", HostType = host };

    private static FileTransferRequest Receive(TransferHostType host = TransferHostType.Tso) =>
        new() { Direction = TransferDirection.Receive, LocalPath = "/tmp/out.txt", HostFile = "LIZTERM.ITEST", HostType = host };

    private static string[] Args(FileTransferRequest request)
    {
        var action = TransferMapper.ToAction(request);
        Assert.Equal("Transfer", action.Name);
        return action.Args;
    }

    private static readonly string[] SendHead = ["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=tso", "mode=ascii", "cr=remove", "remap=yes"];

    [Fact]
    public void Send_text_defaults() => Assert.Equal(SendHead, Args(Send()));

    [Fact]
    public void Receive_text_defaults_add_newlines_and_replace_the_local_file() =>
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "mode=ascii", "cr=add", "remap=yes", "exist=replace"], Args(Receive()));

    [Fact]
    public void Crlf_off_keeps_newlines_in_both_directions()
    {
        Assert.Contains("cr=keep", Args(Send() with { CrLf = false }));
        Assert.Contains("cr=keep", Args(Receive() with { CrLf = false }));
    }

    [Fact]
    public void Remap_off() => Assert.Contains("remap=no", Args(Send() with { Remap = false }));

    [Fact]
    public void Binary_omits_cr_and_remap_in_both_directions()
    {
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=tso", "mode=binary"], Args(Send() with { Mode = TransferMode.Binary }));
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "mode=binary", "exist=replace"], Args(Receive() with { Mode = TransferMode.Binary }));
    }

    [Fact]
    public void Append_in_both_directions()
    {
        Assert.Equal("exist=append", Args(Send() with { Append = true })[^1]);
        var receive = Args(Receive() with { Append = true });
        Assert.Contains("exist=append", receive);
        Assert.DoesNotContain("exist=replace", receive);
    }

    [Fact]
    public void Tso_send_with_record_format_and_tracks()
    {
        var r = Send() with { RecordFormat = RecordFormat.Fixed, Lrecl = 80, Blksize = 3120, AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5, SecondarySpace = 2 };
        Assert.Equal([.. SendHead, "recfm=fixed", "lrecl=80", "blksize=3120", "allocation=tracks", "primaryspace=5", "secondaryspace=2"], Args(r));
    }

    [Fact]
    public void Undefined_record_format_and_cylinders()
    {
        var r = Send() with { RecordFormat = RecordFormat.Undefined, AllocationUnits = AllocationUnits.Cylinders, PrimarySpace = 1 };
        Assert.Equal(["recfm=undefined", "allocation=cylinders", "primaryspace=1"], Args(r)[7..]);
    }

    [Fact]
    public void Avblock_carries_the_average_block()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 100, AverageBlock = 4096 };
        Assert.Equal(["allocation=avblock", "primaryspace=100", "avblock=4096"], Args(r)[7..]);
    }

    [Fact]
    public void Average_block_is_dropped_for_other_units()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.Cylinders, PrimarySpace = 1, AverageBlock = 4096 };
        Assert.Equal(["allocation=cylinders", "primaryspace=1"], Args(r)[7..]);
    }

    [Fact]
    public void Lrecl_and_blksize_need_a_record_format() =>
        Assert.Equal(SendHead, Args(Send() with { Lrecl = 80, Blksize = 3120 }));

    [Fact]
    public void Space_fields_need_allocation_units() =>
        Assert.Equal(SendHead, Args(Send() with { PrimarySpace = 5, SecondarySpace = 1, AverageBlock = 4096 }));

    [Fact]
    public void Vm_send_takes_record_format_and_lrecl_only()
    {
        var r = Send(TransferHostType.Vm) with { RecordFormat = RecordFormat.Variable, Lrecl = 255, Blksize = 3120, AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5 };
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=vm", "mode=ascii", "cr=remove", "remap=yes", "recfm=variable", "lrecl=255"], Args(r));
    }

    [Fact]
    public void Vm_omits_the_undefined_record_format_and_its_lrecl()
    {
        var r = Send(TransferHostType.Vm) with { RecordFormat = RecordFormat.Undefined, Lrecl = 80 };
        Assert.Equal(7, Args(r).Length);
        Assert.Equal("host=vm", Args(r)[3]);
    }

    [Fact]
    public void Cics_send_has_no_allocation_keywords()
    {
        var r = Send(TransferHostType.Cics) with { RecordFormat = RecordFormat.Fixed, Lrecl = 80, AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5 };
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=cics", "mode=ascii", "cr=remove", "remap=yes"], Args(r));
    }

    [Fact]
    public void Receive_ignores_every_allocation_field()
    {
        var r = Receive() with { RecordFormat = RecordFormat.Fixed, Lrecl = 80, Blksize = 3120, AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5, SecondarySpace = 1, AverageBlock = 4096 };
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "mode=ascii", "cr=add", "remap=yes", "exist=replace"], Args(r));
    }

    [Fact]
    public void Buffer_size_and_extra_options_come_last()
    {
        var args = Args(Receive() with { BufferSize = 8192, ExtraOptions = "  NOTRUNC " });
        Assert.Equal(["buffersize=8192", "otheroptions=NOTRUNC"], args[^2..]);
        Assert.DoesNotContain(Args(Receive() with { ExtraOptions = "   " }), a => a.StartsWith("otheroptions", StringComparison.Ordinal));
    }

    [Fact]
    public void Paths_and_vm_names_with_spaces_pass_through_unchanged()
    {
        var r = Send(TransferHostType.Vm) with { LocalPath = "/Users/me/My Files/profile exec", HostFile = "PROFILE EXEC A" };
        var args = Args(r);
        Assert.Contains("localfile=/Users/me/My Files/profile exec", args);
        Assert.Contains("hostfile=PROFILE EXEC A", args);
        var json = RunOperation.Serialize("1", [TransferMapper.ToAction(r)]);
        Assert.Contains("\"hostfile=PROFILE EXEC A\"", json);
    }

    [Fact]
    public void Cancel_action()
    {
        Assert.Equal("Transfer", TransferMapper.CancelAction.Name);
        Assert.Equal(["Cancel"], TransferMapper.CancelAction.Args);
    }

    [Fact]
    public void Binary_tso_send_keeps_allocation_and_drops_cr_and_remap()
    {
        var args = Args(Send() with
        {
            Mode = TransferMode.Binary, RecordFormat = RecordFormat.Fixed, Lrecl = 80, Blksize = 3120,
            AllocationUnits = AllocationUnits.Tracks, PrimarySpace = 5, SecondarySpace = 1,
        });
        Assert.Contains("mode=binary", args);
        Assert.DoesNotContain(args, a => a.StartsWith("cr=") || a.StartsWith("remap="));
        Assert.Contains("recfm=fixed", args);
        Assert.Contains("lrecl=80", args);
        Assert.Contains("blksize=3120", args);
        Assert.Contains("allocation=tracks", args);
        Assert.Contains("primaryspace=5", args);
        Assert.Contains("secondaryspace=1", args);
    }

    [Fact]
    public void Ispf_is_tso_with_the_command_prefix() =>
        Assert.Equal(["direction=send", "hostfile=LIZTERM.JCL(JOB1)", "localfile=/tmp/job.jcl", "host=tso", "commandprefix=TSO", "mode=ascii", "cr=remove", "remap=yes"], Args(Send(TransferHostType.Ispf)));

    [Fact]
    public void An_ispf_receive_carries_the_prefix_too() =>
        Assert.Equal(["direction=receive", "hostfile=LIZTERM.ITEST", "localfile=/tmp/out.txt", "host=tso", "commandprefix=TSO", "mode=ascii", "cr=add", "remap=yes", "exist=replace"], Args(Receive(TransferHostType.Ispf)));

    [Fact]
    public void An_ispf_send_keeps_every_tso_only_keyword()
    {
        var r = Send(TransferHostType.Ispf) with { RecordFormat = RecordFormat.Undefined, Lrecl = 80, Blksize = 3120, AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5, SecondarySpace = 1, AverageBlock = 4096 };
        Assert.Equal(["recfm=undefined", "lrecl=80", "blksize=3120", "allocation=avblock", "primaryspace=5", "secondaryspace=1", "avblock=4096"], Args(r)[8..]);
    }

    [Theory]
    [InlineData(TransferHostType.Tso)]
    [InlineData(TransferHostType.Vm)]
    [InlineData(TransferHostType.Cics)]
    public void Only_ispf_sends_a_command_prefix(TransferHostType host)
    {
        Assert.DoesNotContain(Args(Send(host)), a => a.StartsWith("commandprefix=", StringComparison.Ordinal));
        Assert.DoesNotContain(Args(Receive(host)), a => a.StartsWith("commandprefix=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(TransferHostType.Tso, "host=tso")]
    [InlineData(TransferHostType.Vm, "host=vm")]
    [InlineData(TransferHostType.Cics, "host=cics")]
    [InlineData(TransferHostType.Ispf, "host=tso")]
    public void Every_host_type_has_a_keyword(TransferHostType host, string expected) => Assert.Contains(expected, Args(Send(host)));

    [Theory]
    [InlineData(RecordFormat.Fixed, "recfm=fixed")]
    [InlineData(RecordFormat.Variable, "recfm=variable")]
    [InlineData(RecordFormat.Undefined, "recfm=undefined")]
    public void Every_record_format_has_a_keyword_on_tso(RecordFormat format, string expected) =>
        Assert.Contains(expected, Args(Send() with { RecordFormat = format }));

    [Theory]
    [InlineData(AllocationUnits.Tracks, "allocation=tracks")]
    [InlineData(AllocationUnits.Cylinders, "allocation=cylinders")]
    [InlineData(AllocationUnits.AvBlock, "allocation=avblock")]
    public void Every_allocation_unit_has_a_keyword_on_tso(AllocationUnits units, string expected) =>
        Assert.Contains(expected, Args(Send() with { AllocationUnits = units, PrimarySpace = 1, AverageBlock = units == AllocationUnits.AvBlock ? 4096 : null }));

    [Fact]
    public void Default_record_format_and_units_emit_nothing()
    {
        var args = Args(Send());
        Assert.DoesNotContain(args, a => a.StartsWith("recfm=") || a.StartsWith("allocation="));
    }
}
