using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ChangeDifficultyPacket
{
    [Field(0), ActualType(typeof(byte))]
    public Difficulty Difficulty { get; }

    [Field(1)]
    public bool DifficultyLocked { get; init; }
    public ChangeDifficultyPacket(Difficulty difficulty)
    {
        Difficulty = difficulty;
    }
}
