using System.Diagnostics;
using System.Linq;

namespace Obsidian.Utilities
{
    public sealed class TpsMeasure
    {
        public int Tps => _tps;
        private int _tps;

        private long[] buffer = new long[20];
        private int bufferIndex;

        public void PushMeasurement(long ticks)
        {
            buffer[bufferIndex++] = ticks;
            if (bufferIndex == buffer.Length)
                bufferIndex = 0;

            _tps = (int)(buffer.Average() * 1000L / Stopwatch.Frequency);
        }
    }
}
