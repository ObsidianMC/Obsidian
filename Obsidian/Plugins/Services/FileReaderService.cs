using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using Obsidian.API.Plugins.Services.IO;
using Obsidian.Plugins.ServiceProviders;
using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Plugins.Services;

public class FileReaderService : SecuredServiceBase, IFileReader
{
    internal override PluginPermissions NeededPermission => PluginPermissions.CanRead;
    private string workingDirectory = null;

    public FileReaderService(PluginContainer plugin) : base(plugin)
    {
    }

    public IStream OpenRead(string path)
    {
        if (!IsUsable)
            throw new SecurityException(IFileReader.SecurityExceptionMessage);

        workingDirectory ??= GetWorkingDirectory();

        if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

        return new ReadingStreamService(File.OpenRead(path));
    }

    public byte[] ReadAllBytes(string path)
    {
        if (!IsUsable)
            throw new SecurityException(IFileReader.SecurityExceptionMessage);

        workingDirectory ??= GetWorkingDirectory();

        if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

        return File.ReadAllBytes(path);
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsUsable)
            throw new SecurityException(IFileReader.SecurityExceptionMessage);

        workingDirectory ??= GetWorkingDirectory();

        if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

        return File.ReadAllBytesAsync(path, cancellationToken);
    }

    public string[] ReadAllLines(string path)
    {
        if (!IsUsable)
            throw new SecurityException(IFileReader.SecurityExceptionMessage);

        workingDirectory ??= GetWorkingDirectory();

        if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

        return File.ReadAllLines(path);
    }

    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsUsable)
            throw new SecurityException(IFileReader.SecurityExceptionMessage);

        workingDirectory ??= GetWorkingDirectory();

        if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

        return File.ReadAllLinesAsync(path, cancellationToken);
    }

    public string ReadAllText(string path)
    {
        if (!IsUsable)
            throw new SecurityException(IFileReader.SecurityExceptionMessage);

        workingDirectory ??= GetWorkingDirectory();

        if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

        return File.ReadAllText(path);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsUsable)
            throw new SecurityException(IFileReader.SecurityExceptionMessage);

        workingDirectory ??= GetWorkingDirectory();

        if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

        if (workingDirectory != null && !Path.IsPathFullyQualified(path))
            path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

        return File.ReadAllTextAsync(path, cancellationToken);
    }

    public string CreateWorkingDirectory(bool createOwnDirectory = true, bool skipFolderAutoGeneration = false)
    {
        workingDirectory = createOwnDirectory switch
        {
            true => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(plugin.Source)), plugin.Plugin.Info.Name.Replace(" ", "")),
            false => Path.GetDirectoryName(Path.GetFullPath(plugin.Source))
        };

        if (createOwnDirectory && !skipFolderAutoGeneration && !Directory.Exists(workingDirectory)) Directory.CreateDirectory(workingDirectory);

        return workingDirectory;
    }
    public string GetWorkingDirectory() => workingDirectory ?? CreateWorkingDirectory();
}
