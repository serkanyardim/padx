using System.Windows.Threading;
using Paddock.Core.Services;

namespace Paddock.App.Services;

/// <summary>
/// Notices when the UI thread stops responding and writes to the log what the
/// app was doing at the time (<see cref="Activity"/>), and how long it took to
/// recover. Costs one tiny dispatcher call every few seconds.
/// </summary>
internal sealed class UiWatchdog : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private volatile bool _stopped;

    public UiWatchdog(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _thread = new Thread(Run) { IsBackground = true, Name = "padx UI watchdog" };
        _thread.Start();
    }

    private void Run()
    {
        while (!_stopped)
        {
            Thread.Sleep(Interval);

            using var answered = new ManualResetEventSlim();
            var started = DateTime.Now;
            _dispatcher.BeginInvoke(() => answered.Set(), DispatcherPriority.Send);

            if (answered.Wait(Limit) || _stopped)
                continue;

            var activity = Activity.Current;
            Log.Warn($"UI thread not responding for {Limit.TotalSeconds:0}+ s — busy with: {activity}.");

            answered.Wait();
            Log.Warn($"UI thread responding again after {(DateTime.Now - started).TotalSeconds:0} s (was: {activity}).");
        }
    }

    public void Dispose() => _stopped = true;
}
