using System.Diagnostics;

namespace Obsidian.Utilities;

public sealed class TpsMeasure
{
    public int Tps => _tps;
    private int _tps;

    private long[] buffer = new long[10];
    private int bufferIndex;

    public void PushMeasurement(long ticks)
    {
        buffer[bufferIndex++] = ticks;
        if (bufferIndex == buffer.Length)
            bufferIndex = 0;

        _tps = (int)(1000d / (buffer.Average() * 1000d / Stopwatch.Frequency));
    }
}
