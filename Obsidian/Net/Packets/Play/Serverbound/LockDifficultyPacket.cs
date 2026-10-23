using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class LockDifficultyPacket
{
    /// <summary>
    /// Locks (or unlocks) the world's difficulty, like vanilla's <c>MinecraftServer.setDifficultyLocked</c>: only for game
    /// masters and a singleplayer world's owner. Every player learns the result.
    /// </summary>
    public override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (server is not Server obsidian
            || obsidian.DefaultWorld is not World world
            || !obsidian.MayChangeDifficulty(player))
            return default;

        world.SetDifficulty(world.LevelData.Difficulty, this.Locked);
        obsidian.BroadcastDifficulty();
        return default;
    }
}
