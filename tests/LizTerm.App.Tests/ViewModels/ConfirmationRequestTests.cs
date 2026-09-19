// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class ConfirmationRequestTests
{
    [Fact]
    public async Task A_plain_question_has_no_input_and_its_primary_is_always_allowed()
    {
        var question = new ConfirmationRequest("Delete HELLO from MVSCE02.CNTL? This cannot be undone.", "Delete 1 member");

        Assert.False(question.HasInput);
        Assert.Equal("", question.Input);
        Assert.Null(question.InputProblem);
        Assert.True(question.CanAnswerPrimary);
        Assert.True(question.PrimaryCommand.CanExecute(null));

        question.PrimaryCommand.Execute(null);

        Assert.Equal(ConfirmChoice.Primary, (await question.Answer).Choice);
    }

    [Fact]
    public void An_input_question_starts_prefilled_and_cannot_be_answered_until_the_name_changes()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        Assert.True(question.HasInput);
        Assert.Equal("HELLO", question.Input);
        Assert.Equal("", question.InputLabel);
        Assert.Null(question.InputProblem);
        Assert.False(question.CanAnswerPrimary);
        Assert.False(question.PrimaryCommand.CanExecute(null));

        question.Input = " hello ";
        Assert.False(question.CanAnswerPrimary);

        question.Input = "HELLO2";
        Assert.True(question.CanAnswerPrimary);
        Assert.True(question.PrimaryCommand.CanExecute(null));
    }

    [Fact]
    public void A_name_the_rule_refuses_is_the_problem_and_blocks_the_primary()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        question.Input = "TOO-LONG-NAME";

        Assert.Equal("A member name is at most 8 characters.", question.InputProblem);
        Assert.False(question.CanAnswerPrimary);

        question.Input = "";
        Assert.Equal("Enter a member name.", question.InputProblem);
    }

    [Fact]
    public async Task The_primary_is_refused_while_it_cannot_be_answered()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);

        question.PrimaryCommand.Execute(null);
        Assert.False(question.Answer.IsCompleted);

        question.Input = "HELLO2";
        question.PrimaryCommand.Execute(null);
        Assert.Equal(ConfirmChoice.Primary, (await question.Answer).Choice);
        Assert.Equal("HELLO2", question.Input);
    }

    [Fact]
    public async Task Cancel_does_not_need_a_changed_name()
    {
        var cancelled = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);
        cancelled.CancelCommand.Execute(null);
        Assert.Equal(ConfirmChoice.Cancel, (await cancelled.Answer).Choice);
    }

    [Fact]
    public void Typing_notifies_the_problem_and_the_primary()
    {
        var question = new ConfirmationRequest("Rename HELLO in MVSCE02.CNTL to:", "Rename",
            input: "HELLO", inputRule: HostPath.MemberNameError);
        var changed = new List<string?>();
        question.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var canExecuteChanged = 0;
        question.PrimaryCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;

        question.Input = "HELLO2";

        Assert.Contains(nameof(ConfirmationRequest.InputProblem), changed);
        Assert.Contains(nameof(ConfirmationRequest.CanAnswerPrimary), changed);
        Assert.Equal(1, canExecuteChanged);
    }
}
