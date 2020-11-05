using Obsidian.API.Plugins.Services.Common;
using Obsidian.API.Plugins.Services.Diagnostics;
using System.Security;

namespace Obsidian.API.Plugins.Services
{
    /// <summary>
    /// Represents a service used for process diagnoses.
    /// </summary>
    public interface IDiagnoser : ISecuredService
    {
        /// <summary>
        /// Gets a new <see cref="IProcess"/> component and associates it with the currently active process.
        /// </summary>
        /// <returns>A new <see cref="IProcess"/> component associated with the process resource that is running the calling application.</returns>
        /// <exception cref="SecurityException"></exception>
        public IProcess GetProcess();

        /// <summary>
        /// Creates a new <see cref="IProcess"/> component for each process resource on the local computer.
        /// </summary>
        /// <returns>An array of type <see cref="IProcess"/> that represents all the process resources running on the local computer.</returns>
        /// <exception cref="SecurityException"></exception>
        public IProcess[] GetProcesses();
    }
}
