using Obsidian.API.Utilities;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed class MapDecorationDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.MapDecorations;

    public string Identifier => "minecraft:map_decorations";

    public required Dictionary<string, MapDecoration> Decorations { get; set; }

    [SetsRequiredMembers]
    internal MapDecorationDataComponent() { }

    public void Read(INetStreamReader reader)
    {
        var count = reader.ReadVarInt();

        var decorations = new Dictionary<string, MapDecoration>(count);

        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadString();

            decorations[key] = new()
            {
                Type = Enum.Parse<MapDecorationType>(reader.ReadString().TrimResourceTag().ToPascalCase(), true),
                X = reader.ReadDouble(),
                Z = reader.ReadDouble(),
                Rotation = reader.ReadSingle()
            };
        }
    }
    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Decorations.Count);
        foreach (var (key, value) in this.Decorations)
        {
            writer.WriteString(key);
            MapDecoration.Write(value, writer);
        }
    }
}

public readonly struct MapDecoration : INetworkSerializable<MapDecoration>
{
    public required MapDecorationType Type { get; init; }

    public required double X { get; init; }

    public required double Z { get; init; }

    public required float Rotation { get; init; }

    public static MapDecoration Read(INetStreamReader reader) => new()
    {
        Type = Enum.Parse<MapDecorationType>(reader.ReadString().TrimResourceTag(), true),
        X = reader.ReadDouble(),
        Z = reader.ReadDouble(),
        Rotation = reader.ReadSingle()
    };

    public static void Write(MapDecoration value, INetStreamWriter writer)
    {
        writer.WriteString($"minecraft:{value.Type.ToString().ToSnakeCase()}");
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Z);
        writer.WriteSingle(value.Rotation);
    }
}
