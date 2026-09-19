// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class HostPathTests
{
    [Fact]
    public void A_dataset_is_trimmed_and_folded_to_upper_case()
    {
        var path = HostPath.ForDataset("  sys1.proclib ");
        Assert.Equal(HostPathKind.Dataset, path.Kind);
        Assert.Equal("SYS1.PROCLIB", path.Dataset);
        Assert.Null(path.Member);
        Assert.Equal("SYS1.PROCLIB", path.ToString());
    }

    [Fact]
    public void A_member_prints_in_parentheses()
    {
        var path = HostPath.ForMember("sys1.proclib", "jes2");
        Assert.Equal(HostPathKind.Member, path.Kind);
        Assert.Equal("JES2", path.Member);
        Assert.Equal("SYS1.PROCLIB(JES2)", path.ToString());
        Assert.Equal(path, HostPath.ForDataset("SYS1.PROCLIB").WithMember("JES2"));
    }

    [Theory]
    [InlineData("sys1.proclib", "SYS1.PROCLIB")]
    [InlineData(" sys1.proclib(jes2) ", "SYS1.PROCLIB(JES2)")]
    [InlineData("#$@.A-B(@A1)", "#$@.A-B(@A1)")]
    public void Parses_datasets_and_members(string text, string expected)
    {
        Assert.True(HostPath.TryParse(text, out var path, out var error), error);
        Assert.Equal(expected, path!.ToString());
    }

    [Theory]
    [InlineData("", "Enter a dataset name.")]
    [InlineData("SYS1.PROCLIB(JES2", "A member name must end with ')'.")]
    [InlineData("AAAAAAAA.BBBBBBBB.CCCCCCCC.DDDDDDDD.EEEEEEEE.F", "A dataset name is at most 44 characters.")]
    [InlineData("SYS1..X", "A dataset name cannot have an empty qualifier.")]
    [InlineData("SYS1.ABCDEFGHI", "Qualifier 'ABCDEFGHI' is longer than 8 characters.")]
    [InlineData("SYS1.123", "Qualifier '123' must start with a letter or # $ @.")]
    [InlineData("SYS1.A_B", "Qualifier 'A_B' contains '_', which a dataset name cannot.")]
    [InlineData("SYS1.X()", "Enter a member name.")]
    [InlineData("SYS1.X(TOOLONGNM)", "A member name is at most 8 characters.")]
    [InlineData("SYS1.X(1ABC)", "A member name must start with a letter or # $ @.")]
    [InlineData("SYS1.X(AB-C)", "A member name cannot contain '-'.")]
    public void Rejects_bad_names_with_a_reason(string text, string expected)
    {
        Assert.False(HostPath.TryParse(text, out var path, out var error));
        Assert.Null(path);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void The_factories_throw_the_same_reason()
    {
        var ex = Assert.Throws<ArgumentException>(() => HostPath.ForMember("SYS1.X", "1ABC"));
        Assert.Equal("A member name must start with a letter or # $ @.", ex.Message);
    }

    [Theory]
    [InlineData("sys1.**", null)]
    [InlineData("MVSCE02.C%ST", null)]
    [InlineData("", "Enter a dataset filter.")]
    [InlineData("SYS1.A B", "A filter cannot contain ' '.")]
    [InlineData("AAAAAAAA.BBBBBBBB.CCCCCCCC.DDDDDDDD.EEEEEEEE.*", "A filter is at most 44 characters.")]
    public void Checks_dataset_filters(string pattern, string? expected) =>
        Assert.Equal(expected, HostPath.DatasetPatternError(pattern));

    [Theory]
    [InlineData("jes2*", null)]
    [InlineData("*JES*", null)]
    [InlineData("JES2%%%%", null)]
    [InlineData("", "Enter a member filter.")]
    [InlineData("JES 2", "A member filter cannot contain ' '.")]
    [InlineData("A.B", "A member filter cannot contain '.'.")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "A member filter is at most 44 characters.")]
    public void Checks_member_filters(string pattern, string? expected) =>
        Assert.Equal(expected, HostPath.MemberPatternError(pattern));

    [Theory]
    [InlineData("IEF*", "IEFBR14", true)]
    [InlineData("ief*", "IEFBR14", true)]
    [InlineData("*BR*", "IEFBR14", true)]
    [InlineData("IEF%%14", "IEFBR14", true)]
    [InlineData("IEF%14", "IEFBR14", false)]
    [InlineData("IEF", "IEFBR14", false)]
    [InlineData("*A.B*", "IEFBR14", false)]
    public void Matches_member_filters_as_the_host_does(string pattern, string name, bool expected) =>
        Assert.Equal(expected, HostPath.MemberPatternMatches(pattern, name));
}
