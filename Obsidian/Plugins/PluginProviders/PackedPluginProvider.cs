using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Org.BouncyCastle.Crypto;
using System.Buffers;
using System.Collections.Frozen;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Obsidian.Plugins.PluginProviders;
public sealed class PackedPluginProvider(PluginManager pluginManager, ILogger logger)
{
    private readonly PluginManager pluginManager = pluginManager;
    private readonly ILogger logger = logger;

    public async Task<PluginContainer> GetPluginAsync(string path)
    {
        await using var fs = new FileStream(path, FileMode.Open);
        using var reader = new BinaryReader(fs);

        var header = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (header != "OBBY")
            throw new InvalidOperationException("Plugin file does not begin with the proper header.");

        //TODO save api version somewhere
        var apiVersion = reader.ReadString();

        var hash = reader.ReadBytes(20);
        var signature = reader.ReadBytes(256);
        var dataLength = reader.ReadInt32();

        var curPos = fs.Position;

        using (var sha1 = SHA1.Create())
        {
            var verifyHash = await sha1.ComputeHashAsync(fs);

            if (!verifyHash.SequenceEqual(hash))
                throw new InvalidDataException("File integrity does not match specified hash.");
        }

        fs.Position = curPos;

        var pluginAssembly = reader.ReadString();
        var pluginVersion = reader.ReadString();

        var loadContext = new PluginLoadContext(pluginAssembly);

        var entryCount = reader.ReadInt32();
        var entries = new Dictionary<string, PluginFileEntry>(entryCount);

        var offset = 0;
        for (int i = 0; i < entryCount; i++)
        {
            var entry = new PluginFileEntry()
            {
                Name = reader.ReadString(),
                Length = reader.ReadInt32(),
                CompressedLength = reader.ReadInt32(),
                Offset = offset,
            };

            entries.Add(entry.Name, entry);

            offset += entry.CompressedLength;
        }

        var startPos = (int)fs.Position;
        foreach (var (_, entry) in entries)
            entry.Offset += startPos;

        var libsWithSymbols = new List<string>();
        foreach (var (_, entry) in entries)
        {
            byte[] actualBytes;
            if (entry.Length != entry.CompressedLength)
            {
                var mem = new byte[entry.CompressedLength];

                var compressedBytesRead = await fs.ReadAsync(mem);

                if (compressedBytesRead != entry.CompressedLength)
                    throw new DataLengthException();

                await using var ms = new MemoryStream(mem);
                await using var ds = new DeflateStream(ms, CompressionMode.Decompress);

                await using var deflatedData = new MemoryStream();

                await ds.CopyToAsync(deflatedData);

                if (deflatedData.Length != entry.Length)
                    throw new DataLengthException();

                actualBytes = deflatedData.ToArray();
            }
            else
            {
                actualBytes = new byte[entry.Length];
                await fs.ReadAsync(actualBytes);
            }

            var name = Path.GetFileNameWithoutExtension(entry.Name);
            //Don't load this assembly wait
            if (name == pluginAssembly)
                continue;

            //TODO LOAD OTHER FILES SOMEWHERE
            if (entry.Name.EndsWith(".dll"))
            {
                if(entries.ContainsKey(entry.Name.Replace(".dll", ".pdb")))
                {
                    //Library has debug symbols load in last
                    libsWithSymbols.Add(entry.Name.Replace(".dll", ".pdb"));
                    continue;
                }

                loadContext.LoadAssembly(actualBytes);
            }
        }

        foreach(var lib in libsWithSymbols)
        {
            var mainLib = await entries[$"{lib}.dll"].GetDataAsync(fs);
            var libSymbols = await entries[$"{lib}.pdb"].GetDataAsync(fs);

            loadContext.LoadAssembly(mainLib, libSymbols);
        }

        var mainPluginEntry = entries[$"{pluginAssembly}.dll"];
        var mainPluginPbdEntry = entries[$"{pluginAssembly}.pdb"];

        var mainAssembly = loadContext.LoadAssembly(await mainPluginEntry.GetDataAsync(fs), await mainPluginPbdEntry.GetDataAsync(fs));

        if (mainAssembly is null)
            throw new InvalidOperationException("Failed to find main assembly");

        await fs.DisposeAsync();

        return await HandlePluginAsync(loadContext, mainAssembly, path, entries);
    }

    internal async Task<PluginContainer> HandlePluginAsync(PluginLoadContext loadContext, Assembly assembly,
        string path, Dictionary<string, PluginFileEntry> entries)
    {
        Type? pluginType = assembly.GetTypes().FirstOrDefault(type => type.IsSubclassOf(typeof(PluginBase)));

        PluginBase? plugin;
        if (pluginType == null || pluginType.GetConstructor([]) == null)
        {
            plugin = default;
            logger.LogError("Loaded assembly contains no type implementing PluginBase with public parameterless constructor.");

            throw new InvalidOperationException("Loaded assembly contains no type implementing PluginBase with public parameterless constructor.");
        }

        logger.LogInformation("Creating plugin instance...");
        plugin = (PluginBase)Activator.CreateInstance(pluginType)!;

        var pluginContainer = new PluginContainer
        {
            Plugin = plugin,
            FileEntries = entries.ToFrozenDictionary(),
            LoadContext = loadContext,
            PluginAssembly = assembly,
            Source = path
        };

        await pluginContainer.InitializeAsync();

        return pluginContainer;
    }
}
