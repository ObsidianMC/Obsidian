using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlaceGhostRecipePacket(int containerId, string recipeId)
{
    [Field(0)]
    public int ContainerId { get; } = containerId;

    [Field(1)]
    public string RecipeId { get; } = recipeId;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.ContainerId);
        writer.WriteString(this.RecipeId);
    }
}
