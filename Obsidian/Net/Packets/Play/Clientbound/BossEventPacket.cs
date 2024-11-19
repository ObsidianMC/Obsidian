using Obsidian.Net.Actions.BossBar;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BossEventPacket
{
    [Field(0)]
    public BossBarAction Action { get; }

    public BossEventPacket(BossBarAction action)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }
}
