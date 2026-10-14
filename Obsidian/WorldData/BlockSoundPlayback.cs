using Obsidian.API.Blocks;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

/// <summary>Block interactions heard by nearby players; the acting client predicts its own sound.</summary>
internal static class BlockSoundPlayback
{
    internal static SoundPacket Packet(IBlock block, Vector position, bool breaking)
    {
        var type = BlockSoundsRegistry.Get(block.GetHashCode());
        var sound = breaking ? type.Break : type.Place;
        // Vanilla 1.21.11 BlockItem placement and client level event 2001 use these volume and pitch adjustments.
        return new SoundPacket
        {
            SoundLocation = sound.ResourceLocation,
            FixedRange = sound.FixedRange,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = (type.Volume + 1) / 2,
            Pitch = type.Pitch * 0.8f,
            Seed = Random.Shared.NextInt64()
        };
    }

    internal static void Play(ILevel level, IBlock block, Vector position, bool breaking, int actor)
    {
        var packet = Packet(block, position, breaking);
        level.PacketBroadcaster.QueuePacketToLevelInRange(level, (VectorD)position + 0.5, packet, actor);
    }
}
