using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace OpenExplorer_UI.Features.Performance;

public readonly record struct FrameMetricsSnapshot(
    double CurrentFps,
    double AverageFrameTimeMilliseconds,
    double MaximumFrameTimeMilliseconds,
    int SampleCount);

public sealed class FrameMetricsCollector : IDisposable
{
    private const int SampleCapacity = 120;
    private readonly double[] frameTimes = new double[SampleCapacity];
    private long previousTimestamp;
    private long lastPublishedTimestamp;
    private int nextSampleIndex;
    private int sampleCount;
    private bool isSubscribed;

    public event EventHandler<FrameMetricsSnapshot>? MetricsUpdated;

    public void Start()
    {
        if (isSubscribed)
        {
            return;
        }

        previousTimestamp = Stopwatch.GetTimestamp();
        lastPublishedTimestamp = previousTimestamp;
        CompositionTarget.Rendering += OnRendering;
        isSubscribed = true;
    }

    public void Stop()
    {
        if (!isSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        isSubscribed = false;
    }

    public void Dispose() => Stop();

    private void OnRendering(object? sender, object args)
    {
        long timestamp = Stopwatch.GetTimestamp();
        double frameTimeMilliseconds = (timestamp - previousTimestamp) * 1000.0 / Stopwatch.Frequency;
        previousTimestamp = timestamp;

        if (frameTimeMilliseconds <= 0 || frameTimeMilliseconds > 1000)
        {
            return;
        }

        frameTimes[nextSampleIndex] = frameTimeMilliseconds;
        nextSampleIndex = (nextSampleIndex + 1) % frameTimes.Length;
        sampleCount = Math.Min(sampleCount + 1, frameTimes.Length);

        if (timestamp - lastPublishedTimestamp < Stopwatch.Frequency)
        {
            return;
        }

        lastPublishedTimestamp = timestamp;
        double average = 0;
        double maximum = 0;
        for (int index = 0; index < sampleCount; index++)
        {
            average += frameTimes[index];
            maximum = Math.Max(maximum, frameTimes[index]);
        }

        average /= sampleCount;
        MetricsUpdated?.Invoke(
            this,
            new FrameMetricsSnapshot(1000.0 / average, average, maximum, sampleCount));
    }
}
