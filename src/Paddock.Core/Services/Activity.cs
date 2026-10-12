namespace Paddock.Core.Services;

/// <summary>
/// A breadcrumb: what the app is busy with right now. Long operations set it,
/// and the UI watchdog reports it if the interface ever stops responding — so
/// a freeze in the log names its cause instead of leaving a guess.
/// </summary>
public static class Activity
{
    private static volatile string _current = "idle";

    public static string Current => _current;

    /// <summary>Marks the start of an operation; dispose the result to mark its end.</summary>
    public static IDisposable Begin(string what)
    {
        var previous = _current;
        _current = what;
        return new Scope(previous);
    }

    private sealed class Scope(string previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}
