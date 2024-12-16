namespace Obsidian.API.Inventory.DataComponents;
public static class ComponentBuilder
{
    public static SimpleDataComponent<int> MaxStackSize => BuildSimpleComponent(DataComponentType.MaxStackSize, "minecraft:max_stack_size",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<int> MaxDamage => BuildSimpleComponent(DataComponentType.MaxDamage, "minecraft:max_damage",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<int> Damage => BuildSimpleComponent(DataComponentType.Damage, "minecraft:damage",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<bool> Unbreakable => BuildSimpleComponent(DataComponentType.Unbreakable, "minecraft:unbreakable",
        (writer, value) => writer.WriteBoolean(value),
        (reader) => reader.ReadBoolean());

    public static SimpleDataComponent<ChatMessage> CustomName => BuildSimpleComponent(DataComponentType.CustomName, "minecraft:custom_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    public static SimpleDataComponent<ChatMessage> ItemName => BuildSimpleComponent(DataComponentType.CustomName, "minecraft:item_name",
        (writer, value) => writer.WriteChat(value),
        (reader) => reader.ReadChat());

    public static SimpleDataComponent<List<ChatMessage>> Lore => BuildSimpleComponent(DataComponentType.Lore, "minecraft:lore",
        (writer, value) => writer.WriteLengthPrefixedArray(value),
        (reader) =>
        {
            var count = reader.ReadVarInt();

            if (count == 0)
                return [];

            var components = new List<ChatMessage>(count);

            for (var i = 0; i < count; i++)
                components[i] = reader.ReadChat();

            return components;
        });

    public static SimpleDataComponent<ItemRarity> Rarity => BuildSimpleComponent(DataComponentType.Rarity, "minecraft:rarity",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt<ItemRarity>());


    public static SimpleDataComponent<TValue> BuildSimpleComponent<TValue>(DataComponentType type, string identifier,
        Action<INetStreamWriter, TValue> writer,
        Func<INetStreamReader, TValue> reader) => new(type, identifier, writer, reader);
}
