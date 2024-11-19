using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;

public partial class HelloPacket
{
    [Field(0)]
    public string Username { get; set; }

    [Field(1), ActualType(typeof(Guid))]
    public Guid? PlayerUuid { get; set; }
}
