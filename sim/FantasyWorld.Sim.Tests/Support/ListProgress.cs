namespace FantasyWorld.Sim.Tests.Support;

/// <summary>Synchronous IProgress (System.Progress posts asynchronously).</summary>
public sealed class ListProgress<T> : IProgress<T>
{
    private readonly Action<T> _onReport;
    public ListProgress(Action<T> onReport) => _onReport = onReport;
    public void Report(T value) => _onReport(value);
}
