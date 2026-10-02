using Obsidian.Nbt;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Deep copies of NBT tags, so template data shared between threads is never handed out or changed.
/// </summary>
internal static class NbtCopy
{
    /// <summary>
    /// A deep copy of <paramref name="compound"/>, without the tags named in <paramref name="excluded"/>.
    /// </summary>
    public static NbtCompound Copy(NbtCompound compound, params string[] excluded)
    {
        var copy = new NbtCompound(compound.Name ?? string.Empty);
        foreach (var (name, tag) in compound)
        {
            if (Array.IndexOf(excluded, name) < 0)
                copy.Add(name, Copy(tag, name));
        }

        return copy;
    }

    /// <summary>
    /// Copies the tags of <paramref name="source"/> into <paramref name="target"/> like vanilla's <c>CompoundTag.merge</c>:
    /// compounds merge recursively, anything else replaces the target's tag.
    /// </summary>
    public static void Merge(NbtCompound target, NbtCompound source)
    {
        foreach (var (name, tag) in source)
        {
            if (tag is NbtCompound compound && target.TryGetTag<NbtCompound>(name, out var existing))
            {
                Merge(existing, compound);
                continue;
            }

            target.Remove(name);
            target.Add(name, Copy(tag, name));
        }
    }

    private static INbtTag Copy(INbtTag tag, string? name) => tag switch
    {
        NbtCompound compound => CopyCompound(compound, name),
        NbtList list => CopyList(list, name),
        NbtArray<byte> bytes => new NbtArray<byte>(name, [.. bytes.GetArray()]),
        NbtArray<int> ints => new NbtArray<int>(name, [.. ints.GetArray()]),
        NbtArray<long> longs => new NbtArray<long>(name, [.. longs.GetArray()]),
        NbtTag<bool> value => new NbtTag<bool>(name!, value.Value),
        NbtTag<byte> value => new NbtTag<byte>(name!, value.Value),
        NbtTag<short> value => new NbtTag<short>(name!, value.Value),
        NbtTag<int> value => new NbtTag<int>(name!, value.Value),
        NbtTag<long> value => new NbtTag<long>(name!, value.Value),
        NbtTag<float> value => new NbtTag<float>(name!, value.Value),
        NbtTag<double> value => new NbtTag<double>(name!, value.Value),
        NbtTag<string> value => new NbtTag<string>(name!, value.Value),
        _ => throw new NotSupportedException($"Can't copy NBT tag {tag.GetType().Name}.")
    };

    private static NbtCompound CopyCompound(NbtCompound compound, string? name)
    {
        var copy = new NbtCompound(name ?? string.Empty);
        foreach (var (childName, child) in compound)
            copy.Add(childName, Copy(child, childName));

        return copy;
    }

    private static NbtList CopyList(NbtList list, string? name)
    {
        var copy = new NbtList(list.ListType, name ?? string.Empty);
        foreach (var item in list)
            copy.Add(Copy(item, null));

        return copy;
    }
}
