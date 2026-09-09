// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Files;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Files;

public class LocalFileNamesTests
{
    [Theory]
    [InlineData("LIZTERM.JCL(JOB1)", TransferHostType.Tso, "JOB1")]
    [InlineData("'MVSCE02.LIZTERM.JCL(JOB1)'", TransferHostType.Tso, "JOB1")]
    [InlineData("LIZTERM.ITEST", TransferHostType.Tso, "ITEST")]
    [InlineData("'MVSCE02.LIZTERM.ITEST'", TransferHostType.Tso, "ITEST")]
    [InlineData("ITEST", TransferHostType.Tso, "ITEST")]
    [InlineData("A.B()", TransferHostType.Tso, "received")]
    [InlineData("A.B.", TransferHostType.Tso, "received")]
    [InlineData("PROFILE EXEC A", TransferHostType.Vm, "PROFILE.EXEC")]
    [InlineData("PROFILE  EXEC", TransferHostType.Vm, "PROFILE.EXEC")]
    [InlineData("PROFILE", TransferHostType.Vm, "PROFILE")]
    [InlineData("MYFILE", TransferHostType.Cics, "MYFILE")]
    [InlineData("my file", TransferHostType.Cics, "my file")]
    [InlineData("   ", TransferHostType.Tso, "received")]
    [InlineData("''", TransferHostType.Tso, "received")]
    public void Suggests_a_local_name_from_the_host_name(string hostFile, TransferHostType type, string expected) =>
        Assert.Equal(expected, LocalFileNames.Suggest(hostFile, type));
}
