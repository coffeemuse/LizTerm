// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class HostCredentialsTests
{
    [Fact]
    public void ToString_never_shows_the_password()
    {
        var credentials = new HostCredentials("MVSCE02", "s3cret-pw");
        Assert.Equal("MVSCE02", credentials.Userid);
        Assert.Equal("s3cret-pw", credentials.Password);
        Assert.DoesNotContain("s3cret-pw", credentials.ToString());
        Assert.Contains("MVSCE02", credentials.ToString());
    }

    [Fact]
    public void An_exception_carries_its_kind_reason_and_server_text()
    {
        var ex = new HostFileException(HostFileErrorKind.CannotOpen, "SYS1.X(Y): not found.", 3, "Cannot open dataset member");
        Assert.Equal(HostFileErrorKind.CannotOpen, ex.Kind);
        Assert.Equal(3, ex.Reason);
        Assert.Equal("Cannot open dataset member", ex.ServerMessage);
        Assert.Equal("SYS1.X(Y): not found.", ex.Message);
        Assert.Null(ex.Certificate);
    }
}
