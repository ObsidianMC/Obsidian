using Obsidian.API.Plugins.Services.Common;
using Obsidian.API.Plugins.Services.Diagnostics;
using System.Security;

namespace Obsidian.API.Plugins.Services;

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

    /// <summary>
    /// Starts a process resource by specifying the name of an application and a set of command-line arguments, and associates the resource with a new <see cref="IProcess"/> component.
    /// </summary>
    /// <param name="fileName">The application or document to start.</param>
    /// <param name="arguments">The set of command-line arguments to use when starting the application.</param>
    /// <param name="createWindow">Indicates whether to start the process in a new window.</param>
    /// <param name="useShell">Indicates whether to use the operating system shell to start the process.</param>
    /// <returns>A new <see cref="IProcess"/> that is associated with the process resource, or null if no process resource is started. </returns>
    public IProcess StartProcess(string fileName, string? arguments = null, bool createWindow = true, bool useShell = false);

    /// <summary>
    /// Returns a new instance of <see cref="IStopwatch"/>.
    /// </summary>
    public IStopwatch GetStopwatch();

    /// <summary>
    /// Returns a new instance of <see cref="IStopwatch"/> and starts it.
    /// </summary>
    public IStopwatch StartStopwatch();
}
