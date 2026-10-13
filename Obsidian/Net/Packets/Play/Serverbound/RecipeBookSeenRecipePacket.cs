using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class RecipeBookSeenRecipePacket
{
    [Field(0)]
    [VarLength]
    public int RecipeDisplayId { get; private set; }

    public string RecipeId => RecipesRegistry.Current.GetBookEntry(RecipeDisplayId)?.Recipe.Identifier ?? string.Empty;

    public override void Populate(INetStreamReader reader)
    {
        RecipeDisplayId = reader.ReadVarInt();
    }
}
