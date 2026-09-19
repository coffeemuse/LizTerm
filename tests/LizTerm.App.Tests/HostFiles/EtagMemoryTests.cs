// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.HostFiles;

public class EtagMemoryTests
{
    private static readonly HostPath Hello = HostPath.ForMember("MVSCE02.CNTL", "HELLO");
    private static readonly HostPath Alloc = HostPath.ForMember("MVSCE02.CNTL", "ALLOC");
    private static readonly HostPath Cntl = HostPath.ForDataset("MVSCE02.CNTL");

    [Fact]
    public void Remembers_a_stamp_by_path_exactly_as_given()
    {
        var memory = new EtagMemory();

        memory.Remember(Hello, "W/\"347617DA000000A0\"");

        Assert.Equal("W/\"347617DA000000A0\"", memory.TryGet(Hello));
        Assert.Null(memory.TryGet(Alloc));
        Assert.Equal(1, memory.Count);
    }

    [Fact]
    public void Remembering_null_forgets()
    {
        var memory = new EtagMemory();
        memory.Remember(Hello, "a");

        memory.Remember(Hello, null);

        Assert.Null(memory.TryGet(Hello));
        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void Forget_drops_one_path()
    {
        var memory = new EtagMemory();
        memory.Remember(Hello, "a");
        memory.Remember(Alloc, "b");

        memory.Forget(Hello);

        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("b", memory.TryGet(Alloc));
    }

    [Fact]
    public void ForgetUnder_drops_the_dataset_and_its_members_but_not_a_longer_name()
    {
        var memory = new EtagMemory();
        memory.Remember(Cntl, "ds");
        memory.Remember(Hello, "a");
        memory.Remember(HostPath.ForDataset("MVSCE02.CNTL2"), "other");
        memory.Remember(HostPath.ForMember("MVSCE02.CNTL2", "HELLO"), "other-member");

        memory.ForgetUnder(Cntl);

        Assert.Null(memory.TryGet(Cntl));
        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("other", memory.TryGet(HostPath.ForDataset("MVSCE02.CNTL2")));
        Assert.Equal("other-member", memory.TryGet(HostPath.ForMember("MVSCE02.CNTL2", "HELLO")));
    }

    [Fact]
    public void Move_carries_a_members_stamp_to_its_new_name()
    {
        var memory = new EtagMemory();
        memory.Remember(Hello, "a");

        memory.Move(Hello, HostPath.ForMember("MVSCE02.CNTL", "HELLO2"));

        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("a", memory.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO2")));
    }

    [Fact]
    public void Move_of_a_dataset_carries_everything_under_it()
    {
        var memory = new EtagMemory();
        memory.Remember(Cntl, "ds");
        memory.Remember(Hello, "a");
        memory.Remember(HostPath.ForMember("MVSCE02.CNTL2", "HELLO"), "other");

        memory.Move(Cntl, HostPath.ForDataset("MVSCE02.JCL"));

        Assert.Null(memory.TryGet(Cntl));
        Assert.Null(memory.TryGet(Hello));
        Assert.Equal("ds", memory.TryGet(HostPath.ForDataset("MVSCE02.JCL")));
        Assert.Equal("a", memory.TryGet(HostPath.ForMember("MVSCE02.JCL", "HELLO")));
        Assert.Equal("other", memory.TryGet(HostPath.ForMember("MVSCE02.CNTL2", "HELLO")));
    }

    [Fact]
    public void Move_of_a_path_with_no_stamp_does_nothing()
    {
        var memory = new EtagMemory();

        memory.Move(Hello, Alloc);

        Assert.Equal(0, memory.Count);
    }

    [Fact]
    public void An_access_owns_one_memory()
    {
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080" },
            (_, _, _) => new FakeHostFileService(), savePin: null);

        Assert.Same(access.Etags, access.Etags);
        Assert.Equal(0, access.Etags.Count);
    }
}
