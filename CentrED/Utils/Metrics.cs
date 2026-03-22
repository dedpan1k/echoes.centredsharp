namespace CentrED.Utils;

/// <summary>
/// Collects named timing measurements for editor subsystems.
/// </summary>
public class Metrics
{
    /// <summary>
    /// Stores the most recent elapsed time recorded for each metric name.
    /// </summary>
    public Dictionary<string, TimeSpan> Timers = new();

    // Tracks the start time for metrics that are currently being measured.
    private readonly Dictionary<string, DateTime> starts = new();
    
    /// <summary>
    /// Sets the last recorded elapsed time for a metric.
    /// </summary>
    /// <param name="name">The metric name.</param>
    public TimeSpan this[string name]
    {
        set => Timers[name] = value;
    }

    /// <summary>
    /// Starts timing the specified metric.
    /// </summary>
    /// <param name="name">The metric name.</param>
    public void Start(String name)
    {
       starts[name] = DateTime.Now;
    }

    /// <summary>
    /// Stops timing the specified metric and stores the elapsed duration.
    /// </summary>
    /// <param name="name">The metric name.</param>
    public void Stop(String name)
    {
        Timers[name] = DateTime.Now - starts[name];
    }

    /// <summary>
    /// Measures the execution time of a callback and stores it under the specified metric name.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="callback">The work to time.</param>
    public void Measure(String name, Action callback)
    {
        Start(name);
        callback();
        Stop(name);
    }
}