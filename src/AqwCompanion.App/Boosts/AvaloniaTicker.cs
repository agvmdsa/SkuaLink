using Avalonia.Threading;

namespace AqwCompanion.App.Boosts;

/// <summary>The 1 s ticker of the boosts card, a <see cref="DispatcherTimer"/>; it must be created on the UI thread.</summary>
internal static class AvaloniaTicker
{
    public static IDisposable EverySecond(Action tick)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        EventHandler handler = (_, _) =>
        {
            try
            {
                tick();
            }
            catch (Exception)
            {
                // A failed refresh must not stop the timer or reach the dispatcher.
            }
        };
        timer.Tick += handler;
        timer.Start();
        return new Stop(() =>
        {
            timer.Stop();
            timer.Tick -= handler;
        });
    }

    private sealed class Stop(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}
