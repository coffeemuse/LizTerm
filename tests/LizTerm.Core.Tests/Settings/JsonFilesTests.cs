// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

/// <summary>Why a file would not read (#168). System.Text.Json already says; these pin the wording we pass on.</summary>
public class JsonFilesTests
{
    [Fact]
    public void A_json_object_parses_with_no_problem()
    {
        Assert.NotNull(JsonFiles.Parse("""{"bindings": {}}""", out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void A_trailing_comma_is_reported_with_the_line_the_editor_shows()
    {
        Assert.Null(JsonFiles.Parse("{\n  \"bindings\": {\n    \"Ctrl+Q\": \"PF1\",\n  }\n}", out var problem));

        // The reader counts lines from zero; the user's editor counts from one.
        Assert.Equal("It is not valid JSON. Line 4: the JSON object contains a trailing comma at the end.", problem);
    }

    [Fact]
    public void An_unclosed_object_is_reported()
    {
        Assert.Null(JsonFiles.Parse("{\n  \"bindings\": {\n    \"Ctrl+Q\": \"PF1\"\n}", out var problem));

        Assert.Equal("It is not valid JSON. Line 4: there is an open JSON object or array that should be closed.", problem);
    }

    [Fact]
    public void An_unquoted_value_is_reported_where_it_starts()
    {
        Assert.Null(JsonFiles.Parse("{\n  \"bindings\": {\n    \"Ctrl+Q\": PF1\n  }\n}", out var problem));

        Assert.Equal("It is not valid JSON. Line 3: 'P' is an invalid start of a value.", problem);
    }

    [Fact]
    public void A_duplicated_chord_is_named_although_the_reader_gives_no_line()
    {
        Assert.Null(JsonFiles.Parse("""{"bindings": {"Ctrl+Q": "PF1", "Ctrl+Q": "PF2"}}""", out var problem));

        Assert.Equal("It is not valid JSON. Duplicate property 'Ctrl+Q'.", problem);
    }

    [Fact]
    public void Valid_json_that_is_not_an_object_is_its_own_problem()
    {
        Assert.Null(JsonFiles.Parse("[1, 2]", out var problem));

        Assert.Equal("It does not hold a JSON object.", problem);
    }

    [Fact]
    public void ReadLenient_reports_the_problem_for_a_file_that_will_not_parse()
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "not json");
        try
        {
            Assert.Null(JsonFiles.ReadLenient(path, out var problem));
            Assert.Equal("It is not valid JSON. Line 1: 'not json' is an invalid JSON literal. Expected the literal 'null'.", problem);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A file that is there but will not open costs every binding just as a broken one does, and the
    /// user can chmod it, so it is reported rather than read as "no file" (#168). A directory in the file's place
    /// is the portable way to make the read throw; a mode of 000 would not fail on Windows.</summary>
    [Fact]
    public void ReadLenient_reports_a_file_it_could_not_open_at_all()
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(path);
        try
        {
            Assert.Null(JsonFiles.ReadLenient(path, out var problem));

            Assert.NotNull(problem);
            Assert.EndsWith(".", problem);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Fact]
    public void ReadLenient_reports_no_problem_for_a_missing_file()
    {
        Assert.Null(JsonFiles.ReadLenient(Path.Combine(Path.GetTempPath(), "lizterm-no-such-" + Guid.NewGuid().ToString("N")), out var problem));

        Assert.Null(problem);
    }

    [Fact]
    public void ReadStrict_carries_the_reason_into_its_message()
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{\n  \"bindings\": {\n    \"Ctrl+Q\": \"PF1\",\n  }\n}");
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => JsonFiles.ReadStrict(path));

            Assert.Contains("Line 4: the JSON object contains a trailing comma at the end.", ex.Message);
            Assert.Contains(path, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
