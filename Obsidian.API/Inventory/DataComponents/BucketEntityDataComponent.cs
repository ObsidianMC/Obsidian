using Obsidian.Nbt;
using System.IO;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record BucketEntityDataComponent() : SimpleDataComponent(DataComponentType.BucketEntityData, "minecraft:bucket_entity_data")
{
    public NbtCompound Value { get; init; } = new();
    public override void Write(INetStreamWriter writer) => writer.WriteNbtCompound(Value);
    public override void Read(INetStreamReader reader)
    {
        using var stream = new MemoryStream(reader.AsSpan(reader.Offset, reader.Size - reader.Offset).ToArray());
        var nbt = new NbtReader(stream);
        if (!nbt.TryReadNextTag<NbtCompound>(false, out var value)) throw new InvalidDataException("Invalid bucket entity data.");
        foreach (var (name, tag) in value) Value.Add(name, tag);
        for (var count = stream.Position; count > 0; count--) reader.ReadByte();
    }
}
