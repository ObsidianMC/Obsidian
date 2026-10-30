namespace Obsidian.API.Inventory.DataComponents;

public static partial class ComponentBuilder
{
    /// <summary>A named block transformer, such as <c>minecraft:hoe</c>.</summary>
    public static SimpleDataComponent<string> BlockTransformer => RegistryEntry(
        DataComponentType.BlockTransformer,
        "minecraft:block_transformer",
        "minecraft:block_transformer");

    /// <summary>The decorated pot pattern supplied by this stack.</summary>
    public static SimpleDataComponent<string> ProvidesPotteryPattern => RegistryEntry(
        DataComponentType.ProvidesPotteryPattern,
        "minecraft:provides_pottery_pattern",
        "minecraft:decorated_pot_pattern");

    private static SimpleDataComponent<string> RegistryEntry(DataComponentType type, string name, string registry) =>
        BuildSimpleComponent(
            type,
            name,
            (writer, value) =>
            {
                var id = ComponentRegistryNames.Id(registry, value);
                writer.WriteRegistryId(registry, id);
            },
            reader =>
            {
                var id = reader.ReadRegistryId(registry);
                return ComponentRegistryNames.Name(registry, id);
            });
}
