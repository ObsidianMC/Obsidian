using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.Registries;

internal static class JukeboxSongsRegistry
{
    // Built-in 1.21.11 song data. The sent registry order also defines level-event song IDs.
    internal static readonly (string Name, int Seconds)[] Songs =
    [
        ("11", 71), ("13", 178), ("5", 178), ("blocks", 345), ("cat", 185), ("chirp", 185),
        ("creator", 176), ("creator_music_box", 73), ("far", 174), ("lava_chicken", 134),
        ("mall", 197), ("mellohi", 96), ("otherside", 195), ("pigstep", 149), ("precipice", 299),
        ("relic", 218), ("stal", 150), ("strad", 188), ("tears", 175), ("wait", 238), ("ward", 251)
    ];

    internal static IEnumerable<string> Identifiers => Songs.Select(song => "minecraft:" + song.Name);

    internal static int GetSong(ItemStack item)
    {
        var name = item.GetComponent<JukeboxPlayableDataComponent>(DataComponentType.JukeboxPlayable)?.Song.Left;
        if (name == null && item.Holder.UnlocalizedName.StartsWith("minecraft:music_disc_", StringComparison.Ordinal))
            name = "minecraft:" + item.Holder.UnlocalizedName[21..];
        return Array.FindIndex(Songs, song => name == "minecraft:" + song.Name);
    }
}
