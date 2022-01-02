using Obsidian.Net.Actions.BossBar;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BossBarPacket : IClientboundPacket
{
    [Field(0)]
    public BossBarAction Action { get; }

    public int Id => 0x0D;

    public BossBarPacket(BossBarAction action)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }
}
