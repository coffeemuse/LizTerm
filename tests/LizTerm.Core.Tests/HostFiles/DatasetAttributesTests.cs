// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class DatasetAttributesTests
{
    [Theory]
    [InlineData("FB", 80, 19040, 80)]
    [InlineData("F", 133, 133, 133)]
    [InlineData("VB", 255, 6233, 251)]
    [InlineData("U", 0, 19069, 19069)]
    [InlineData(null, 80, 800, null)]
    [InlineData("VB", 4, 100, null)]
    [InlineData("FB", 0, 100, null)]
    public void Usable_line_length_follows_the_record_format(string? recfm, int lrecl, int blksize, int? expected) =>
        Assert.Equal(expected, new DatasetAttributes("PS", recfm, lrecl, blksize, null).UsableLineLength);

    [Theory]
    [InlineData("PO", true, false, true)]
    [InlineData("PS", false, true, true)]
    [InlineData("DA", false, false, false)]
    [InlineData(null, false, false, false)]
    public void Only_partitioned_and_sequential_datasets_are_supported(string? dsorg, bool partitioned, bool sequential, bool supported)
    {
        var attributes = new DatasetAttributes(dsorg, "FB", 80, 800, null);
        Assert.Equal(partitioned, attributes.IsPartitioned);
        Assert.Equal(sequential, attributes.IsSequential);
        Assert.Equal(supported, attributes.IsSupported);
    }

    [Fact]
    public void An_unknown_record_format_is_reported_as_such() =>
        Assert.Equal(RecordFormatFamily.Unknown, new DatasetAttributes("PS", "?", 80, 80, null).RecordFormat);
}
