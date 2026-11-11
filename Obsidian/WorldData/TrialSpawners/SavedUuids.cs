using Obsidian.Entities;
using Obsidian.Nbt;

namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// Sets of UUIDs as trial spawners and vaults save them (vanilla's <c>UUIDUtil.CODEC_SET</c> and
/// <c>CODEC_LINKED_SET</c>): a list of four-int arrays, left out when empty.
/// </summary>
internal static class SavedUuids
{
    /// <summary>
    /// The set saved under <paramref name="name"/>, in its saved order without repeats. Like vanilla's lenient field,
    /// a set that doesn't read is empty.
    /// </summary>
    public static ImmutableArray<Guid> Read(NbtCompound tag, string name)
    {
        if (!tag.TryGetTag<NbtList>(name, out var list))
            return [];

        var uuids = ImmutableArray.CreateBuilder<Guid>();
        foreach (var element in list)
        {
            if (element is not NbtArray<int> ints || ints.GetArray().Length != 4)
                return [];

            var uuid = EntityNbt.UuidFromInts(ints.GetArray());
            if (!uuids.Contains(uuid))
                uuids.Add(uuid);
        }

        return uuids.ToImmutable();
    }

    public static void Write(NbtCompound tag, string name, ImmutableArray<Guid> uuids)
    {
        if (uuids.IsEmpty)
            return;

        var list = new NbtList(NbtTagType.IntArray, name);
        foreach (var uuid in uuids)
            list.Add(new NbtArray<int>(string.Empty, EntityNbt.UuidToInts(uuid)));

        tag.Add(list);
    }
}
