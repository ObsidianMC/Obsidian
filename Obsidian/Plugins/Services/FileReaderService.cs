using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using Obsidian.API.Plugins.Services.Common;
using Obsidian.Plugins.ServiceProviders;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Plugins.Services
{
    public class FileReaderService : SecuredServiceBase, IFileReader
    {
        internal override PluginPermissions NeededPermission => PluginPermissions.CanRead;

        public FileReaderService(PluginContainer plugin) : base(plugin)
        {
        }

        public IStream OpenRead(string path)
        {
            if (!IsUsable)
                throw new SecurityException(IFileReader.securityExceptionMessage);

            return new ReadingStreamService(File.OpenRead(path));
        }

        public byte[] ReadAllBytes(string path)
        {
            if (!IsUsable)
                throw new SecurityException(IFileReader.securityExceptionMessage);

            return File.ReadAllBytes(path);
        }

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!IsUsable)
                throw new SecurityException(IFileReader.securityExceptionMessage);

            return File.ReadAllBytesAsync(path, cancellationToken);
        }

        public string[] ReadAllLines(string path)
        {
            if (!IsUsable)
                throw new SecurityException(IFileReader.securityExceptionMessage);

            return File.ReadAllLines(path);
        }

        public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!IsUsable)
                throw new SecurityException(IFileReader.securityExceptionMessage);

            return File.ReadAllLinesAsync(path, cancellationToken);
        }

        public string ReadAllText(string path)
        {
            if (!IsUsable)
                throw new SecurityException(IFileReader.securityExceptionMessage);

            return File.ReadAllText(path);
        }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!IsUsable)
                throw new SecurityException(IFileReader.securityExceptionMessage);

            return File.ReadAllTextAsync(path, cancellationToken);
        }
    }
}
