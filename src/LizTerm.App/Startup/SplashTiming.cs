namespace LizTerm.App.Startup;

/// <summary>When the splash closes: at the first click or key, but never before <see cref="Minimum"/> after it
/// was shown, and at <see cref="Maximum"/> with no input at all (spec 7).</summary>
public sealed class SplashTiming(TimeSpan minimum, TimeSpan maximum)
{
    public static readonly SplashTiming Default = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5));

    public TimeSpan Minimum { get; } = minimum;
    public TimeSpan Maximum { get; } = maximum;

    public DateTime CloseAt(DateTime shownAt, DateTime? dismissRequestedAt)
    {
        if (dismissRequestedAt is not { } requested) return shownAt + Maximum;
        var earliest = shownAt + Minimum;
        return requested > earliest ? requested : earliest;
    }
}
