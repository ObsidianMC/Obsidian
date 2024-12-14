namespace Obsidian.API.Inventory.DataComponents;
public static class ComponentBuilder
{
    public static SimpleDataComponent<int> MaxStackSize => BuildSimpleComponent(DataComponentType.MaxStackSize, "minecraft:max_stack_size",
        (writer, value) => writer.WriteVarInt(value),
        (reader) => reader.ReadVarInt());

    public static SimpleDataComponent<TValue> BuildSimpleComponent<TValue>(DataComponentType type, string identifier, 
        Action<INetStreamWriter, TValue> writer, 
        Func<INetStreamReader, TValue> reader) => new(type, identifier, writer, reader);
}
