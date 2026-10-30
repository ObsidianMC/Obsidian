using Obsidian.API.Utilities;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>A component's complete network value, excluding its type ID and any untrusted-codec length prefix.</summary>
public sealed record OpaqueDataComponent(DataComponentType ComponentType, ImmutableArray<byte> Data) : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.ComponentType,
        ComponentValueKey.Sequence(this.Data)
    });

    public bool Equals(OpaqueDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    public override DataComponentType Type => this.ComponentType;
    public override string Identifier => GetIdentifier(this.Type);

    public static string GetIdentifier(DataComponentType type)
    {
        if (Registries.DataComponentTypesRegistry.TryGet(type, out var plugin))
            return plugin.Identifier;

        var name = type.ToString().ToSnakeCase();
        if (type is >= DataComponentType.VillagerVariant and <= DataComponentType.ShulkerColor
            or DataComponentType.CushionColor)
        {
            var split = name.IndexOf('_');
            if (type == DataComponentType.TropicalFishPattern || type == DataComponentType.TropicalFishBaseColor
                || type == DataComponentType.TropicalFishPatternColor)
            {
                split = "tropical_fish".Length;
            }

            if (type == DataComponentType.ZombieNautilusVariant)
                split = "zombie_nautilus".Length;
            if (split >= 0)
                name = name[..split] + "/" + name[(split + 1)..];
        }

        return "minecraft:" + name;
    }

    public override void Write(INetStreamWriter writer)
    {
        foreach (var value in this.Data)
            writer.WriteByte(value);
    }
}
