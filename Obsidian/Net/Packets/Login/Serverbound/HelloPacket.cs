using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;

public partial class HelloPacket
{
    [Field(0)]
    public string Username { get; private set; } = default!;

    [Field(1), ActualType(typeof(Guid))]
    public Guid PlayerUuid { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Username = reader.ReadString(16);
        this.PlayerUuid = reader.ReadGuid();
    }
}
