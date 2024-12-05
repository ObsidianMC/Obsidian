using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ChangeDifficultyPacket(Difficulty difficulty)
{
    [Field(0), ActualType(typeof(byte))]
    public Difficulty Difficulty { get; } = difficulty;

    [Field(1)]
    public bool DifficultyLocked { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteByte(this.Difficulty);
        writer.WriteBoolean(this.DifficultyLocked);
    }
}
