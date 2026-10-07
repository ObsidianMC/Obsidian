using Obsidian.API.Utilities;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>A component's complete network value, excluding its type ID and any untrusted-codec length prefix.</summary>
public sealed record OpaqueDataComponent(DataComponentType ComponentType, byte[] Data) : DataComponent
{
    public override DataComponentType Type => this.ComponentType;
    public override string Identifier => GetIdentifier(this.Type);
    public static string GetIdentifier(DataComponentType type)
    {
        var name = type.ToString().ToSnakeCase();
        if ((int)type >= 79)
        {
            var split = name.IndexOf('_');
            if (type == DataComponentType.TropicalFishPattern || type == DataComponentType.TropicalFishBaseColor || type == DataComponentType.TropicalFishPatternColor) split = "tropical_fish".Length;
            if (type == DataComponentType.ZombieNautilusVariant) split = "zombie_nautilus".Length;
            if (split >= 0) name = name[..split] + "/" + name[(split + 1)..];
        }
        return "minecraft:" + name;
    }
    public override void Write(INetStreamWriter writer) { foreach (var value in this.Data) writer.WriteByte(value); }
}
