using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

/// <summary>The shared effects of putting out candles and campfires, by hand or by water.</summary>
internal static class BlockLighting
{
    internal static bool CanLight(IBlock block) => block.GetProperty("lit") == "false"
        && block.GetProperty("waterlogged") != "true"
        && block.BlockClass() is "CampfireBlock" or "CandleBlock" or "CandleCakeBlock";

    internal static void Extinguished(AbstractLevel level, Vector position, IBlock block, IEntity? source)
    {
        var candle = block.BlockClass() is "CandleBlock" or "CandleCakeBlock";
        Play(level, position, candle ? "minecraft:block.candle.extinguish" : "minecraft:entity.generic.extinguish_fire");
        level.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)position + 0.5, source);
        // Candle smoke uses Level.addParticle and campfire smoke is client-only. Neither sends a server particle packet.
    }

    internal static void Play(AbstractLevel level, Vector position, string sound, float volume = 1,
        float pitch = 1, int except = -1)
    {
        level.PacketBroadcaster.BroadcastToLevelInRange(level, position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = volume,
            Pitch = pitch,
            Seed = Globals.Random.NextInt64()
        }, except);
    }
}
