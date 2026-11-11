using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

/// <summary>
/// A mob that shears take something off, like vanilla's <c>Shearable</c>: a sheep's wool, a snow golem's pumpkin, a
/// bogged's mushrooms and a mooshroom's mushrooms.
/// </summary>
internal interface IShearable
{
    /// <summary>Vanilla's <c>readyForShearing</c>: whether shearing it now takes something off.</summary>
    bool ReadyForShearing { get; }

    /// <summary>
    /// Vanilla's <c>shear</c>: plays the shearing sound from the mob in <paramref name="category"/> (the shearer's:
    /// a player's, or blocks for a dispenser) and takes off what it has.
    /// </summary>
    ValueTask ShearAsync(SoundCategory category);
}

internal static class Shearing
{
    /// <summary>The mob's shearing sound, following it, for the players around it.</summary>
    internal static void PlaySound(Mob mob, string sound, SoundCategory category) =>
        mob.PacketBroadcaster.QueuePacketToLevelInRange(mob.Level, mob.Position, new SoundEntityPacket
        {
            EntityId = mob.EntityId,
            SoundLocation = sound,
            Category = category,
            Volume = 1,
            Pitch = 1,
            Seed = Globals.Random.NextInt64()
        });
}
