namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Shows the ingredients of a recipe display as ghosts in a window's crafting grid, for a recipe the player picked in
/// their book but lacks the items for.
/// </summary>
public partial class PlaceGhostRecipePacket
{
    /// <param name="containerId">The window.</param>
    /// <param name="entry">The recipe book entry the player picked, whose display shows.</param>
    internal PlaceGhostRecipePacket(int containerId, RecipeBookEntry entry)
    {
        this.ContainerId = containerId;
        this.Entry = entry;
    }

    public int ContainerId { get; }

    internal RecipeBookEntry Entry { get; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.ContainerId);
        RecipeBookAddPacket.WriteRecipeDisplay(writer, this.Entry);
    }
}
