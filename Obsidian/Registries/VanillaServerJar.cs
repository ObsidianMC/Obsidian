using Microsoft.Extensions.Logging;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// Extracts vanilla data that Mojang's EULA doesn't let Obsidian redistribute from the official server jar, which is
/// downloaded from Mojang (through its version manifest) on first use and cached per version.
/// </summary>
internal static partial class VanillaServerJar
{
    private const string StructurePrefix = "data/minecraft/structure/";

    /// <summary>
    /// Makes sure the structure templates of <paramref name="version"/> (the jar's <c>data/minecraft/structure</c>) are
    /// extracted under <paramref name="cacheDirectory"/>, downloading the server jar when they aren't.
    /// </summary>
    /// <returns>The directory of the templates, for <see cref="StructureRegistry.Initialize"/>.</returns>
    public static async Task<string> ExtractStructuresAsync(HttpClient httpClient, string cacheDirectory, string version, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var versionDirectory = Path.GetFullPath(Path.Combine(cacheDirectory, version));
        var structureDirectory = Path.Combine(versionDirectory, "structure");
        // Written once every template is extracted, so an interrupted extraction is done again.
        var marker = Path.Combine(versionDirectory, "structure.complete");
        if (File.Exists(marker))
        {
            return structureDirectory;
        }

        Log.Downloading(logger, version);
        Directory.CreateDirectory(versionDirectory);
        // The jar is only needed while extracting.
        var jarPath = Path.Combine(versionDirectory, $"server-{version}.jar.tmp");
        try
        {
            await MojangDownloads.DownloadAsync(httpClient, version, "server", jarPath, cancellationToken);

            if (Directory.Exists(structureDirectory))
            {
                Directory.Delete(structureDirectory, recursive: true);
            }

            var count = ExtractStructures(jarPath, version, structureDirectory);
            await File.WriteAllTextAsync(marker, string.Empty, cancellationToken);
            Log.Extracted(logger, count, version);
        }
        finally
        {
            File.Delete(jarPath);
        }

        return structureDirectory;
    }

    /// <summary>Extracts templates through a temporary directory and returns their count.</summary>
    private static int ExtractStructures(string jarPath, string version, string structureDirectory)
    {
        using var bundle = ZipFile.OpenRead(jarPath);
        using var server = OpenServerJar(bundle, version);

        var extracting = Path.GetFullPath(structureDirectory + ".tmp");
        if (Directory.Exists(extracting))
        {
            Directory.Delete(extracting, recursive: true);
        }

        var count = 0;
        foreach (var entry in server.Entries)
        {
            if (!entry.FullName.StartsWith(StructurePrefix, StringComparison.Ordinal) || !entry.FullName.EndsWith(".nbt", StringComparison.Ordinal))
            {
                continue;
            }

            // Entries can't write outside the directory, whatever their names.
            var destination = Path.GetFullPath(Path.Combine(extracting, entry.FullName[StructurePrefix.Length..]));
            if (!destination.StartsWith(extracting + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The server jar of {version} has a template outside its structure directory: {entry.FullName}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination);
            count++;
        }

        Directory.Move(extracting, structureDirectory);
        return count;
    }

    /// <summary>
    /// Opens the server jar bundled in the downloaded jar: since 1.18 the download is a bundler, which lists the jar in
    /// <c>META-INF/versions.list</c> as <c>sha256 \t id \t path</c> under <c>META-INF/versions/</c>.
    /// </summary>
    private static ZipArchive OpenServerJar(ZipArchive bundle, string version)
    {
        var list = bundle.GetEntry("META-INF/versions.list")
            ?? throw new InvalidDataException($"The server jar of {version} isn't a bundler jar.");
        string? path = null;
        using (var reader = new StreamReader(list.Open()))
        {
            while (reader.ReadLine() is string line)
            {
                var fields = line.Split('\t');
                if (fields.Length == 3 && fields[1] == version)
                {
                    path = fields[2];
                }
            }
        }

        var entry = (path is null ? null : bundle.GetEntry("META-INF/versions/" + path))
            ?? throw new InvalidDataException($"The server jar of {version} doesn't bundle the server.");
        // A zip archive needs a seekable stream, which the bundled entry isn't.
        var buffer = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(buffer);
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    /// <summary>Structured log messages for vanilla structure extraction.</summary>
    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Downloading the vanilla {Version} server jar from Mojang to extract its structure templates")]
        public static partial void Downloading(ILogger logger, string version);
        [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {Count} structure templates from the vanilla {Version} server jar")]
        public static partial void Extracted(ILogger logger, int count, string version);
    }
}
