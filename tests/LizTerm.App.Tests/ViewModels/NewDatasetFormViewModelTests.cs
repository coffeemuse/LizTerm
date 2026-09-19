// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class NewDatasetFormViewModelTests
{
    private static NewDatasetFormViewModel Filled(string name = "MVSCE02.NEW")
    {
        var form = new NewDatasetFormViewModel { Name = name };
        return form;
    }

    [Fact]
    public void Starts_as_an_fb80_library_with_the_default_space_and_no_name()
    {
        var form = new NewDatasetFormViewModel();

        Assert.Equal(("", true, "FB", "80", "3120"), (form.Name, form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
        Assert.Equal((true, "5", "5", "20"), (form.IsTracks, form.Primary, form.Secondary, form.DirectoryBlocks));
        Assert.Equal("✗ Enter a dataset name.", form.NameProblem);
        Assert.Null(form.RecfmProblem);
        Assert.False(form.CanCreate);
    }

    [Fact]
    public void A_full_form_can_create_and_folds_what_it_sends()
    {
        var form = Filled(" mvsce02.new ");
        form.Recfm = " fb ";
        form.IsCylinders = true;

        Assert.True(form.CanCreate);
        var allocation = form.Allocation;
        Assert.Equal(DatasetOrganization.Partitioned, allocation.Organization);
        Assert.Equal("FB", allocation.FoldedRecfm);
        Assert.Equal((80, 3120, SpaceUnit.Cylinders, 5, 5, 20), (allocation.Lrecl, allocation.Blksize, allocation.Unit, allocation.Primary, allocation.Secondary, allocation.DirectoryBlocks));
        Assert.False(form.IsTracks);
    }

    [Fact]
    public void Each_field_shows_its_own_problem()
    {
        var form = Filled();

        form.Recfm = "X";
        Assert.Equal("✗ A record format starts with F, V or U.", form.RecfmProblem);
        form.Recfm = "FB";

        form.Lrecl = "abc";
        Assert.Equal("✗ Enter a whole number.", form.LreclProblem);
        form.Lrecl = "0";
        Assert.Equal("✗ LRECL must be between 1 and 32760.", form.LreclProblem);
        form.Lrecl = "80";

        form.Blksize = "-1";
        Assert.Equal("✗ Enter a whole number.", form.BlksizeProblem);
        form.Blksize = "0";
        Assert.Equal("✗ BLKSIZE must be between 1 and 32760.", form.BlksizeProblem);
        form.Blksize = "3120";

        form.Primary = "0";
        Assert.Equal("✗ Primary space must be at least 1.", form.PrimaryProblem);
        form.Primary = "5";

        form.Secondary = "x";
        Assert.Equal("✗ Enter a whole number.", form.SecondaryProblem);
        form.Secondary = "0";
        Assert.Null(form.SecondaryProblem);

        form.DirectoryBlocks = "0";
        Assert.Equal("✗ A partitioned dataset needs at least 1 directory block.", form.DirectoryBlocksProblem);
        Assert.False(form.CanCreate);
        form.DirectoryBlocks = "20";
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void A_sequential_dataset_ignores_the_directory_blocks()
    {
        var form = Filled();
        form.DirectoryBlocks = "x";
        Assert.NotNull(form.DirectoryBlocksProblem);

        form.IsSequential = true;

        Assert.False(form.IsPartitioned);
        Assert.Null(form.DirectoryBlocksProblem);
        Assert.True(form.CanCreate);
        Assert.Equal(DatasetOrganization.Sequential, form.Allocation.Organization);
    }

    [Fact]
    public void Undefined_length_records_allow_lrecl_0()
    {
        var form = Filled();
        form.Recfm = "U";
        form.Lrecl = "0";

        Assert.Null(form.LreclProblem);
        Assert.True(form.CanCreate);
    }

    [Fact]
    public void A_bad_name_is_the_name_problem()
    {
        var form = Filled("MVSCE02.TOOLONGQUALIFIER");

        Assert.Equal("✗ Qualifier 'TOOLONGQUALIFIER' is longer than 8 characters.", form.NameProblem);
        Assert.False(form.CanCreate);
    }

    [Fact]
    public void Prefill_takes_the_type_and_dcb_from_a_dataset()
    {
        var form = Filled();

        form.PrefillFrom(new DatasetAttributes("PS", "U", 0, 19069, "PUB000"));

        Assert.Equal((false, "U", "0", "19069"), (form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
        Assert.Equal(("5", "5", "20"), (form.Primary, form.Secondary, form.DirectoryBlocks));
    }

    [Fact]
    public void Prefill_leaves_what_the_listing_did_not_say()
    {
        var form = Filled();

        form.PrefillFrom(new DatasetAttributes("DA", null, null, 4096, null));

        Assert.Equal((true, "FB", "80", "4096"), (form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
    }

    [Fact]
    public void A_change_notifies_every_problem_and_can_create()
    {
        var form = Filled();
        var changed = new List<string?>();
        form.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        form.Recfm = "V";

        foreach (var name in new[] { nameof(form.RecfmProblem), nameof(form.LreclProblem), nameof(form.CanCreate) })
            Assert.Contains(name, changed);
        changed.Clear();
        form.IsPartitioned = false;
        Assert.Contains(nameof(form.IsSequential), changed);
        Assert.Contains(nameof(form.DirectoryBlocksProblem), changed);
    }
}
