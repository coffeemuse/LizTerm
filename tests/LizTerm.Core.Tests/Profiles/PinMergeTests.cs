// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Profiles;

public class PinMergeTests
{
    private static readonly CertificatePin Fresh = new("AA:BB", "CN=mvs", "pem-fresh");
    private static readonly CertificatePin Stale = new("CC:DD", "CN=mvs", "pem-stale");

    private static SessionProfile Profile(string host = "mvs", int port = 3270, CertificatePin? pin = null) =>
        new() { Name = "MVS", Host = host, Port = port, PinnedCertificate = pin };

    /// <summary>The reported bug: the picker's copy predated a pin a session window wrote, so the editor showed
    /// none and Save wrote the whole record back over it.</summary>
    [Fact]
    public void A_pin_written_since_the_editor_opened_is_carried_forward()
    {
        var result = PinMerge.Resolve(Profile(pin: null), Profile(pin: Fresh), pinCleared: false);
        Assert.Equal(Fresh, result);
    }

    [Fact]
    public void Forget_clears_the_pin_even_when_disk_still_has_one()
    {
        var result = PinMerge.Resolve(Profile(pin: null), Profile(pin: Fresh), pinCleared: true);
        Assert.Null(result);
    }

    /// <summary>A pin belongs to the host and port it was taken from. Repointing the profile invalidates it, and
    /// the merge must not undo the editor's deliberate drop.</summary>
    [Fact]
    public void Repointing_the_host_drops_the_pin()
    {
        var result = PinMerge.Resolve(Profile(host: "other", pin: null), Profile(host: "mvs", pin: Fresh), pinCleared: false);
        Assert.Null(result);
    }

    [Fact]
    public void Repointing_the_port_drops_the_pin()
    {
        var result = PinMerge.Resolve(Profile(port: 992, pin: null), Profile(port: 3270, pin: Fresh), pinCleared: false);
        Assert.Null(result);
    }

    /// <summary>The editor never invents a pin, so a non-null one there is the copy it loaded. Disk wins when it
    /// has a newer one -- a session that re-pinned after a certificate change.</summary>
    [Fact]
    public void The_disk_pin_wins_over_the_editors_older_copy()
    {
        var result = PinMerge.Resolve(Profile(pin: Stale), Profile(pin: Fresh), pinCleared: false);
        Assert.Equal(Fresh, result);
    }

    /// <summary>A rename deletes the old file after this runs, so a missing file is also the normal path for a
    /// profile being created. Neither may invent a pin.</summary>
    [Fact]
    public void No_file_on_disk_keeps_whatever_the_editor_held()
    {
        Assert.Equal(Stale, PinMerge.Resolve(Profile(pin: Stale), onDisk: null, pinCleared: false));
        Assert.Null(PinMerge.Resolve(Profile(pin: null), onDisk: null, pinCleared: false));
        Assert.Null(PinMerge.Resolve(Profile(pin: Stale), onDisk: null, pinCleared: true));
    }

    [Fact]
    public void No_pin_anywhere_stays_no_pin()
    {
        Assert.Null(PinMerge.Resolve(Profile(pin: null), Profile(pin: null), pinCleared: false));
    }
}
