using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlaceRecipePacket : IServerboundPacket
{
    [Field(0)]
    public sbyte WindowId { get; private set; }

    [Field(1)]
    public string RecipeId { get; private set; }

    [Field(2)]
    public bool MakeAll { get; private set; }

    public int Id => 0x22;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await player.client.QueuePacketAsync(new PlaceGhostRecipePacket(WindowId, RecipeId));
    }
}
