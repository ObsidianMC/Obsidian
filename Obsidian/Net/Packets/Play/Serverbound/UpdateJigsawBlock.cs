using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UpdateJigsawBlock : IServerboundPacket
{
    [Field(0)]
    public VectorF Location { get; set; }

    [Field(1)]
    public string Name { get; set; }

    [Field(2)]
    public string Target { get; set; }

    [Field(3)]
    public string Pool { get; set; }

    [Field(4)]
    public string FinalState { get; set; }

    [Field(5)]
    public string JointType { get; set; }

    public int Id => 0x29;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;

}
