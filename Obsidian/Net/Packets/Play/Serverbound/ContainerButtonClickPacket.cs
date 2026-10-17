using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ContainerButtonClickPacket
{
    [Field(0)]
    public int ContainerId { get; private set; }

    [Field(1)]
    public int ButtonId { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
        this.ButtonId = reader.ReadVarInt();
    }

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) => player switch
    {
        Obsidian.Entities.PlayerBase { OpenedContainer: MenuContainer } concrete =>
            concrete.ClickMenuButtonAsync(this.ContainerId, this.ButtonId),
        Obsidian.Entities.PlayerBase concrete => concrete.EnchantAsync(this.ContainerId, this.ButtonId),
        _ => ValueTask.CompletedTask
    };
}
