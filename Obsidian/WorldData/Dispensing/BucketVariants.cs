using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using System.Text.Json;

namespace Obsidian.WorldData.Dispensing;

/// <summary>Species components restored after a bucket's entity NBT, before the mob is published.</summary>
internal static class BucketVariants
{
    // Vanilla's serialized enum names, in codec order. Tropical fish use a sparse packed pattern ID on the wire.
    private static readonly string[] axolotls = ["lucy", "wild", "gold", "cyan", "blue"];
    private static readonly string[] sizes = ["small", "medium", "large"];
    private static readonly string[] patterns =
    [
        "kob", "sunstreak", "snooper", "dasher", "brinely", "spotty",
        "flopper", "stripey", "glitter", "blockfish", "betty", "clayfish"
    ];
    private static readonly string[] colors =
    [
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"
    ];

    internal static void Apply(Mob mob, ItemStack stack)
    {
        if (mob is Axolotl axolotl && Value(stack, DataComponentType.AxolotlVariant, axolotls) is int variant)
            axolotl.Variant = variant;

        if (mob is Salmon salmon && Value(stack, DataComponentType.SalmonSize, sizes) is int size)
            salmon.Size = size;

        if (mob is not TropicalFish fish)
            return;

        var pattern = Value(stack, DataComponentType.TropicalFishPattern, patterns, packedPattern: true);
        var baseColor = Value(stack, DataComponentType.TropicalFishBaseColor, colors);
        var patternColor = Value(stack, DataComponentType.TropicalFishPatternColor, colors);
        var packed = fish.Variant;
        if (pattern is int shape)
            packed = (packed & unchecked((int)0xffff0000)) | shape;
        if (baseColor is int body)
            packed = (packed & ~0xff0000) | body << 16;
        if (patternColor is int spots)
            packed = (packed & 0xffffff) | spots << 24;

        fish.Variant = packed;
    }

    private static int? Value(ItemStack stack, DataComponentType type, string[] names, bool packedPattern = false)
    {
        if (stack.RemoveComponents.Contains(type))
            return null;

        // Received item patches retain the enum's VarInt bytes; saved patches retain the serialized name.
        if (stack.GetComponent<OpaqueDataComponent>(type) is OpaqueDataComponent opaque)
        {
            var value = ReadEnum(opaque);
            if (value is not int numeric)
                return null;

            if (packedPattern)
                return (numeric & ~0x701) == 0 && numeric >> 8 < 6 ? numeric : null;

            return (uint)numeric < names.Length ? numeric : null;
        }

        var identifier = OpaqueDataComponent.GetIdentifier(type);
        string? name = null;
        if (stack.UnmodeledComponents?.TryGetTagValue<string>(identifier, out var saved) == true)
        {
            name = saved;
        }
        else
        {
            var defaults = CombatItems.Component(stack, identifier["minecraft:".Length..]);
            if (defaults.ValueKind == JsonValueKind.String)
                name = defaults.GetString();
        }

        var index = Array.IndexOf(names, name);
        if (index < 0)
            return null;

        // Vanilla stores fish shape in byte 0 and pattern in byte 1 of its entity metadata.
        return packedPattern ? index / 6 | index % 6 << 8 : index;
    }

    private static int? ReadEnum(OpaqueDataComponent component)
    {
        var value = 0;
        for (var index = 0; index < component.Data.Length && index < 5; index++)
        {
            var next = component.Data[index];
            value |= (next & 0x7f) << (index * 7);
            if ((next & 0x80) == 0)
                return value;
        }

        return null;
    }
}
