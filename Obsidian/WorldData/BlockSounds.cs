using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

/// <summary>
/// Sounds that blocks' own routines play, like vanilla's <c>Level.playSound</c> without a player: at the block's centre,
/// for everyone near it.
/// </summary>
internal static class BlockSounds
{
    /// <summary>
    /// Plays <paramref name="sound"/> in the blocks category at the centre of <paramref name="position"/>.
    /// </summary>
    /// <remarks>
    /// Vanilla draws the sound's seed from a random of its own rather than the level's, so this draws nothing from
    /// the level's random either, which keeps the level's draws vanilla's.
    /// </remarks>
    public static void Play(AbstractLevel level, Vector position, string sound, float volume, float pitch)
    {
        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        level.PacketBroadcaster.BroadcastToLevelInRange(level, center, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
            Category = SoundCategory.Blocks,
            Volume = volume,
            Pitch = pitch,
            Seed = Random.Shared.NextInt64()
        });
    }
}
