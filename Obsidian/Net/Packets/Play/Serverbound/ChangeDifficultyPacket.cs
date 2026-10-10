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
            return default;

        var level = world.LevelData;
        if (level.DifficultyLocked)
            return default;

        world.SetDifficulty(level.Hardcore ? Difficulty.Hard : this.Difficulty, locked: false);
        obsidian.BroadcastDifficulty();
        return default;
    }
}
