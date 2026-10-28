using Microsoft.Extensions.Configuration;
using Obsidian.API.Plugins;
using Obsidian.Builtins.WorldEdit;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Builtins;

/// <summary>An Obsidian feature using the plugin API's registration and lifecycle contracts.</summary>
internal abstract class Builtin(PluginInfo metadata) : PluginBase
{
    internal PluginInfo Metadata { get; } = metadata;

    internal bool OwnsModule(Type type) => type.Namespace == this.GetType().Namespace
        || type.Namespace?.StartsWith(this.GetType().Namespace + ".", StringComparison.Ordinal) == true;

    internal static bool IsBuiltinModule(Type type) =>
        type.Assembly == typeof(Builtin).Assembly
        && type.Namespace?.StartsWith(typeof(Builtin).Namespace + ".", StringComparison.Ordinal) == true;

    internal static IEnumerable<Builtin> Enabled(IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Builtins:worldedit"))
            yield return new WorldEditBuiltin();
    }

    internal PluginArchive Resources() => new BuiltinResources(this.Metadata);

    // The existing container reads metadata/resources through this contract. Builtin code is instantiated directly.
    private sealed class BuiltinResources : PluginArchive
    {
        [SetsRequiredMembers]
        internal BuiltinResources(PluginInfo info)
        {
            this.Info = info;
            this.Source = $"builtin:{info.Id}";
        }

        public override IEnumerable<string> Paths => [];
        public override byte[]? Read(string path) => null;
    }
}
