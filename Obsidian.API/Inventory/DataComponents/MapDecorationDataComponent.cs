using Obsidian.API.Utilities;
using Obsidian.Nbt;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record class MapDecorationDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.decorations.OrderBy(entry => entry.Key, StringComparer.Ordinal)
    });

    public bool Equals(MapDecorationDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    public override DataComponentType Type => DataComponentType.MapDecorations;

    public override string Identifier => "minecraft:map_decorations";

    private IReadOnlyDictionary<string, MapDecoration> decorations =
        new Dictionary<string, MapDecoration>().AsReadOnly();

    public required IReadOnlyDictionary<string, MapDecoration> Decorations
    {
        get => this.decorations;
        init => this.decorations = new Dictionary<string, MapDecoration>(value).AsReadOnly();
    }

    [SetsRequiredMembers]
    internal MapDecorationDataComponent() { }

    protected override void ReadCore(INetStreamReader reader)
    {
        var compound = reader.ReadNbtCompound();
        var decorations = new Dictionary<string, MapDecoration>();
        foreach (var (key, tag) in compound)
        {
            var value = (NbtCompound)tag;
            decorations[key] = new()
            {
                Type = Enum.Parse<MapDecorationType>(value.GetString("type")!.TrimResourceTag().ToPascalCase(), true),
                X = value.GetDouble("x"),
                Z = value.GetDouble("z"),
                Rotation = value.GetFloat("rotation")
            };
        }

        this.decorations = decorations.AsReadOnly();
    }

    // Vanilla doesn't sync this component with a codec of its own, so it goes over the network as NBT: a compound of the
    // decorations by key.
    public override void Write(INetStreamWriter writer)
    {
        var compound = new NbtCompound();
        foreach (var (key, value) in this.Decorations)
            compound.Add(value.ToNbt(key));

        writer.WriteNbtCompound(compound);
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

    /// <summary>
    /// The decoration as vanilla's <c>MapDecorations.Entry</c> NBT.
    /// </summary>
    public NbtCompound ToNbt(string name) => new(name)
    {
        new NbtTag<string>("type", $"minecraft:{this.Type.ToString().ToSnakeCase()}"),
        new NbtTag<double>("x", this.X),
        new NbtTag<double>("z", this.Z),
        new NbtTag<float>("rotation", this.Rotation)
    };

    public static void Write(MapDecoration value, INetStreamWriter writer)
    {
        writer.WriteString($"minecraft:{value.Type.ToString().ToSnakeCase()}");
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Z);
        writer.WriteSingle(value.Rotation);
    }
}
