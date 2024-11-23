using Obsidian.Net.Actions.BossBar;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class BossEventPacket(BossBarAction action)
{
    [Field(0)]
    public BossBarAction Action { get; } = action ?? throw new ArgumentNullException(nameof(action));

    public override void Serialize(INetStreamWriter writer)
    {
        this.Action.WriteTo(writer);
    }
}
