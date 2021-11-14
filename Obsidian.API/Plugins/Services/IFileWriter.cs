using Obsidian.API.Plugins.Services.IO;
﻿using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.API.Plugins.Services;

/// <summary>
/// Represents a service used for creating and writing to files.
/// </summary>
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
            throw new SecurityException(SecurityExceptionMessage);

        var workingDirectory = GetWorkingDirectory();
        var fullPath = Path.GetDirectoryName(Path.GetFullPath(path));
        var fullPathToCombine = Path.GetDirectoryName(Path.GetFullPath(workingDirectory));

        if (fullPath == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (fullPathToCombine == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (!fullPath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);
        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(fullPathToCombine, path);

        File.Create(path).Close();
    }

    /// <summary>
    /// Copies an existing file to a new file. Overwriting a file of the same name is allowed.
    /// </summary>
    /// <exception cref="SecurityException"></exception>
    public void CopyFile(string sourceFileName, string destinationFileName)
    {
        if (!IsUsable)
            throw new SecurityException(SecurityExceptionMessage);

        string workingDirectory = GetWorkingDirectory();
        var sourceFilePath = Path.GetDirectoryName(Path.GetFullPath(sourceFileName));
        var destinationFilePath = Path.GetDirectoryName(Path.GetFullPath(destinationFileName));
        var fullPathToCombine = Path.GetDirectoryName(Path.GetFullPath(workingDirectory));

        if (sourceFilePath == null || destinationFilePath == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (fullPathToCombine == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (!sourceFilePath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(sourceFileName)) throw new UnauthorizedAccessException(sourceFileName);
        if (workingDirectory != null && !Path.IsPathFullyQualified(sourceFileName))
            sourceFileName = Path.Combine(fullPathToCombine, sourceFileName);

        workingDirectory = GetWorkingDirectory();

        if (!destinationFilePath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(destinationFileName)) throw new UnauthorizedAccessException(destinationFileName);
        if (workingDirectory != null && !Path.IsPathFullyQualified(destinationFileName))
            destinationFileName = Path.Combine(fullPathToCombine, destinationFileName);

        File.Copy(sourceFileName, destinationFileName, overwrite: true);
    }

    /// <summary>
    /// Moves a specified file to a new location, providing the option to specify a new file name.
    /// </summary>
    /// <exception cref="SecurityException"></exception>
    public void MoveFile(string sourceFileName, string destinationFileName)
    {
        if (!IsUsable)
            throw new SecurityException(SecurityExceptionMessage);

        var workingDirectory = GetWorkingDirectory();
        var sourceFilePath = Path.GetDirectoryName(Path.GetFullPath(sourceFileName));
        var destinationFilePath = Path.GetDirectoryName(Path.GetFullPath(destinationFileName));
        var fullPathToCombine = Path.GetDirectoryName(Path.GetFullPath(workingDirectory));

        if (sourceFilePath == null || destinationFilePath == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (fullPathToCombine == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (!sourceFilePath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(sourceFileName)) throw new UnauthorizedAccessException(sourceFileName);
        if (workingDirectory != null && !Path.IsPathFullyQualified(sourceFileName))
            sourceFileName = Path.Combine(fullPathToCombine, sourceFileName);

        workingDirectory = GetWorkingDirectory();

        if (!destinationFilePath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(destinationFileName)) throw new UnauthorizedAccessException(destinationFileName);
        if (workingDirectory != null && !Path.IsPathFullyQualified(destinationFileName))
            destinationFileName = Path.Combine(fullPathToCombine, destinationFileName);

        File.Move(sourceFileName, destinationFileName);
    }

    /// <summary>
    /// Deletes the specified file.
    /// </summary>
    /// <exception cref="SecurityException"></exception>
    public void DeleteFile(string path)
    {
        if (!IsUsable)
            throw new SecurityException(SecurityExceptionMessage);

        var workingDirectory = GetWorkingDirectory();
        var fullPath = Path.GetDirectoryName(Path.GetFullPath(path));
        var fullPathToCombine = Path.GetDirectoryName(Path.GetFullPath(workingDirectory));

        if (fullPath == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (fullPathToCombine == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (!fullPath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);
        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(fullPathToCombine, path);

        File.Delete(path);
    }

    /// <summary>
    /// Creates all directories and subdirectories in the specified path unless they already exist.
    /// </summary>
    /// <exception cref="SecurityException"></exception>
    public void CreateDirectory(string path)
    {
        if (!IsUsable)
            throw new SecurityException(SecurityExceptionMessage);

        var workingDirectory = GetWorkingDirectory();
        var fullPath = Path.GetDirectoryName(Path.GetFullPath(path));
        var fullPathToCombine = Path.GetDirectoryName(Path.GetFullPath(workingDirectory));

        if (fullPath == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (fullPathToCombine == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (!fullPath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);
        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(fullPathToCombine, path);

        Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Deletes the specified directory and, if indicated, any subdirectories and files in the directory.
    /// </summary>
    /// <exception cref="SecurityException"></exception>
    public void DeleteDirectory(string path)
    {
        if (!IsUsable)
            throw new SecurityException(SecurityExceptionMessage);

        var workingDirectory = GetWorkingDirectory();
        var fullPath = Path.GetDirectoryName(Path.GetFullPath(path));
        var fullPathToCombine = Path.GetDirectoryName(Path.GetFullPath(workingDirectory));

        if (fullPath == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (fullPathToCombine == null)
        {
            throw new DirectoryNotFoundException();
        }

        if (!fullPath.StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);
        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(fullPathToCombine, path);

        Directory.Delete(path, recursive: true);
    }
}
