using CommunityToolkit.Mvvm.Input;

namespace LizTerm.App.Tests;

/// <summary>CommunityToolkit's ExecuteAsync ignores CanExecute; hotkeys reach commands through this guard.</summary>
public class CommandRoutingTests
{
    [Fact]
    public async Task Async_commands_run_only_when_they_can()
    {
        var ran = 0;
        var blocked = new AsyncRelayCommand(() => { ran++; return Task.CompletedTask; }, () => false);
        var allowed = new AsyncRelayCommand(() => { ran++; return Task.CompletedTask; }, () => true);
        await CommandRouting.TryExecuteAsync(blocked);
        Assert.Equal(0, ran);
        await CommandRouting.TryExecuteAsync(allowed);
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task Parameterized_and_plain_commands_are_guarded_too()
    {
        var seen = new List<string>();
        var typed = new AsyncRelayCommand<string>(s => { seen.Add(s!); return Task.CompletedTask; }, s => s != "no");
        await CommandRouting.TryExecuteAsync(typed, "no");
        await CommandRouting.TryExecuteAsync(typed, "yes");
        var plain = new RelayCommand(() => seen.Add("plain"), () => false);
        CommandRouting.TryExecute(plain);
        Assert.Equal(["yes"], seen);
    }
}
