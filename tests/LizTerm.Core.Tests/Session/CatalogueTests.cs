// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class CatalogueTests
{
    [Fact]
    public void Models_carry_their_geometry_and_render_it()
    {
        Assert.Equal(4, TerminalModel.All.Count);
        Assert.Equal("3 — 32x80", TerminalModel.Find(3)!.ToString());
        Assert.Equal("5 — 27x132", TerminalModel.Find(5)!.ToString());
        Assert.Null(TerminalModel.Find(9));

        // A model seeded from a hand-edited profile carries no geometry, and "9 — 0x0" would be a lie.
        Assert.Equal("9", new TerminalModel(9, 0, 0).ToString());
    }

    [Fact]
    public void Every_model_the_profile_default_can_hold_is_present()
    {
        foreach (var number in new[] { 2, 3, 4, 5 }) Assert.NotNull(TerminalModel.Find(number));
    }

    /// <summary>41 as reported by b3270 4.5ga6. Task 12 asserts this against a live engine; this only pins the
    /// count so an accidental deletion is loud.</summary>
    [Fact]
    public void Code_pages_are_the_full_engine_list()
    {
        Assert.Equal(41, CodePage.All.Count);
        Assert.Equal("cp285 — United Kingdom", CodePage.Find("cp285")!.ToString());
        Assert.Null(CodePage.Find("bogus-page"));
    }

    /// <summary>bracket is what this project's own TK5 sample profile uses and is named nothing like a code
    /// page, so it leads rather than trailing the list as it does in the engine's own ordering.</summary>
    [Fact]
    public void Bracket_leads_the_list_and_the_rest_stay_in_numeric_order()
    {
        Assert.Equal("bracket", CodePage.All[0].Name);
        Assert.Equal("cp037", CodePage.All[1].Name);
        Assert.Equal("cp1399", CodePage.All[^1].Name);

        var numbers = CodePage.All.Skip(1).Select(p => int.Parse(p.Name[2..])).ToList();
        for (var i = 1; i < numbers.Count; i++)
            Assert.True(numbers[i] > numbers[i - 1], $"cp{numbers[i - 1]} should sort before cp{numbers[i]}");
    }

    [Fact]
    public void Every_code_page_has_a_label_and_a_unique_name()
    {
        Assert.All(CodePage.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Label)));
        Assert.Equal(CodePage.All.Count, CodePage.All.Select(p => p.Name).Distinct().Count());
    }
}
