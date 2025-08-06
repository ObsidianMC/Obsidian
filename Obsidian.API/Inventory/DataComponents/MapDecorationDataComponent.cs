using Obsidian.API.Utilities;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class MapDecorationDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.MapDecorations;

    public override string Identifier => "minecraft:map_decorations";

    public required Dictionary<string, MapDecoration> Decorations { get; set; }

    [SetsRequiredMembers]
    internal MapDecorationDataComponent() { }

    public override void Read(INetStreamReader reader)
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

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Decorations.Count);
        foreach (var (key, value) in this.Decorations)
        {
            writer.WriteString(key);
            MapDecoration.Write(value, writer);
        }
    }
}

public readonly record struct MapDecoration : INetworkSerializable<MapDecoration>
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
