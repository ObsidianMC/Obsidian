using Obsidian.API.Entities;
using Obsidian.API.Plugins;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The entity types plugins registered, after vanilla's: their ids follow vanilla's, in registration order.
/// </summary>
internal static class EntityDefinitionsRegistry
{
    private static readonly Lock registering = new();

    // Registering replaces the table whole, so lookups never lock.
    private static EntityDefinition[] definitions = [];
    private static FrozenDictionary<string, EntityDefinition> byName = FrozenDictionary<string, EntityDefinition>.Empty;

    /// <summary>
    /// How many entity types vanilla has: <see cref="EntityType"/>'s values are their ids.
    /// </summary>
    public static int VanillaCount { get; } = Enum.GetValues<EntityType>().Max(type => (int)type) + 1;

    /// <summary>
    /// The entity types plugins registered, in id order. They stay registered until the server stops.
    /// </summary>
    public static IReadOnlyList<EntityDefinition> PluginDefinitions => definitions;

    /// <summary>
    /// Finds a plugin's entity type by its identifier, ignoring case.
    /// </summary>
    public static bool TryGet(string identifier, [NotNullWhen(true)] out EntityDefinition? definition) =>
        byName.TryGetValue(identifier, out definition);

    /// <summary>
    /// Registers an entity type, or returns the one registered before under the same identifier, which keeps its id.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The identifier is in vanilla's namespace, or the type was registered before as another kind.
    /// </exception>
    public static EntityDefinition Register(string identifier, EntityKind kind)
    {
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} is in vanilla's namespace.", nameof(identifier));

        lock (registering)
        {
            // Its entities exist as one kind, mobs or not, until the server restarts.
            if (byName.TryGetValue(identifier, out var existing))
            {
                return existing.Kind == kind
                    ? existing
                    : throw new ArgumentException($"{identifier}'s kind can't change until the server restarts.");
            }

            var definition = new EntityDefinition(VanillaCount + definitions.Length, identifier, kind);

            definitions = [.. definitions, definition];
            byName = definitions.ToFrozenDictionary(entry => entry.Identifier, StringComparer.OrdinalIgnoreCase);

            return definition;
        }
    }

    /// <summary>
    /// Gives an entity type its properties and its plugin's behaviour, unless the plugin started unloading: then its
    /// types were released already, and binding one now would keep the plugin loaded.
    /// </summary>
    /// <returns>Whether the type was bound.</returns>
    public static bool TryBind(
        EntityDefinition definition,
        EntityProperties properties,
        IPluginContainer plugin,
        Func<bool> closing)
    {
        lock (registering)
        {
            if (closing())
                return false;

            definition.Settings = EntitySettings.From(properties);
            definition.Binding = new EntityBinding(plugin, properties.Behavior);
            return true;
        }
    }

    /// <summary>
    /// Drops the behaviour of every entity type <paramref name="plugin"/> registered, as the plugin unloads. The types
    /// stay registered, with their ids and properties, for their entities saved in the world.
    /// </summary>
    /// <returns>The plugin's types, whose entities unloading removes from the levels.</returns>
    public static ImmutableArray<EntityDefinition> ReleasePlugin(IPluginContainer plugin)
    {
        var released = ImmutableArray.CreateBuilder<EntityDefinition>();

        lock (registering)
        {
            foreach (var definition in definitions)
            {
                if (definition.Binding?.Owner != plugin)
                    continue;

                definition.Binding = null;
                released.Add(definition);
            }
        }

        return released.ToImmutable();
    }
}
