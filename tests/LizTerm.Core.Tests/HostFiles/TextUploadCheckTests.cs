// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class TextUploadCheckTests
{
    private static readonly DatasetAttributes Fb80 = new("PO", "FB", 80, 19040, "PUB000");

    private static TextUploadResult Run(string text, DatasetAttributes? target = null, TextUploadOptions? options = null) =>
        TextUploadCheck.Run(Encoding.UTF8.GetBytes(text), target ?? Fb80, options);

    [Fact]
    public void Plain_lines_pass_unchanged()
    {
        var result = Run("//HELLO JOB\n//STEP EXEC PGM=IEFBR14\n");
        Assert.True(result.CanUpload);
        Assert.Equal(new[] { "//HELLO JOB", "//STEP EXEC PGM=IEFBR14" }, result.Lines);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void A_tab_width_below_one_is_refused(int width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Run("A\tB\n", options: new TextUploadOptions(TabWidth: width)));

    [Fact]
    public void A_utf8_byte_order_mark_is_dropped()
    {
        var result = TextUploadCheck.Run([0xEF, 0xBB, 0xBF, (byte)'A', (byte)'\n'], Fb80);
        Assert.Equal(new[] { "A" }, result.Lines);
    }

    [Theory]
    [InlineData("A\nB", new[] { "A", "B" })]
    [InlineData("A\r\nB\r\n", new[] { "A", "B" })]
    [InlineData("A\rB\r", new[] { "A", "B" })]
    [InlineData("A\n\nB\n", new[] { "A", "", "B" })]
    [InlineData("A\n\n", new[] { "A", "" })]
    [InlineData("", new string[0])]
    [InlineData("A\fB", new[] { "A\fB" })]
    public void Splits_on_lf_crlf_and_cr_only_and_keeps_blank_lines(string text, string[] expected) =>
        Assert.Equal(expected, TextUploadCheck.SplitLines(text));

    [Fact]
    public void Invalid_utf8_blocks_and_suggests_binary()
    {
        var result = TextUploadCheck.Run([(byte)'A', 0xFF, (byte)'\n'], Fb80);
        Assert.False(result.CanUpload);
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.InvalidEncoding, problem.Kind);
        Assert.Equal(0, problem.Line);
        Assert.Equal("The file is not UTF-8 text. Choose Binary to send its bytes unchanged.", problem.Message);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void A_character_above_latin1_blocks_and_is_named()
    {
        var result = Run("OK\nPRICE 5€\n");
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.UnsupportedCharacter, problem.Kind);
        Assert.Equal(2, problem.Line);
        Assert.Equal("Line 2 contains “€” (U+20AC), which the host can't store.", problem.Message);
    }

    [Fact]
    public void A_character_outside_the_basic_plane_is_named_whole()
    {
        var problem = Assert.Single(Run("A😀\n").Errors);
        Assert.Equal("Line 1 contains “😀” (U+1F600), which the host can't store.", problem.Message);
    }

    [Fact]
    public void Latin1_characters_pass() => Assert.True(Run("¬ ¢ é | ~ \\ [ ]\n").CanUpload);

    [Fact]
    public void A_line_longer_than_the_record_blocks()
    {
        var result = Run(new string('X', 80) + "\n" + new string('Y', 81) + "\n");
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.LineTooLong, problem.Kind);
        Assert.Equal(2, problem.Line);
        Assert.Equal("Line 2 is 81 characters; the limit is 80.", problem.Message);
    }

    [Fact]
    public void Variable_records_allow_lrecl_less_four()
    {
        var vb84 = new DatasetAttributes("PS", "VB", 84, 6233, null);
        Assert.True(Run(new string('X', 80), vb84).CanUpload);
        Assert.Equal("Line 1 is 81 characters; the limit is 80.", Assert.Single(Run(new string('X', 81), vb84).Errors).Message);
    }

    [Fact]
    public void Undefined_records_use_the_block_size()
    {
        var u100 = new DatasetAttributes("PS", "U", 0, 100, null);
        Assert.True(Run(new string('X', 100), u100).CanUpload);
        Assert.False(Run(new string('X', 101), u100).CanUpload);
    }

    [Fact]
    public void An_unknown_record_format_skips_the_length_check() =>
        Assert.True(Run(new string('X', 500), new DatasetAttributes("PS", null, null, null, null)).CanUpload);

    [Fact]
    public void Tabs_are_expanded_by_default_and_reported()
    {
        var result = Run("A\tB\nC\n\tD\n");
        Assert.True(result.CanUpload);
        Assert.Equal(new[] { "A       B", "C", "        D" }, result.Lines);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(TextUploadProblemKind.TabsPresent, warning.Kind);
        Assert.Equal(1, warning.Line);
        Assert.Equal("2 lines contain tab characters.", warning.Message);
    }

    [Fact]
    public void Tabs_can_be_left_alone()
    {
        var result = Run("A\tB\n", options: new TextUploadOptions(ExpandTabs: false));
        Assert.Equal(new[] { "A\tB" }, result.Lines);
        Assert.Equal("1 line contains tab characters.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Expanded_tabs_count_towards_the_length()
    {
        var result = Run(new string('X', 73) + "\tY\n");
        Assert.Equal("Line 1 is 81 characters; the limit is 80.", Assert.Single(result.Errors).Message);
    }

    [Theory]
    [InlineData("a\tb", 8, "a       b")]
    [InlineData("12345678\tX", 8, "12345678        X")]
    [InlineData("\t", 4, "    ")]
    public void ExpandTabs_moves_to_the_next_stop(string line, int width, string expected) =>
        Assert.Equal(expected, TextUploadCheck.ExpandTabs(line, width));

    [Fact]
    public void Only_five_problems_of_a_kind_are_listed_then_a_count()
    {
        var text = string.Concat(Enumerable.Repeat(new string('X', 81) + "\n", 8)) + "€\n";
        var result = Run(text);
        Assert.Equal(7, result.Errors.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 0 }, result.Errors.Take(6).Select(p => p.Line));
        Assert.Equal("3 more lines have the same problem.", result.Errors[5].Message);
        Assert.Equal(TextUploadProblemKind.LineTooLong, result.Errors[5].Kind);
        Assert.Equal(TextUploadProblemKind.UnsupportedCharacter, result.Errors[6].Kind);
    }

    [Fact]
    public void A_unix_file_has_no_record_length_and_keeps_its_tabs()
    {
        var result = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("A\tB\n" + new string('x', 500) + "\n"), maxBytes: null, new TextUploadOptions(ExpandTabs: false));
        Assert.True(result.CanUpload);
        Assert.Equal("A\tB", result.Lines[0]);
        Assert.Equal(500, result.Lines[1].Length);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_unix_file_still_refuses_characters_outside_latin1()
    {
        var result = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("café\n€\n"));
        Assert.False(result.CanUpload);
        var problem = Assert.Single(result.Errors);
        Assert.Equal(TextUploadProblemKind.UnsupportedCharacter, problem.Kind);
        Assert.Equal(2, problem.Line);
    }

    [Fact]
    public void A_unix_file_over_the_cap_is_too_large_counting_latin1_bytes_and_line_feeds()
    {
        var ok = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("ab\ncd\n"), maxBytes: 6);
        Assert.True(ok.CanUpload);

        var over = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("ab\ncd\n"), maxBytes: 5);
        Assert.False(over.CanUpload);
        var problem = Assert.Single(over.Errors);
        Assert.Equal(TextUploadProblemKind.FileTooLarge, problem.Kind);
        Assert.Equal(0, problem.Line);
        Assert.Equal("The file is 6 bytes; the host holds at most 5.", problem.Message);
    }

    [Fact]
    public void The_cap_counts_expanded_tabs_and_formats_thousands()
    {
        var result = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("\tx\n"), maxBytes: 8, new TextUploadOptions(ExpandTabs: true));
        Assert.False(result.CanUpload);
        Assert.Equal("The file is 10 bytes; the host holds at most 8.", Assert.Single(result.Errors).Message);

        var big = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes(new string('x', 70_000) + "\n"), maxBytes: HostFileLimits.MaxUnixFileBytes);
        Assert.Equal("The file is 70,001 bytes; the host holds at most 65,536.", Assert.Single(big.Errors).Message);
    }

    [Fact]
    public void A_dataset_check_still_warns_about_tabs_it_did_not_expand()
    {
        var result = Run("A\tB\n", options: new TextUploadOptions(ExpandTabs: false));
        Assert.Equal(TextUploadProblemKind.TabsPresent, Assert.Single(result.Warnings).Kind);
    }

    [Fact]
    public void A_final_line_without_a_newline_still_counts_its_line_ending()
    {
        // "ab\ncd" is 5 bytes in the source, but the write path stores every line LF-terminated, including the
        // last, so the stored size — and the cap — count a line ending for "cd" too, making it 6 bytes.
        var ok = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("ab\ncd"), maxBytes: 6);
        Assert.True(ok.CanUpload);

        var over = TextUploadCheck.RunForUnixFile(Encoding.UTF8.GetBytes("ab\ncd"), maxBytes: 5);
        Assert.False(over.CanUpload);
        Assert.Equal("The file is 6 bytes; the host holds at most 5.", Assert.Single(over.Errors).Message);
    }
}
