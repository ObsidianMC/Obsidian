using Obsidian.API.Plugins.Services.Diagnostics;
using System;
using System.Diagnostics;

namespace Obsidian.Plugins.Services
{
    public class StopwatchService : IStopwatch
    {
        private readonly Stopwatch stopwatch;

        public StopwatchService(bool start)
        {
            stopwatch = start ? Stopwatch.StartNew() : new Stopwatch();
        }

        public TimeSpan Elapsed => stopwatch.Elapsed;

        public long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds;

        public long ElapsedTicks => stopwatch.ElapsedTicks;

        public bool IsRunning => stopwatch.IsRunning;

        public void Reset()
        {
            stopwatch.Reset();
        }

        public void Restart()
        {
            stopwatch.Restart();
        }

        public void Start()
        {
            stopwatch.Start();
        }

        public void Stop()
        {
            stopwatch.Stop();
        }
    }
}
