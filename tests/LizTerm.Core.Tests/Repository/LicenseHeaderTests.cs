// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Tests.Repository;

public class LicenseHeaderTests
{
    private const string Cs = """
        // This file is part of LizTerm.
        // Copyright 2026 by CoffeeMuse
        // SPDX-License-Identifier: BSD-3-Clause

        namespace LizTerm.Core;
        """;

    private const string Sh = """
        #!/usr/bin/env bash
        # This file is part of LizTerm.
        # Copyright 2026 by CoffeeMuse
        # SPDX-License-Identifier: BSD-3-Clause

        set -euo pipefail
        """;

    private const string Xml = """
        <!--
          This file is part of LizTerm.
          Copyright 2026 by CoffeeMuse
          SPDX-License-Identifier: BSD-3-Clause
        -->
        <Window />
        """;

    [Theory]
    [InlineData(Cs)]
    [InlineData(Sh)]
    [InlineData(Xml)]
    public void Accepts_all_three_comment_styles(string text) => Assert.True(LicenseHeader.IsPresent(text));

    [Fact]
    public void Rejects_a_file_with_no_header() =>
        Assert.False(LicenseHeader.IsPresent("namespace LizTerm.Core;\n\npublic class Thing;\n"));

    [Fact]
    public void Rejects_a_copyright_with_no_spdx_line() =>
        Assert.False(LicenseHeader.IsPresent("// This file is part of LizTerm.\n// Copyright 2026 by CoffeeMuse\n"));

    [Fact]
    public void Rejects_the_wrong_license() =>
        Assert.False(LicenseHeader.IsPresent(Cs.Replace("BSD-3-Clause", "MIT")));

    [Fact]
    public void Rejects_someone_elses_copyright() =>
        Assert.False(LicenseHeader.IsPresent(Cs.Replace("CoffeeMuse", "Somebody Else")));

    /// <summary>A header is a header because it is at the top. Buried under a using block it is a comment, and a
    /// reader who opens the file to find the terms does not find them.</summary>
    [Fact]
    public void Rejects_a_header_pushed_past_the_top_of_the_file() =>
        Assert.False(LicenseHeader.IsPresent(string.Concat(Enumerable.Repeat("using System;\n", 10)) + Cs));

    /// <summary>Out of order is not the header: the SPDX id must follow the copyright it applies to.</summary>
    [Fact]
    public void Rejects_the_lines_out_of_order() =>
        Assert.False(LicenseHeader.IsPresent(
            "// This file is part of LizTerm.\n// SPDX-License-Identifier: BSD-3-Clause\n// Copyright 2026 by CoffeeMuse\n"));
}
