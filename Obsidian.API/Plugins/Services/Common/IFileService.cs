using System.IO;
using System.Security;

namespace Obsidian.API.Plugins.Services.Common
{
    /// <summary>
    /// Provides the base interface for file services.
    /// </summary>
    public interface IFileService : ISecuredService
    {
        /// <summary>
        /// Determines whether the specified file exists.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public bool FileExists(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            return File.Exists(path);
        }

        /// <summary>
        /// Determines whether the given path refers to an existing directory on disk.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public bool DirectoryExists(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            return Directory.Exists(path);
        }

        /// <summary>
        /// Combines an array of strings into a path.
        /// </summary>
        public string CombinePath(params string[] paths)
        {
            return Path.Combine(paths);
        }

        /// <summary>
        /// Returns the extension (including the period ".") of the specified path string.
        /// </summary>
        public string GetExtension(string path)
        {
            return Path.GetExtension(path);
        }

        /// <summary>
        /// Returns the file name and extension of the specified path string.
        /// </summary>
        public string GetFileName(string path)
        {
            return Path.GetFileName(path);
        }

        /// <summary>
        /// Returns the file name of the specified path string without the extension.
        /// </summary>
        public string GetFileNameWithoutExtension(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// Returns the absolute path for the specified path string.
        /// </summary>
        public string GetFullPath(string path)
        {
            return Path.GetFullPath(path);
        }
    }
}
