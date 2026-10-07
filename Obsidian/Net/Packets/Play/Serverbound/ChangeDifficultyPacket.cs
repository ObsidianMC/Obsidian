namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ChangeDifficultyPacket
{
    /// <summary>
    /// Changes the world's difficulty like vanilla's <c>MinecraftServer.setDifficulty</c>: only for game masters and a
    /// singleplayer world's owner, never while it's locked, and a hardcore world stays hard. Every player learns the result.
    /// </summary>
    public override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        if (server is not Server obsidian || !obsidian.MayChangeDifficulty(player))
            return default;

        var level = obsidian.DefaultWorld.LevelData;
        if (level.DifficultyLocked)
            return default;

        level.Difficulty = level.Hardcore ? Difficulty.Hard : this.Difficulty;
        obsidian.BroadcastDifficulty();
        return default;
    }
}
