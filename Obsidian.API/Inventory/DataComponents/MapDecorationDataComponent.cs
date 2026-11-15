using Obsidian.API.Utilities;
using Obsidian.Nbt;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// Icons a map item shows, like an explorer map's target (<c>minecraft:map_decorations</c>).
/// </summary>
public sealed record class MapDecorationDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.decorations.OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => (entry.Key, entry.Value))
    });

    /// <inheritdoc/>
    public bool Equals(MapDecorationDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.MapDecorations;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:map_decorations";

    private IReadOnlyDictionary<string, MapDecoration> decorations =
        new Dictionary<string, MapDecoration>().AsReadOnly();

    /// <summary>
    /// Gets the icons by a key unique on the map.
    /// </summary>
    public required IReadOnlyDictionary<string, MapDecoration> Decorations
    {
        get => this.decorations;
        init => this.decorations = new Dictionary<string, MapDecoration>(value).AsReadOnly();
    }

    [SetsRequiredMembers]
    internal MapDecorationDataComponent() { }

    /// <inheritdoc/>
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
    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        var compound = new NbtCompound();
        foreach (var (key, value) in this.Decorations)
            compound.Add(value.ToNbt(key));

        writer.WriteNbtCompound(compound);
    }
}

/// <summary>
/// An icon on a map.
/// </summary>
public readonly record struct MapDecoration : INetworkSerializable<MapDecoration>
{
    /// <summary>
    /// Gets the icon's kind.
    /// </summary>
    public required MapDecorationType Type { get; init; }

    /// <summary>
    /// Gets the icon's world X coordinate.
    /// </summary>
    public required double X { get; init; }

    /// <summary>
    /// Gets the icon's world Z coordinate.
    /// </summary>
    public required double Z { get; init; }

    /// <summary>
    /// Gets the icon's rotation, in degrees.
    /// </summary>
    public required float Rotation { get; init; }

    /// <summary>
    /// Reads an icon written by <see cref="Write"/>.
    /// </summary>
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

    /// <summary>
    /// Writes the icon's kind, position and rotation.
    /// </summary>
    public static void Write(MapDecoration value, INetStreamWriter writer)
    {
        writer.WriteString($"minecraft:{value.Type.ToString().ToSnakeCase()}");
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Z);
        writer.WriteSingle(value.Rotation);
    }
}
