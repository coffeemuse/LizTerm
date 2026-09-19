// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class DatasetAllocationTests
{
    private static readonly DatasetAllocation Pds = new(DatasetOrganization.Partitioned, "FB", 80, 3120, SpaceUnit.Tracks, 5, 5, 20);

    [Fact]
    public void A_sound_allocation_has_no_problems()
    {
        Assert.Empty(Pds.Problems());
        Assert.True(Pds.IsValid);
        Assert.Empty((Pds with { Organization = DatasetOrganization.Sequential, DirectoryBlocks = 0 }).Problems());
        Assert.Empty((Pds with { Recfm = " u ", Lrecl = 0, Blksize = 19069 }).Problems());
    }

    [Theory]
    [InlineData("FB", null)]
    [InlineData("fba", null)]
    [InlineData("VBS", null)]
    [InlineData("U", null)]
    [InlineData("", "Enter a record format.")]
    [InlineData("D", "A record format starts with F, V or U.")]
    [InlineData("FX", "A record format cannot contain 'X'.")]
    [InlineData("FBB", "A record format cannot repeat 'B'.")]
    [InlineData("UBSAMB", "A record format cannot repeat 'B'.")]
    public void Recfm_is_a_first_letter_then_modifiers_at_most_once(string recfm, string? expected) =>
        Assert.Equal(expected, DatasetAllocation.RecfmError(recfm));

    [Fact]
    public void Recfm_is_folded()
    {
        Assert.Equal("FBA", (Pds with { Recfm = " fba " }).FoldedRecfm);
        Assert.True((Pds with { Recfm = "u" }).IsUndefinedLength);
        Assert.False(Pds.IsUndefinedLength);
    }

    [Theory]
    [InlineData("FB", 0, AllocationField.Lrecl, "LRECL must be between 1 and 32760.")]
    [InlineData("FB", 32761, AllocationField.Lrecl, "LRECL must be between 1 and 32760.")]
    [InlineData("U", -1, AllocationField.Lrecl, "LRECL must be between 0 and 32760 for undefined-length records.")]
    public void Lrecl_has_a_range_that_depends_on_the_format(string recfm, int lrecl, AllocationField field, string expected) =>
        Assert.Equal(expected, (Pds with { Recfm = recfm, Lrecl = lrecl }).Problems()[field]);

    [Fact]
    public void Each_field_reports_its_own_problem()
    {
        var problems = (Pds with { Recfm = "Q", Blksize = 0, Primary = 0, Secondary = -1, DirectoryBlocks = 0 }).Problems();

        Assert.Equal("A record format starts with F, V or U.", problems[AllocationField.Recfm]);
        Assert.Equal("BLKSIZE must be between 1 and 32760.", problems[AllocationField.Blksize]);
        Assert.Equal("Primary space must be at least 1.", problems[AllocationField.Primary]);
        Assert.Equal("Secondary space cannot be negative.", problems[AllocationField.Secondary]);
        Assert.Equal("A partitioned dataset needs at least 1 directory block.", problems[AllocationField.DirectoryBlocks]);
        Assert.Equal(5, problems.Count);
    }

    [Fact]
    public void Directory_blocks_are_ignored_for_a_sequential_dataset() =>
        Assert.Empty((Pds with { Organization = DatasetOrganization.Sequential, DirectoryBlocks = -3 }).Problems());

    [Fact]
    public void A_null_record_format_is_a_problem_not_a_crash()
    {
        var problems = (Pds with { Recfm = null! }).Problems();

        Assert.Equal("Enter a record format.", problems[AllocationField.Recfm]);
        Assert.Single(problems);
    }
}
