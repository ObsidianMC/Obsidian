using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class GenerateStructure : IServerboundPacket
{
    [Field(0)]
    public VectorF Position { get; set; }

    [Field(1), VarLength]
    public int Levels { get; set; }
    
    [Field(2)]
    public bool KeepJigsaws { get; set; }

    public int Id => 0x0E;

    public ValueTask HandleAsync(Server server, Player player)
    {
        server.Logger.LogDebug(this.AsString());
        return ValueTask.CompletedTask;
    }
}
