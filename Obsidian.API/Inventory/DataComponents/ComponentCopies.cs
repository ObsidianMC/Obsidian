using Obsidian.Nbt;
using System.IO;

namespace Obsidian.API.Inventory.DataComponents;

// Legacy API values remain mutable outside components. Copy them at component boundaries, never on Version reads.
internal static class ComponentCopies
{
    public static NbtCompound? Nbt(NbtCompound? value)
    {
        if (value is null)
            return null;

        using var writer = new RawNbtWriter(value.Name ?? string.Empty);
        writer.WriteEntries(value);
        writer.EndCompound();

        return NbtReader.ReadTag(writer.Data, true, out _) as NbtCompound
            ?? throw new InvalidDataException("Invalid component NBT.");
    }

    public static ChatMessage? Chat(ChatMessage? value) => value?.CopyForComponent();

    public static ImmutableArray<ChatMessage> Chats(ImmutableArray<ChatMessage> values) =>
        values.IsDefault ? [] : values.Select(value => Chat(value)!).ToImmutableArray();

    public static ItemStack? Stack(ItemStack? value) => value is null ? null : new(value, value.Count);

    public static ImmutableArray<ItemStack> Stacks(ImmutableArray<ItemStack> values) =>
        values.IsDefault ? [] : values.Select(value => Stack(value)!).ToImmutableArray();
}
