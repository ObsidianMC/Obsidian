using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ChangeDifficultyPacket : IClientboundPacket
{
    [Field(0), ActualType(typeof(byte))]
    public Difficulty Difficulty { get; }

    [Field(1)]
    public bool DifficultyLocked { get; init; }

    public int Id => 0x0B;

    public ChangeDifficultyPacket(Difficulty difficulty)
    {
        Difficulty = difficulty;
    }
}
