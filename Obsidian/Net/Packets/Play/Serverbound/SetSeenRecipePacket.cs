using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetSeenRecipePacket : IServerboundPacket
{
    [Field(0)]
    public string RecipeId { get; private set; }

    public int Id => 0x29;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
