using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.Entities;
using Org.BouncyCastle.Crypto;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

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

        foreach (var depends in partialContainer.Info.Dependencies)
        {
            var plugin = this.pluginManager.Plugins.FirstOrDefault(x => x.Info.Id == depends.Id);

            partialContainer.AddDependency(plugin.LoadContext);
            this.logger.LogInformation("Added {depends} as a dependency for {name}", plugin.Info.Name, partialContainer.Info.Name);
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

        using var dependenciesData = new MemoryStream(pluginContainer.GetFileData($"{pluginAssembly}.deps.json"));
        var dependencies = JsonSerializer.Deserialize<DotNetDeps>(dependenciesData, JsonSerializerOptions.Web);
        var targets = dependencies.Targets[dependencies.RuntimeTarget.Name].Deserialize<Dictionary<string, DotNetTarget>>(JsonSerializerOptions.Web);

        foreach (var (key, target) in targets)
        {
            var deps = target.Dependencies;
            var runtimes = target.Runtime;

            if (runtimes == null)//We don't care if its null
                continue;

            foreach (var (dll, runtimeElement) in runtimes)
            {
                var sanitizedDll = dll;
                var split = dll.Split('/');
                if (split.Length > 1)
                    sanitizedDll = split.Last();

                var name = sanitizedDll[..sanitizedDll.IndexOf(".dll")];

                if (name == pluginAssembly || runtimeElement.ToString() == "{}")
                    continue;

                var runtime = runtimeElement.Deserialize<DependencyRuntime>(JsonSerializerOptions.Web);
                var assemblyName = new AssemblyName
                {
                    Name = name,
                    Version = new(runtime.AssemblyVersion),
                };

                var depends = pluginContainer.Info.Dependencies.Select(x => x.Id)
                    .SelectMany(x => this.pluginManager.Plugins.Where(p => p.Info.Id == x));

                //TODO allow users to define what version of a dependency is allowed/valid e.g version: >1.0.0 or >=1.0.0 or =1.0.0.
                var dependency = depends.FirstOrDefault(x => x.PluginAssembly.GetName().Name == name && x.Info.Version == assemblyName.Version);

                //we don't need to load this into the context.
                if (dependency != null)
                    continue;

                if (pluginContainer.FileEntries.ContainsKey($"${name}.pdb"))
                {
                    //Library has debug symbols load in last
                    libsWithSymbols.Add(name);
                    continue;
                }

                try
                {
                    //Check to see if this assembly already exists in the shared context.
                    var sharedAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                    if (sharedAssembly != null)
                        continue;
                }
                catch { }

                var data = pluginContainer.GetFileData(sanitizedDll);
                pluginContainer.LoadContext.LoadAssembly(data);
            }
        }

        return libsWithSymbols;
    }
}


public readonly struct DotNetDeps
{
    public required RuntimeTarget RuntimeTarget { get; init; }

    public required Dictionary<string, JsonElement> Targets { get; init; }
}

public readonly struct DotNetTarget
{
    public Dictionary<string, string>? Dependencies { get; init; }

    public Dictionary<string, JsonElement>? Runtime { get; init; }
}

public readonly struct DependencyRuntime
{
    public required string AssemblyVersion { get; init; }

    public required string FileVersion { get; init; }
}

public readonly struct RuntimeTarget
{
    public required string Name { get; init; }

    public required string Signature { get; init; }
}
