using Avalonia;
using Avalonia.Headless;
using LizTerm.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LizTerm.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LizTerm.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
