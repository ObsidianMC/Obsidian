using System;
using System.Security;

namespace Obsidian.API.Plugins.Services.Diagnostics
{
    public interface IProcess : IDisposable
    {
        /// <summary>
        /// Gets the name of the process.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the unique identifier for the associated process.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the time that the associated process was started.
        /// </summary>
        public DateTime StartTime { get; }

        /// <summary>
        /// Gets the time that the associated process exited.
        /// </summary>
        public DateTime ExitTime { get; }

        /// <summary>
        /// Gets the number of threads that are running in the associated process.
        /// </summary>
        public int ThreadCount { get; }

        /// <summary>
        /// Gets the amount of the virtual memory, in bytes, allocated for the associated process.
        /// </summary>
        public long VirtualMemorySize { get; }

        /// <summary>
        /// Gets the maximum amount of virtual memory, in bytes, used by the associated process.
        /// </summary>
        public long PeakVirtualMemorySize { get; }

        /// <summary>
        /// Gets the amount of paged memory, in bytes, allocated for the associated process.
        /// </summary>
        public long PagedMemorySize { get; }

        /// <summary>
        /// Gets the amount of nonpaged system memory, in bytes, allocated for the associated process.
        /// </summary>
        public long NonpagedSystemMemorySize { get; }

        /// <summary>
        /// Gets the amount of pageable system memory, in bytes, allocated for the associated process.
        /// </summary>
        public long PagedSystemMemorySize { get; }

        /// <summary>
        /// Gets the maximum amount of memory in the virtual memory paging file, in bytes, used by the associated process.
        /// </summary>
        public long PeakPagedMemorySize { get; }

        /// <summary>
        /// Gets the amount of private memory, in bytes, allocated for the associated process.
        /// </summary>
        public long PrivateMemorySize { get; }

        /// <summary>
        /// Gets the amount of physical memory, in bytes, allocated for the associated process.
        /// </summary>
        public long WorkingSet { get; }

        /// <summary>
        /// Gets the maximum amount of physical memory, in bytes, used by the associated process.
        /// </summary>
        public long PeakWorkingSet { get; }

        /// <summary>
        /// Frees all the resources that are associated with this component.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void Close();

        /// <summary>
        /// Immediately stops the associated process.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void Kill();
    }
}
