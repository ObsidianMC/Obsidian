using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlaceRecipePacket
{
    [Field(0)]
    public int ContainerId { get; private set; }

    [Field(1)]
    public string RecipeId { get; private set; } = default!;

    [Field(2)]
    public bool MakeAll { get; private set; }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        await player.client.QueuePacketAsync(new PlaceGhostRecipePacket(ContainerId, RecipeId));
    }

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
        this.RecipeId = reader.ReadString();
        this.MakeAll = reader.ReadBoolean();
    }
}
