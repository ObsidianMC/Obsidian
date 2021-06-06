using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using Obsidian.API.Plugins.Services.IO;
using Obsidian.Plugins.ServiceProviders;
using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Plugins.Services
{
    public class FileWriterService : SecuredServiceBase, IFileWriter
    {
        internal override PluginPermissions NeededPermission => PluginPermissions.CanWrite;
        private string workingDirectory = null;

        public FileWriterService(PluginContainer plugin) : base(plugin)
        {
        }

        public void AppendText(string path, string value)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            File.AppendAllText(path, value);
        }

        public Task AppendTextAsync(string path, string value, CancellationToken cancellationToken = default)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            return File.AppendAllTextAsync(path, value, cancellationToken);
        }

        public IStream OpenWrite(string path)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            return new WritingStreamService(File.OpenWrite(path));
        }

        public void WriteAllBytes(string path, byte[] value)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            File.WriteAllBytes(path, value);
        }

        public Task WriteAllBytesAsync(string path, byte[] value, CancellationToken cancellationToken = default)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            return File.WriteAllBytesAsync(path, value, cancellationToken);
        }

        public void WriteAllLines(string path, string[] value)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            File.WriteAllLines(path, value);
        }

        public Task WriteAllLinesAsync(string path, string[] value, CancellationToken cancellationToken = default)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            return File.WriteAllLinesAsync(path, value, cancellationToken);
        }

        public void WriteAllText(string path, string value)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            File.WriteAllText(path, value);
        }

        public Task WriteAllTextAsync(string path, string value, CancellationToken cancellationToken = default)
        {
            if (!IsUsable)
                throw new SecurityException(IFileWriter.SecurityExceptionMessage);

            workingDirectory ??= GetWorkingDirectory();

            if (!Path.GetDirectoryName(Path.GetFullPath(path)).StartsWith(workingDirectory, true, null) && Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException(path);

            if (workingDirectory != null && !Path.IsPathFullyQualified(path))
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workingDirectory)), path);

            return File.WriteAllTextAsync(path, value, cancellationToken);
        }

        public string CreateWorkingDirectory(bool createOwnDirectory = true, bool skipFolderAutoGeneration = false)
        {
            workingDirectory = createOwnDirectory switch
            {
                true => Path.Combine(Path.GetDirectoryName(plugin.Source), plugin.Plugin.Info.Name.Replace(" ", "")),
                false => Path.GetDirectoryName(plugin.Source)
            };

            if (createOwnDirectory && !skipFolderAutoGeneration && !Directory.Exists(workingDirectory)) Directory.CreateDirectory(workingDirectory);

            return workingDirectory;
        }
        public string GetWorkingDirectory() => workingDirectory ?? CreateWorkingDirectory();
    }
}
