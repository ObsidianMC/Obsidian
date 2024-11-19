using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDisplayObjectivePacket
{
    [Field(0), ActualType(typeof(sbyte))]
    public ScoreboardPosition Position { get; init; }

    [Field(1), FixedLength(16)]
    public string ScoreName { get; init; }
}
