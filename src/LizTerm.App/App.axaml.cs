using Avalonia;
using Avalonia.Markup.Xaml;

namespace LizTerm.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
