using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class PlaceGhostRecipePacket : IClientboundPacket
{
    [Field(0)]
    public sbyte WindowId { get; }

    [Field(1)]
    public string RecipeId { get; }

    public int Id => 0x37;

    public PlaceGhostRecipePacket(sbyte windowId, string recipeId)
    {
        WindowId = windowId;
        RecipeId = recipeId;
    }
}
