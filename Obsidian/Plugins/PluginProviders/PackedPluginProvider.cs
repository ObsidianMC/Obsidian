using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Org.BouncyCastle.Crypto;
using System.Buffers;
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

        var entries = new PluginFileEntry[reader.ReadInt32()];

        var offset = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = new PluginFileEntry()
            {
                FullName = reader.ReadString(),
                Length = reader.ReadInt32(),
                CompressedLength = reader.ReadInt32(),
                Offset = offset,
            };

            entries[i] = entry;

            offset += entry.CompressedLength;
        }

        var startPos = (int)fs.Position;
        foreach (var entry in entries)
            entry.Offset += startPos;

        foreach (var entry in entries)
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

            var name = Path.GetFileNameWithoutExtension(entry.FullName);
            //Don't load this assembly wait
            if (name == pluginAssembly)
                continue;

            //TODO LOAD OTHER FILES SOMEWHERE
            if (entry.FullName.EndsWith(".dll"))
                loadContext.LoadAssembly(actualBytes);
        }

        var mainPluginEntry = entries.First(x => x.FullName.EndsWith($"{pluginAssembly}.dll"));
        var mainPluginPbdEntry = entries.First(x => x.FullName.EndsWith($"{pluginAssembly}.pdb"));

        var mainAssembly = loadContext.LoadAssembly(await mainPluginEntry.GetDataAsync(fs), await mainPluginPbdEntry.GetDataAsync(fs));

        if (mainAssembly is null)
            throw new InvalidOperationException("Failed to find main assembly");

        return await HandlePluginAsync(loadContext, mainAssembly, path, entries);
    }

    internal async Task<PluginContainer> HandlePluginAsync(PluginLoadContext loadContext, Assembly assembly,
        string path, IEnumerable<PluginFileEntry> entries)
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

        string name = assembly.GetName().Name!;
        using var pluginInfoStream = assembly.GetManifestResourceStream($"{name}.plugin.json")
            ?? throw new InvalidOperationException($"Failed to find embedded plugin.json file for {name}");

        var info = await pluginInfoStream.FromJsonAsync<PluginInfo>() ??
            throw new JsonException($"Couldn't deserialize plugin.json from {name}");

        return new PluginContainer(plugin, info, assembly, loadContext, path, entries);
    }
}
