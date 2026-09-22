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

    [Fact]
    public void A_unix_path_keeps_its_case_and_has_no_dataset()
    {
        var path = HostPath.ForUnix("/u/IBMUSER/Notes.txt");
        Assert.Equal(HostPathKind.Unix, path.Kind);
        Assert.Equal("/u/IBMUSER/Notes.txt", path.UnixPath);
        Assert.Null(path.Dataset);
        Assert.Null(path.Member);
        Assert.Equal("/u/IBMUSER/Notes.txt", path.ToString());
        Assert.Equal("Notes.txt", path.Name);
        Assert.Equal(path, HostPath.ForUnix("/u/IBMUSER/Notes.txt"));
        Assert.NotEqual(path, HostPath.ForUnix("/u/ibmuser/notes.txt"));
    }

    [Fact]
    public void A_unix_path_walks_up_to_the_root_and_down_to_a_child()
    {
        var notes = HostPath.ForUnix("/u/ibmuser/notes");
        Assert.Equal("/u/ibmuser", notes.Parent!.UnixPath);
        Assert.Equal("/u", notes.Parent.Parent!.UnixPath);
        Assert.Equal("/", notes.Parent.Parent.Parent!.UnixPath);
        Assert.Null(notes.Parent.Parent.Parent.Parent);
        Assert.Equal("/", HostPath.ForUnix("/").Name);
        Assert.Equal("/u/ibmuser/notes/drafts", notes.Child("drafts").UnixPath);
        Assert.Equal("/tmp", HostPath.ForUnix("/").Child("tmp").UnixPath);
        Assert.Equal("drafts", notes.Child("drafts").Name);
        Assert.Null(HostPath.ForDataset("SYS1.PROCLIB").Parent);
        Assert.Equal("SYS1.PROCLIB", HostPath.ForDataset("SYS1.PROCLIB").Name);
        Assert.Equal("JES2", HostPath.ForMember("SYS1.PROCLIB", "JES2").Name);
    }

    [Theory]
    [InlineData("a/b", "A name cannot contain '/'.")]
    [InlineData("..", "A path cannot contain a '.' or '..' segment.")]
    [InlineData("", "Enter a name.")]
    public void A_child_name_is_one_segment(string name, string expected)
    {
        var ex = Assert.Throws<ArgumentException>(() => HostPath.ForUnix("/u").Child(name));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void A_dataset_has_no_children_and_a_unix_path_no_members()
    {
        Assert.Throws<InvalidOperationException>(() => HostPath.ForDataset("SYS1.PROCLIB").Child("x"));
        Assert.Throws<InvalidOperationException>(() => HostPath.ForUnix("/u").WithMember("X"));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData(" /u/ibmuser ", "/u/ibmuser")]
    [InlineData("/u/ibmuser/hello world#1.txt", "/u/ibmuser/hello world#1.txt")]
    public void Parses_a_leading_slash_as_a_unix_path(string text, string expected)
    {
        Assert.True(HostPath.TryParse(text, out var path, out var error), error);
        Assert.Equal(HostPathKind.Unix, path!.Kind);
        Assert.Equal(expected, path.UnixPath);
    }

    [Theory]
    [InlineData("", "Enter a path.")]
    [InlineData("u/ibmuser", "A path must start with '/'.")]
    [InlineData("/u//x", "A path cannot have an empty segment.")]
    [InlineData("/u/ibmuser/", "A path cannot end with '/'.")]
    [InlineData("/u/./x", "A path cannot contain a '.' or '..' segment.")]
    [InlineData("/u/../x", "A path cannot contain a '.' or '..' segment.")]
    [InlineData("/u/a\tb", "A path cannot contain control characters.")]
    public void Refuses_a_bad_unix_path_with_a_reason(string text, string expected)
    {
        Assert.Equal(expected, HostPath.UnixPathError(text));
        var ex = Assert.Throws<ArgumentException>(() => HostPath.ForUnix(text));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void A_unix_path_is_at_most_251_characters()
    {
        var longest = "/" + new string('a', 250);
        Assert.Null(HostPath.UnixPathError(longest));
        Assert.Equal("A path is at most 251 characters.", HostPath.UnixPathError(longest + "b"));
        Assert.Equal(251, HostPath.MaxUnixPathLength);
    }

    [Fact]
    public void A_unix_path_keeps_its_blanks_exactly_and_only_typed_text_is_trimmed()
    {
        var lead = HostPath.ForUnix("/u/me/ lead");
        var trail = HostPath.ForUnix("/u/me").Child("trail ");

        Assert.Equal("/u/me/ lead", lead.UnixPath);
        Assert.Equal(" lead", lead.Name);
        Assert.Equal("/u/me/trail ", trail.UnixPath);
        Assert.NotEqual(HostPath.ForUnix("/u/me/trail"), trail);
        Assert.Equal("A path must start with '/'.", HostPath.UnixPathError(" /u/me"));
        Assert.True(HostPath.TryParse(" /u/me/trail ", out var typed, out _));
        Assert.Equal("/u/me/trail", typed!.UnixPath);
    }

    [Fact]
    public void A_unix_path_holds_latin1_only_so_its_length_is_its_bytes()
    {
        Assert.Null(HostPath.UnixPathError("/u/me/café ¬"));
        Assert.Null(HostPath.UnixPathError("/" + new string('é', 250)));
        Assert.Equal("A path cannot contain “€” (U+20AC), which the host can't store.", HostPath.UnixPathError("/u/me/€.txt"));
        Assert.Equal("A path cannot contain “😀” (U+1F600), which the host can't store.", HostPath.UnixPathError("/u/😀"));
        Assert.Throws<ArgumentException>(() => HostPath.ForUnix("/u/me").Child("naïve€"));
    }

    [Fact]
    public void A_bad_unix_path_fails_try_parse_with_the_same_reason()
    {
        Assert.False(HostPath.TryParse("/u/../x", out var path, out var error));
        Assert.Null(path);
        Assert.Equal("A path cannot contain a '.' or '..' segment.", error);
    }
}
