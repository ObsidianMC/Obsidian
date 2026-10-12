namespace Obsidian.Net.Payloads;

/// <summary>
/// The registries plugins add to that clients must agree with the server on, numbered as NeoForge's and Fabric's
/// registry sync send them: blocks (whose order numbers block states), entity types and items.
/// </summary>
/// <remarks>
/// The server sends the ones plugins added to; the Obsidian client checks what it receives against its own, which its
/// mods filled the same way.
/// </remarks>
internal static class SyncedRegistries
{
    public const string Blocks = "minecraft:block";
    public const string EntityTypes = "minecraft:entity_type";
    public const string Items = "minecraft:item";

    /// <summary>
    /// The registries, in the order the server sends them.
    /// </summary>
    public static ImmutableArray<string> Names { get; } = [Blocks, EntityTypes, Items];

    /// <summary>
    /// The registries plugins added to, with all their entries.
    /// </summary>
    public static IReadOnlyList<SyncedRegistry> GetModded() => [.. Names.Where(HasPluginEntries).Select(Get)];

    /// <summary>
    /// Whether plugins added to <paramref name="name"/>.
    /// </summary>
    public static bool HasPluginEntries(string name) => name switch
    {
        Blocks => BlocksRegistry.PluginBlocks.Count > 0,
        EntityTypes => EntityDefinitionsRegistry.PluginDefinitions.Count > 0,
        Items => ItemsRegistry.HasPluginItems,
        _ => throw new ArgumentException($"{name} isn't a synced registry.", nameof(name))
    };

    /// <summary>
    /// Every entry of <paramref name="name"/>, vanilla's and plugins', with its id.
    /// </summary>
    public static SyncedRegistry Get(string name)
    {
        (int Id, string Identifier)[] entries = name switch
        {
            Blocks => BlocksRegistry.ResourceIds
                .Select((identifier, id) => (id, identifier))
                .Concat(BlocksRegistry.PluginBlocks.Select(type => (type.Id, type.Identifier)))
                .ToArray(),
            EntityTypes => Enum.GetValues<EntityType>()
                .Select(type => ((int)type, Entities.EntityNbt.TypeId(type)))
                .Concat(EntityDefinitionsRegistry.PluginDefinitions.Select(definition =>
                    (definition.Id, definition.Identifier)))
                .ToArray(),
            Items => ItemsRegistry.All
                .Select(item => ((int)item.Id, item.UnlocalizedName))
                .ToArray(),
            _ => throw new ArgumentException($"{name} isn't a synced registry.", nameof(name))
        };

        return new SyncedRegistry(name, entries);
    }
}
