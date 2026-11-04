using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChangeDifficultyPacket
{
    /// <summary>
    /// Changes the world's difficulty like vanilla's <c>MinecraftServer.setDifficulty</c>: only for game masters and a
    /// singleplayer world's owner, never while it's locked, and a hardcore world stays hard. Every player learns the result.
    /// </summary>
    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (server is not Server obsidian
            || obsidian.DefaultWorld is not World world
            || !obsidian.MayChangeDifficulty(player))
        {
            return default;
        }

        // Not through a lock, which the change checks as it makes it.
        var difficulty = world.LevelData.Hardcore ? Difficulty.Hard : this.Difficulty;
        if (!world.SetDifficulty(difficulty, force: false))
            return default;

        obsidian.BroadcastDifficulty();
        return default;
    }
}
