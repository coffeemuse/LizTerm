using Avalonia.Controls;
using Avalonia.Threading;
using LizTerm.App.Startup;

namespace LizTerm.App.Views;

/// <summary>Spec 7: closes on the first click or key, never before the minimum, and at the maximum on its own.</summary>
public partial class SplashWindow : Window
{
    private readonly SplashTiming _timing;
    private readonly DateTime _shownAt = DateTime.UtcNow;
    private readonly DispatcherTimer _timer = new();
    private bool _dismissed;

    public SplashWindow() : this(AppVersion.Current, SplashTiming.Default) { }

    public SplashWindow(string version, SplashTiming timing)
    {
        InitializeComponent();
        _timing = timing;
        VersionText.Text = "Version " + version;
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Opened += (_, _) => Arm(null);
        PointerPressed += (_, _) => Dismiss();
        KeyDown += (_, _) => Dismiss();
        Closed += (_, _) => _timer.Stop();
    }

    private void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        Arm(DateTime.UtcNow);
    }

    private void Arm(DateTime? dismissRequestedAt)
    {
        _timer.Stop();
        var delay = _timing.CloseAt(_shownAt, dismissRequestedAt) - DateTime.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            Close();
            return;
        }
        _timer.Interval = delay;
        _timer.Start();
    }
}
