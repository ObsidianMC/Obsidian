using Obsidian.API.Plugins.Services.IO;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.API.Plugins.Services
{
    /// <summary>
    /// Represents a service used for reading from files.s
    /// </summary>
    public interface IFileReader : IFileService
    {
        /// <summary>
        /// Opens an existing file for reading.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public IStream OpenRead(string path);

        /// <summary>
        /// Opens a binary file, reads the contents of the file into a byte array, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public byte[] ReadAllBytes(string path);

        /// <summary>
        /// Asynchronously opens a binary file, reads the contents of the file into a byte array, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a text file, reads all lines of the file, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public string[] ReadAllLines(string path);

        /// <summary>
        /// Asynchronously opens a text file, reads all lines of the file, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a text file, reads all the text in the file, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public string ReadAllText(string path);

        /// <summary>
        /// Asynchronously opens a text file, reads all the text in the file, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the names of files (including their paths) in the specified directory.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public string[] GetDirectoryFiles(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            return Directory.GetFiles(path);
        }

        /// <summary>
        /// Returns the names of subdirectories (including their paths) in the specified directory.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public string[] GetSubdirectories(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            return Directory.GetDirectories(path);
        }

        /// <summary>
        /// Creates a directory, that is used by default for relative paths.
        /// </summary>
        /// <param name="createOwnDirectory">If set to <b><c>false</c></b>, the automatically assigned directory for your plugin will be skipped.</param>
        /// <param name="skipFolderAutoGeneration">If set to <b><c>true</c></b>, skips the auto generation method for default plugin dir. Also, <b><c>createOwnDirectory</c></b> needs to be <b><c>true</c></b> for this to work.</param>
        /// <returns>Path to the created directory.</returns>
        public string CreateWorkingDirectory(bool createOwnDirectory = true, bool skipFolderAutoGeneration = false);
    }
}
