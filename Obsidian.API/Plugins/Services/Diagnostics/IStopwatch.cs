using Obsidian.API.Plugins.Services.Common;
using System;

namespace Obsidian.API.Plugins.Services.Diagnostics
{
    public interface IStopwatch : IService
    {
        /// <summary>
        /// Gets the total elapsed time measured by the current instance.
        /// </summary>
        public TimeSpan Elapsed { get; }

        /// <summary>
        /// Gets the total elapsed time measured by the current instance, in milliseconds.
        /// </summary>
        public long ElapsedMilliseconds { get; }

        /// <summary>
        /// Gets the total elapsed time measured by the current instance, in timer ticks.
        /// </summary>
        public long ElapsedTicks { get; }

        /// <summary>
        /// Gets a value indicating whether the <see cref="IStopwatch"/> timer is running.
        /// </summary>
        public bool IsRunning { get; }

        /// <summary>
        /// Starts, or resumes, measuring elapsed time for an interval.
        /// </summary>
        public void Start();

        /// <summary>
        /// Stops measuring elapsed time for an interval.
        /// </summary>
        public void Stop();

        /// <summary>
        /// Stops time interval measurement and resets the elapsed time to zero.
        /// </summary>
        public void Reset();

        /// <summary>
        /// Stops time interval measurement, resets the elapsed time to zero, and starts measuring elapsed time.
        /// </summary>
        public void Restart();
    }
}
