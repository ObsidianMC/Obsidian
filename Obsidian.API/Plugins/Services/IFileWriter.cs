using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Obsidian.API.Plugins.Services.Common;

namespace Obsidian.API.Plugins.Services
{
    public interface IFileWriter : IFileService
    {
        /// <summary>
        /// Opens an existing file or creates a new file for writing.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public IStream OpenWrite(string path);

        /// <summary>
        /// Creates a new file, writes the specified byte array to the file, and then closes the file. If the target file already exists, it is overwritten.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void WriteAllBytes(string path, byte[] value);

        /// <summary>
        /// Asynchronously creates a new file, writes the specified byte array to the file, and then closes the file. If the target file already exists, it is overwritten.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public Task WriteAllBytesAsync(string path, byte[] value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new file, write the specified string array to the file, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void WriteAllLines(string path, string[] value);

        /// <summary>
        /// Creates a new file, writes the specified string array to the file by using the specified encoding, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public Task WriteAllLinesAsync(string path, string[] value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new file, writes the specified string to the file, and then closes the file. If the target file already exists, it is overwritten.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void WriteAllText(string path, string value);

        /// <summary>
        /// Asynchronously creates a new file, writes the specified string to the file, and then closes the file. If the target file already exists, it is overwritten.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public Task WriteAllTextAsync(string path, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a file, appends the specified string to the file, and then closes the file. If the file does not exist, this method creates a file, writes the specified string to the file, then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void AppendText(string path, string value);

        /// <summary>
        /// Asynchronously opens a file or creates a file if it does not already exist, appends the specified string to the file, and then closes the file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public Task AppendTextAsync(string path, string value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates or overwrites a file in the specified path.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void CreateFile(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            File.Create(path);
        }

        /// <summary>
        /// Copies an existing file to a new file. Overwriting a file of the same name is allowed.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void CopyFile(string sourceFileName, string destinationFileName)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            File.Copy(sourceFileName, destinationFileName, overwrite: true);
        }

        /// <summary>
        /// Moves a specified file to a new location, providing the option to specify a new file name.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void MoveFile(string sourceFileName, string destinationFileName)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            File.Move(sourceFileName, destinationFileName);
        }

        /// <summary>
        /// Deletes the specified file.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void DeleteFile(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            File.Delete(path);
        }

        /// <summary>
        /// Creates all directories and subdirectories in the specified path unless they already exist.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void CreateDirectory(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            Directory.CreateDirectory(path);
        }

        /// <summary>
        /// Deletes the specified directory and, if indicated, any subdirectories and files in the directory.
        /// </summary>
        /// <exception cref="SecurityException"></exception>
        public void DeleteDirectory(string path)
        {
            if (!IsUsable)
                throw new SecurityException(securityExceptionMessage);

            Directory.Delete(path, recursive: true);
        }
    }
}
