using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class EditBook : IServerboundPacket
{
    [Field(0), VarLength, ActualType(typeof(int))]
    public Hand Hand { get; set; }
    
    [Field(1), VarLength]
    public int Count { get; set; }
    
    [Field(2), ActualType(typeof(string))]
    public List<string> Entries { get; set; }
    
    [Field(3)]
    public bool HasTitle { get; set; }
    
    [Field(4)]
    public string Title { get; set; }

    public int Id => 0x15;

    public ValueTask HandleAsync(Server server, Player player)
    {
        server.Logger.LogDebug(this.AsString());
        return ValueTask.CompletedTask;
    }
}
