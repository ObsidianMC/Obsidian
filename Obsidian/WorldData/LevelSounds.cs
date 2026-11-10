using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

/// <summary>
/// Sounds played as vanilla's <c>Level.playSound</c> plays them: to the players in range, with a seed from a
/// generator of the server's own. Vanilla seeds these from the level's sound seed generator, not its random, so they
/// must not draw from <see cref="AbstractLevel.GrowthRandom"/>, whose sequence parity depends on.
/// </summary>
internal static class LevelSounds
{
    /// <summary>Plays <paramref name="sound"/> at <paramref name="position"/>.</summary>
    public static void Play(
        AbstractLevel level,
        VectorD position,
        string sound,
        SoundCategory category,
        float volume = 1,
        float pitch = 1)
    {
        var packet = new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X, position.Y, position.Z),
            Category = category,
            Volume = volume,
            Pitch = pitch,
            Seed = Random.Shared.NextInt64()
        };

        level.PacketBroadcaster.BroadcastToLevelInRange(level, position, packet);
    }

    /// <summary>Plays a block's <paramref name="sound"/> at the centre of <paramref name="position"/>.</summary>
    public static void PlayAtBlock(AbstractLevel level, Vector position, string sound) =>
        Play(level, (VectorD)position + 0.5, sound, SoundCategory.Blocks);

    /// <summary>Plays <paramref name="sound"/> following <paramref name="entity"/>, as vanilla's entity sounds do.</summary>
    public static void PlayFrom(AbstractLevel level, Entity entity, string sound, SoundCategory category)
    {
        var packet = new SoundEntityPacket
        {
            SoundLocation = sound,
            Category = category,
            EntityId = entity.EntityId,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        };

        level.PacketBroadcaster.BroadcastToLevelInRange(level, entity.Position, packet);
    }
}
