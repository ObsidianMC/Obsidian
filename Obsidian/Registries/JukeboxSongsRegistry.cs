using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.Registries;

internal static class JukeboxSongsRegistry
{
    // Built-in 26.3 song data: each song's length and the signal a comparator reads from a jukebox playing it
    // (vanilla's comparator_output). The sent registry order also defines level-event song IDs.
    internal static readonly (string Name, int Seconds, int Comparator)[] Songs =
    [
        ("11", 71, 11), ("13", 178, 1), ("5", 178, 15), ("blocks", 345, 3), ("bounce", 234, 8), ("cat", 185, 2),
        ("chirp", 185, 4), ("creator", 176, 12), ("creator_music_box", 73, 11), ("far", 174, 5),
        ("lava_chicken", 134, 9), ("mall", 197, 6), ("mellohi", 96, 7), ("otherside", 195, 14), ("pigstep", 149, 13),
        ("precipice", 299, 13), ("relic", 218, 14), ("stal", 150, 8), ("strad", 188, 9), ("tears", 175, 10),
        ("wait", 238, 12), ("ward", 251, 10)
    ];

    /// <summary>
    /// Vanilla's <c>JukeboxBlockEntity.getComparatorOutput</c>: the comparator output of the song of the disc in
    /// <paramref name="jukebox"/>, or 0 without one. It reads the disc, playing or not.
    /// </summary>
    internal static int ComparatorOutput(Obsidian.WorldData.DataBlockEntity jukebox)
    {
        var record = jukebox.SnapshotTag<Obsidian.Nbt.NbtCompound>("RecordItem");
        if (record?.ItemFromNbt() is not { IsAir: false } disc)
            return 0;

        var song = GetSong(disc);
        return song < 0 ? 0 : Songs[song].Comparator;
    }

    internal static IEnumerable<string> Identifiers => Songs.Select(song => "minecraft:" + song.Name);

    internal static int GetSong(ItemStack item)
    {
        var name = item.GetComponent<JukeboxPlayableDataComponent>(DataComponentType.JukeboxPlayable)?.Song.Left;
        if (name == null && item.Holder.UnlocalizedName.StartsWith("minecraft:music_disc_", StringComparison.Ordinal))
            name = "minecraft:" + item.Holder.UnlocalizedName[21..];
        return Array.FindIndex(Songs, song => name == "minecraft:" + song.Name);
    }
}
