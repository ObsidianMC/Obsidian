using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class RenameItemPacket
{
    [Field(0)]
    public string ItemName { get; private set; } = default!;

    public override void Populate(INetStreamReader reader) => this.ItemName = reader.ReadString();

    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        Obsidian.Events.MainEventHandler.RenameAnvilItemAsync(player, this.ItemName);
}
