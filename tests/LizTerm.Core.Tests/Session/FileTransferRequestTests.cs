// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class FileTransferRequestTests
{
    private static FileTransferRequest Send() =>
        new() { Direction = TransferDirection.Send, LocalPath = "/nonexistent/a.txt", HostFile = "A.B" };

    [Fact]
    public void Defaults_match_spec()
    {
        var r = Send();
        Assert.Equal(TransferHostType.Tso, r.HostType);
        Assert.Equal(TransferMode.Text, r.Mode);
        Assert.True(r.CrLf);
        Assert.True(r.Remap);
        Assert.False(r.Append);
        Assert.Equal(RecordFormat.Default, r.RecordFormat);
        Assert.Equal(AllocationUnits.Default, r.AllocationUnits);
        Assert.Null(r.Lrecl);
        Assert.Null(r.Blksize);
        Assert.Null(r.PrimarySpace);
        Assert.Null(r.SecondarySpace);
        Assert.Null(r.AverageBlock);
        Assert.Null(r.BufferSize);
        Assert.Null(r.ExtraOptions);
        Assert.Null(r.Validate());
    }

    [Fact]
    public void Blank_local_path_is_reported_before_a_blank_host_file() =>
        Assert.Equal("Choose a local file.", (Send() with { LocalPath = " ", HostFile = "" }).Validate());

    [Fact]
    public void Blank_host_file_is_rejected() =>
        Assert.Equal("Enter the host file name.", (Send() with { HostFile = " " }).Validate());

    [Fact]
    public void Non_positive_numbers_are_rejected_with_the_field_name()
    {
        Assert.Equal("LRECL must be a positive number.", (Send() with { Lrecl = 0 }).Validate());
        Assert.Equal("BLKSIZE must be a positive number.", (Send() with { Blksize = -1 }).Validate());
        Assert.Equal("Primary space must be a positive number.", (Send() with { PrimarySpace = 0 }).Validate());
        Assert.Equal("Secondary space must be a positive number.", (Send() with { SecondarySpace = 0 }).Validate());
        Assert.Equal("Average block size must be a positive number.", (Send() with { AverageBlock = 0 }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(32769)]
    public void Buffer_size_outside_the_range_is_rejected(int size) =>
        Assert.Equal("Buffer size must be between 256 and 32768.", (Send() with { BufferSize = size }).Validate());

    [Fact]
    public void Buffer_size_bounds_are_accepted()
    {
        Assert.Null((Send() with { BufferSize = 256 }).Validate());
        Assert.Null((Send() with { BufferSize = 32768 }).Validate());
    }

    [Fact]
    public void Tso_send_with_allocation_units_needs_primary_space()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.Tracks };
        Assert.Equal("Primary space is required when allocation units are set.", r.Validate());
        Assert.Null((r with { PrimarySpace = 5 }).Validate());
    }

    [Fact]
    public void Avblock_needs_an_average_block_size()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.AvBlock, PrimarySpace = 5 };
        Assert.Equal("Average block size is required for AVBLOCK allocation.", r.Validate());
        Assert.Null((r with { AverageBlock = 4096 }).Validate());
    }

    [Fact]
    public void Allocation_rules_apply_only_when_sending_to_tso()
    {
        var r = Send() with { AllocationUnits = AllocationUnits.AvBlock };
        Assert.Null((r with { Direction = TransferDirection.Receive }).Validate());
        Assert.Null((r with { HostType = TransferHostType.Vm }).Validate());
        Assert.Null((r with { HostType = TransferHostType.Cics }).Validate());
    }

    [Fact]
    public void Result_is_a_plain_record()
    {
        var result = new FileTransferResult(false, "TRANS17 Miscellaneous I/O error");
        Assert.False(result.Succeeded);
        Assert.Equal("TRANS17 Miscellaneous I/O error", result.Message);
    }
}
