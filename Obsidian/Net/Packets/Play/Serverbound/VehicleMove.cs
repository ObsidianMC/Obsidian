using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class VehicleMove : IServerboundPacket
{
    [Field(0), DataFormat(typeof(double))]
    public VectorF Position { get; set; }

    [Field(1)]
    public float Yaw { get; set; }

    [Field(2)]
    public float Pitch { get; set; }

    public int Id => 0x15;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
