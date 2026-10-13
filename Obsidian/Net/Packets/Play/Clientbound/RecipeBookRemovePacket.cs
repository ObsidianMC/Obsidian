namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Removes recipes from the client's recipe book, by their display ids.
/// </summary>
public partial class RecipeBookRemovePacket(IReadOnlyList<int> displayIds)
{
    public IReadOnlyList<int> DisplayIds { get; } = displayIds;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.DisplayIds.Count);

        foreach (var displayId in this.DisplayIds)
            writer.WriteVarInt(displayId);
    }
}
