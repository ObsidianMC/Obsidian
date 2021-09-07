using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Utilities
{
    public sealed class BalancingTimer
    {
        public int Interval => _interval;
        public CancellationToken CancellationToken => _cancellationToken;

        private readonly int _interval;
        private readonly CancellationToken _cancellationToken;

        private readonly Stopwatch stopwatch = new();
        private readonly long ticksInterval; // Number of Stopwatch ticks equal to the interval

        private long delay = 0;  // Measured in ticks

        public BalancingTimer(int intervalInMilliseconds) : this(intervalInMilliseconds, CancellationToken.None)
        {
        }

        public BalancingTimer(int intervalInMilliseconds, CancellationToken cancellationToken)
        {
            if (intervalInMilliseconds < 1)
                throw new ArgumentOutOfRangeException(nameof(intervalInMilliseconds));

            _interval = intervalInMilliseconds;
            _cancellationToken = cancellationToken;

            ticksInterval = intervalInMilliseconds * Stopwatch.Frequency / 1000L;
        }

        public async ValueTask<bool> WaitForNextTickAsync()
        {
            if (_cancellationToken.IsCancellationRequested)
                return false;

            long delta = stopwatch.ElapsedTicks;
            stopwatch.Restart();

            // Measure delay
            delay += delta - ticksInterval;

            if (delay < 0)
            {
                // Wait for the extra time
                int extraTimeInMilliseconds = (int)(-delay * 1000L / Stopwatch.Frequency);
                await Task.Delay(extraTimeInMilliseconds, _cancellationToken);
            }

            return true;
        }
    }
}
