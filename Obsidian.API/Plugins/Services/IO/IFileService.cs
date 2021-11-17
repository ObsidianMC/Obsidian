using Obsidian.API.Plugins.Services.Common;
using System.IO;
using System.Security;

namespace Obsidian.API.Plugins.Services.IO;

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
            throw new SecurityException(SecurityExceptionMessage);

        string workingDirectory = GetWorkingDirectory();
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

        return File.Exists(path);
    }

    /// <summary>
    /// Determines whether the given path refers to an existing directory on disk.
    /// </summary>
    /// <exception cref="SecurityException"></exception>
    public bool DirectoryExists(string path)
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

        return Directory.Exists(path);
    }

    /// <summary>
    /// Combines an array of strings into a path.
    /// </summary>
    public string CombinePath(params string[] paths)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
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
                paths[i] = Path.Combine(fullPathToCombine, path); ;
        }

        return Path.Combine(paths);
    }

    /// <summary>
    /// Returns the extension (including the period ".") of the specified path string.
    /// </summary>
    public string GetExtension(string path)
    {
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

        return Path.GetExtension(path);
    }

    /// <summary>
    /// Returns the file name and extension of the specified path string.
    /// </summary>
    public string GetFileName(string path)
    {
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

        return Path.GetFileName(path);
    }

    /// <summary>
    /// Returns the file name of the specified path string without the extension.
    /// </summary>
    public string GetFileNameWithoutExtension(string path)
    {
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

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// Returns the absolute path for the specified path string.
    /// </summary>
    public string GetFullPath(string path)
    {
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

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Creates a directory, that is used by default for relative paths.
    /// </summary>
    /// <param name="createOwnDirectory">If set to <b><c>false</c></b>, the automatically assigned directory for your plugin will be skipped.</param>
    /// <param name="skipFolderAutoGeneration">If set to <b><c>true</c></b>, skips the auto generation method for default plugin dir. Also, <b><c>createOwnDirectory</c></b> needs to be <b><c>true</c></b> for this to work.</param>
    /// <returns>Path to the created directory.</returns>
    public string CreateWorkingDirectory(bool createOwnDirectory = true, bool skipFolderAutoGeneration = false);

    /// <summary>
    /// Returns with the working directory.
    /// </summary>
    /// <returns>Path to the created directory.</returns>
    public string GetWorkingDirectory();
}
