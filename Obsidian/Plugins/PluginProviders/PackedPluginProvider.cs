using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Org.BouncyCastle.Crypto;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Obsidian.Plugins.PluginProviders;
public sealed class PackedPluginProvider(PluginManager pluginManager, ILogger logger)
{
    private readonly PluginManager pluginManager = pluginManager;
    private readonly ILogger logger = logger;

    public async Task<PluginContainer?> GetPluginAsync(string path)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs);

        var header = reader.ReadBytes(4);
        if (!"OBBY"u8.SequenceEqual(header))
            throw new InvalidOperationException("Plugin file does not begin with the proper header.");

        //TODO save api version somewhere
        var apiVersion = reader.ReadString();

        var hash = reader.ReadBytes(SHA384.HashSizeInBytes);
        var isSigned = reader.ReadBoolean();

        byte[]? signature = isSigned ? reader.ReadBytes(SHA384.HashSizeInBits) : null;

        var dataLength = reader.ReadInt32();

        var curPos = fs.Position;

        //Don't load untrusted plugins
        var isSigValid = await this.TryValidatePluginAsync(fs, hash, path, isSigned, signature);
        if (!isSigValid)
            return null;

        fs.Position = curPos;

        var pluginAssembly = reader.ReadString();
        var pluginVersion = reader.ReadString();

        var loadContext = new PluginLoadContext(pluginAssembly);

        var entries = await this.InitializeEntriesAsync(reader, fs);

        var partialContainer = this.BuildPartialContainer(loadContext, path, entries, isSigValid);

        //Can't load until those plugins are loaded
        if (partialContainer.Info.Dependencies.Any(x => x.Required && !this.pluginManager.Plugins.Any(d => d.Info.Id == x.Id)))
        {
            var str = partialContainer.Info.Dependencies.Length > 1 ? "has multiple hard dependencies." : 
                $"has a hard dependency on {partialContainer.Info.Dependencies.First().Id}.";
            this.logger.LogWarning("{name} {message}. Will Attempt to load after.", partialContainer.Info.Name, str);
            return partialContainer;
        }

        var mainAssembly = this.InitializePlugin(partialContainer);

        return HandlePlugin(partialContainer, mainAssembly);
    }

    internal Assembly InitializePlugin(PluginContainer pluginContainer)
    {
        var pluginAssembly = pluginContainer.LoadContext.Name;

        var libsWithSymbols = this.ProcessEntries(pluginContainer);
        foreach (var lib in libsWithSymbols)
        {
            var mainLib = pluginContainer.GetFileData($"{lib}.dll")!;
            var libSymbols = pluginContainer.GetFileData($"{lib}.pdb")!;

            pluginContainer.LoadContext.LoadAssembly(mainLib, libSymbols);
        }

        var mainPluginEntry = pluginContainer.GetFileData($"{pluginAssembly}.dll")!;
        var mainPluginPbdEntry = pluginContainer.GetFileData($"{pluginAssembly}.pdb")!;

        var mainAssembly = pluginContainer.LoadContext.LoadAssembly(mainPluginEntry, mainPluginPbdEntry!)
            ?? throw new InvalidOperationException("Failed to find main assembly");

        pluginContainer.PluginAssembly = mainAssembly;

        return mainAssembly;
    }

    internal PluginContainer HandlePlugin(PluginContainer pluginContainer, Assembly assembly)
    {
        Type? pluginType = assembly.GetTypes().FirstOrDefault(type => type.IsSubclassOf(typeof(PluginBase)));

        PluginBase? plugin;
        if (pluginType == null || pluginType.GetConstructor([]) == null)
        {
            plugin = default;
            logger.LogError("Loaded assembly contains no type implementing PluginBase with public parameterless constructor.");

            throw new InvalidOperationException("Loaded assembly contains no type implementing PluginBase with public parameterless constructor.");
        }

        logger.LogDebug("Creating plugin instance...");
        plugin = (PluginBase)Activator.CreateInstance(pluginType)!;

        pluginContainer.PluginAssembly = assembly;
        pluginContainer.Plugin = plugin;

        pluginContainer.Initialize();

        return pluginContainer;
    }

    /// <summary>
    /// Verifies the file hash and tries to validate the signature
    /// </summary>
    /// <returns>True if the provided plugin was successfully validated. Otherwise false.</returns>
    private async Task<bool> TryValidatePluginAsync(FileStream fs, byte[] hash, string path, bool isSigned, byte[]? signature = null)
    {
        using (var sha384 = SHA384.Create())
        {
            var verifyHash = await sha384.ComputeHashAsync(fs);

            if (!verifyHash.SequenceEqual(hash))
            {
                this.logger.LogWarning("File {filePath} integrity does not match specified hash.", path);
                return false;
            }
        }

        var isSigValid = true;
        if (!this.pluginManager.server.Configuration.AllowUntrustedPlugins)
        {
            if (!isSigned)
                return false;

            var deformatter = new RSAPKCS1SignatureDeformatter();
            deformatter.SetHashAlgorithm("SHA384");

            using var rsa = RSA.Create();
            foreach (var rsaParameter in this.pluginManager.AcceptedKeys)
            {
                rsa.ImportParameters(rsaParameter);
                deformatter.SetKey(rsa);

                isSigValid = deformatter.VerifySignature(hash, signature!);

                if (isSigValid)
                    break;
            }
        }

        return isSigValid;
    }

    /// <summary>
    /// Steps through the plugin file stream and initializes each file entry found.
    /// </summary>
    /// <returns>A dictionary that contains file entries with the key as the FileName and value as <see cref="PluginFileEntry"/>.</returns>
    private async Task<Dictionary<string, PluginFileEntry>> InitializeEntriesAsync(BinaryReader reader, FileStream fs)
    {
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
        {
            entry.Offset += startPos;

            var data = new byte[entry.CompressedLength];

            var bytesRead = await fs.ReadAsync(data);

            if (bytesRead != entry.CompressedLength)
                throw new DataLengthException();

            entry.rawData = data;
        }

        return entries;
    }

    private PluginContainer BuildPartialContainer(PluginLoadContext loadContext, string path,
        Dictionary<string, PluginFileEntry> entries, bool validSignature)
    {
        var pluginContainer = new PluginContainer
        {
            LoadContext = loadContext,
            Source = path,
            FileEntries = entries.ToFrozenDictionary(),
            ValidSignature = validSignature
        };

        pluginContainer.Initialize();

        return pluginContainer;
    }


    /// <summary>
    ///  Goes and loads any assemblies found into the <see cref="PluginContainer.LoadContext"/>.
    /// </summary>
    private List<string> ProcessEntries(PluginContainer pluginContainer)
    {
        var pluginAssembly = pluginContainer.LoadContext.Name;

        var libsWithSymbols = new List<string>();
        foreach (var (_, entry) in pluginContainer.FileEntries)
        {
            var actualBytes = entry.GetData();

            var name = Path.GetFileNameWithoutExtension(entry.Name);
            //Don't load this assembly wait
            if (name == pluginAssembly)
                continue;

            //TODO LOAD OTHER FILES SOMEWHERE
            if (entry.Name.EndsWith(".dll"))
            {
                if (pluginContainer.FileEntries.ContainsKey(entry.Name.Replace(".dll", ".pdb")))
                {
                    //Library has debug symbols load in last
                    libsWithSymbols.Add(entry.Name.Replace(".dll", ".pdb"));
                    continue;
                }

                pluginContainer.LoadContext.LoadAssembly(actualBytes);
            }
        }

        return libsWithSymbols;
    }
}
