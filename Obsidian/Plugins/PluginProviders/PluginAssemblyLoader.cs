using Obsidian.API.Plugins;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Obsidian.Plugins.PluginProviders;

/// <summary>
/// Loads a plugin's assemblies from its archive into its load context and creates its <see cref="PluginBase"/>.
/// </summary>
internal static class PluginAssemblyLoader
{
    /// <summary>
    /// Loads the plugin's libraries, then its main assembly, and creates the plugin.
    /// </summary>
    /// <param name="container">The plugin, whose load context already links to its dependencies' load contexts.</param>
    /// <exception cref="InvalidOperationException">The archive lacks the main assembly or a plugin type.</exception>
    public static void Load(PluginContainer container)
    {
        var assemblyName = container.Info.AssemblyName;

        foreach (var library in FindLibraries(container))
            container.LoadContext.LoadAssembly(library.Assembly, library.Symbols);

        var mainAssembly = container.Archive.Read($"{assemblyName}.dll")
            ?? throw new InvalidOperationException($"The plugin has no {assemblyName}.dll.");

        var assembly = container.LoadContext.LoadAssembly(mainAssembly, container.Archive.Read($"{assemblyName}.pdb"))
            ?? throw new InvalidOperationException($"Failed to load {assemblyName}.dll.");

        var pluginType = assembly.GetTypes().FirstOrDefault(type => type.IsSubclassOf(typeof(PluginBase)));
        if (pluginType?.GetConstructor([]) is null)
            throw new InvalidOperationException(
                "Loaded assembly contains no type implementing PluginBase with public parameterless constructor.");

        container.PluginAssembly = assembly;
        container.Plugin = (PluginBase)Activator.CreateInstance(pluginType)!;
        container.Initialize();
    }

    /// <summary>
    /// Finds the libraries the plugin's <c>deps.json</c> lists that neither the server nor a dependency provides.
    /// </summary>
    private static List<(byte[] Assembly, byte[]? Symbols)> FindLibraries(PluginContainer container)
    {
        var assemblyName = container.Info.AssemblyName;
        var libraries = new List<(byte[], byte[]?)>();

        var depsFile = container.Archive.Read($"{assemblyName}.deps.json");
        if (depsFile is null)
            return libraries;

        var deps = JsonSerializer.Deserialize<DotNetDeps>(depsFile, JsonSerializerOptions.Web);
        var targets = deps.Targets[deps.RuntimeTarget.Name]
            .Deserialize<Dictionary<string, DotNetTarget>>(JsonSerializerOptions.Web)!;

        foreach (var (_, target) in targets)
        {
            if (target.Runtime is null)
                continue;

            foreach (var (path, runtimeElement) in target.Runtime)
            {
                var fileName = path.Split('/')[^1];
                var name = Path.GetFileNameWithoutExtension(fileName);

                if (name == assemblyName || runtimeElement.ToString() == "{}")
                    continue;

                if (ProvidedByDependency(container, name))
                    continue;

                var runtime = runtimeElement.Deserialize<DependencyRuntime>(JsonSerializerOptions.Web);
                if (IsSharedAssembly(name, runtime.AssemblyVersion))
                    continue;

                var library = container.Archive.Read(fileName);
                if (library is not null)
                    libraries.Add((library, container.Archive.Read($"{name}.pdb")));
            }
        }

        return libraries;
    }

    private static bool ProvidedByDependency(PluginContainer container, string assemblyName) =>
        container.Dependencies.Any(dependency => dependency.Info.AssemblyName == assemblyName);

    // A library the server already loaded (Obsidian.API, Microsoft.Extensions.*, ...) is shared rather than loaded
    // again, so that the plugin and the server agree on its types.
    private static bool IsSharedAssembly(string name, string version)
    {
        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName
            {
                Name = name,
                Version = new Version(version)
            }) is not null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return false;
        }
    }
}

public readonly record struct DotNetDeps
{
    public required RuntimeTarget RuntimeTarget { get; init; }

    public required Dictionary<string, JsonElement> Targets { get; init; }
}

public readonly record struct DotNetTarget
{
    public Dictionary<string, string>? Dependencies { get; init; }

    public Dictionary<string, JsonElement>? Runtime { get; init; }
}

public readonly record struct DependencyRuntime
{
    public required string AssemblyVersion { get; init; }

    public required string FileVersion { get; init; }
}

public readonly record struct RuntimeTarget
{
    public required string Name { get; init; }

    public required string Signature { get; init; }
}
