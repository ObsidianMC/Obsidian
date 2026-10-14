namespace Obsidian.Net.Payloads;

/// <summary>
/// The registries plugins add to that clients must agree with the server on, numbered as NeoForge's and Fabric's
/// registry sync send them: blocks (whose order numbers block states), block entity types, data component types,
/// entity types, items, menu types and particle types.
/// </summary>
/// <remarks>
/// The server sends the ones plugins added to; the Obsidian client checks what it receives against its own, which its
/// mods filled the same way.
/// </remarks>
internal static class SyncedRegistries
{
    public const string Blocks = "minecraft:block";
    public const string BlockEntityTypes = "minecraft:block_entity_type";
    public const string DataComponentTypes = "minecraft:data_component_type";
    public const string EntityTypes = "minecraft:entity_type";
    public const string Items = "minecraft:item";
    public const string Menus = "minecraft:menu";
    public const string ParticleTypes = "minecraft:particle_type";
    public const string CustomStatistics = "minecraft:custom_stat";

    /// <summary>
    /// The registries, in the order the server sends them.
    /// </summary>
    public static ImmutableArray<string> Names { get; } =
        [Blocks, BlockEntityTypes, DataComponentTypes, EntityTypes, Items, Menus, ParticleTypes, CustomStatistics];

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
        BlockEntityTypes => BlockEntityTypesRegistry.PluginTypes.Count > 0,
        DataComponentTypes => DataComponentTypesRegistry.PluginTypes.Any(type => type.IsSynced),
        EntityTypes => EntityDefinitionsRegistry.PluginDefinitions.Count > 0,
        Items => ItemsRegistry.HasPluginItems,
        Menus => MenuTypesRegistry.PluginMenus.Count > 0,
        ParticleTypes => ParticleTypesRegistry.PluginParticles.Count > 0,
        CustomStatistics => CustomStatisticsRegistry.Plugins.Length > 0,
        _ => throw new ArgumentException($"{name} isn't a synced registry.", nameof(name))
    };

    /// <summary>
    /// Every entry of <paramref name="name"/>, vanilla's and plugins', with its id.
    /// </summary>
    public static SyncedRegistry Get(string name)
    {
        (int Id, string Identifier)[] entries = name switch
        {
            CustomStatistics => CustomStatisticsRegistry.Vanilla.Concat(CustomStatisticsRegistry.Plugins)
                .Select((identifier, id) => (id, identifier))
                .ToArray(),
            Blocks => BlocksRegistry.ResourceIds
                .Select((identifier, id) => (id, identifier))
                .Concat(BlocksRegistry.PluginBlocks.Select(type => (type.Id, type.Identifier)))
                .ToArray(),
            BlockEntityTypes => BlockEntityTypesRegistry.VanillaIdentifiers
                .Select((identifier, id) => (id, identifier))
                .Concat(BlockEntityTypesRegistry.PluginTypes.Select(type => (type.Id, type.Identifier)))
                .ToArray(),
            // Only the plugins' types clients get values of; the server's own are left out, so their ids are gaps.
            DataComponentTypes => VanillaRegistryIds.Get(DataComponentTypes)
                .Select((identifier, id) => (id, identifier))
                .Concat(DataComponentTypesRegistry.PluginTypes
                    .Where(type => type.IsSynced)
                    .Select(type => ((int)type.Type, type.Identifier)))
                .ToArray(),
            EntityTypes => Enum.GetValues<EntityType>()
                .Select(type => ((int)type, Entities.EntityNbt.TypeId(type)))
                .Concat(EntityDefinitionsRegistry.PluginDefinitions.Select(definition =>
                    (definition.Id, definition.Identifier)))
                .ToArray(),
            Items => ItemsRegistry.All
                .Select(item => ((int)item.Id, item.UnlocalizedName))
                .ToArray(),
            Menus => MenuTypesRegistry.VanillaIdentifiers
                .Select((identifier, id) => (id, identifier))
                .Concat(MenuTypesRegistry.PluginMenus.Select(menu => (menu.Id, menu.Identifier)))
                .ToArray(),
            ParticleTypes => ParticleTypesRegistry.VanillaIdentifiers
                .Select((identifier, id) => (id, identifier))
                .Concat(ParticleTypesRegistry.PluginParticles.Select(particle => (particle.Id, particle.Identifier)))
                .ToArray(),
            _ => throw new ArgumentException($"{name} isn't a synced registry.", nameof(name))
        };

        return new SyncedRegistry(name, entries);
    }
}
