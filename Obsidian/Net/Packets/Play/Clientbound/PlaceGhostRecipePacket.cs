using Obsidian.API.Crafting;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Shows <paramref name="recipe"/>'s ingredients as ghosts in the crafting grid of the window
/// <paramref name="containerId"/>, for a recipe the player picked in their book but lacks the items for.
/// </summary>
public partial class PlaceGhostRecipePacket(int containerId, IRecipeWithResult recipe)
{
    public int ContainerId { get; } = containerId;

    public IRecipeWithResult Recipe { get; } = recipe;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.ContainerId);
        RecipeBookAddPacket.WriteRecipeDisplay(writer, this.Recipe);
    }
}
