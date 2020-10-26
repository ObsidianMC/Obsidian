using Obsidian.API.Plugins.Services.Common;
using System;
using System.Security;
using System.Text;

namespace Obsidian.API.Plugins.Services
{
    public interface INativeLoader : ISecuredService
    {
        /// <summary>
        /// Attempts to load exported function from native library.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public T LoadMethod<T>(string libraryPath) where T : Delegate;

        /// <summary>
        /// Attempts to load exported function with specific string encoding from native library.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public T LoadMethod<T>(string libraryPath, Encoding stringEncoding) where T : Delegate;

        /// <summary>
        /// Attempts to load exported function from native library.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public T LoadMethod<T>(string libraryPath, string name) where T : Delegate;

        /// <summary>
        /// Attempts to load exported function with specific encoding from native library.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public T LoadMethod<T>(string libraryPath, string name, Encoding stringEncoding) where T : Delegate;
    }
}
