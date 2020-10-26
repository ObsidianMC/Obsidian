using System;

namespace Obsidian.API.Plugins
{
    [Flags]
    public enum PluginPermissions : byte
    {
        None = 0,

        /// <summary>
        /// Allows writing to files
        /// </summary>
        CanWrite = 1,

        /// <summary>
        /// Allows reading from files
        /// </summary>
        CanRead = 2,

        /// <summary>
        /// Allows using System.IO library
        /// </summary>
        FileAccess = 3,

        /// <summary>
        /// Allows using System.Net library
        /// </summary>
        InternetAccess = 4,

        /// <summary>
        /// Allows using System.Runtime.InteropServices library
        /// </summary>
        Interop = 8,

        /// <summary>
        /// Allows using System.Reflection library
        /// </summary>
        Reflection = 16,

        /// <summary>
        /// Allows using System.Diagnostics and System.Runtime.Loader library
        /// </summary>
        RunningSubprocesses = 32,

        /// <summary>
        /// Allows using Microsoft.CSharp library
        /// </summary>
        Compilation = 64,

        /// <summary>
        /// Allows using 3rd party libraries
        /// </summary>
        ThirdPartyLibraries = 128,

        All = 255
    }
}
