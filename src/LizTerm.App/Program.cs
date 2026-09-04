using Avalonia;

namespace LizTerm.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
#if DEBUG
        // AvaloniaUI Developer Tools (avdt): press F12 in a debug build, or attach through the
        // avalonia_devtools MCP server declared in .mcp.json. Not compiled into Release.
        builder = builder.WithDeveloperTools();
#endif
        return builder;
    }
}
