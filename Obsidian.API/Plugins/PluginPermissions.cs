using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Represents permissions for performing specific types of actions.
    /// </summary>
    [Flags]
    public enum PluginPermissions : byte
    {
        None = 0,

        /// <summary>
        /// Allows writing to files.
        /// </summary>
        CanWrite = 1,

        /// <summary>
        /// Allows reading from files.
        /// </summary>
        CanRead = 2,

        /// <summary>
        /// Allows working with files.
        /// </summary>
        FileAccess = 3,

        /// <summary>
        /// Allows doing actions over network.
        /// </summary>
        NetworkAccess = 4,

        /// <summary>
        /// Allows using native libraries.
        /// </summary>
        Interop = 8,

        /// <summary>
        /// Allows performing reflection.
        /// </summary>
        Reflection = 16,

        /// <summary>
        /// Allows using System.Diagnostics and System.Runtime.Loader libraries.
        /// </summary>
        RunningSubprocesses = 32,

        /// <summary>
        /// Allows using Microsoft.CodeAnalysis and related libraries.
        /// </summary>
        Compilation = 64,

        /// <summary>
        /// Allows using 3rd party libraries.
        /// </summary>
        ThirdPartyLibraries = 128,

        All = 255
    }
}
