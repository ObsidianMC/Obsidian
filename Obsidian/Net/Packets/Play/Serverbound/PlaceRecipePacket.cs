using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using Obsidian.Events;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlaceRecipePacket
{
    [Field(0)]
    public int ContainerId { get; private set; }

    [Field(1)]
    [VarLength]
    public int RecipeDisplayId { get; private set; }

    public string RecipeId => RecipeDisplayId >= 0 && RecipeDisplayId < RecipesRegistry.RecipeBookRecipes.Count
        ? RecipesRegistry.RecipeBookRecipes[RecipeDisplayId].Identifier : string.Empty;

    [Field(2)]
    public bool MakeAll { get; private set; }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        await MainEventHandler.PlaceBookRecipeAsync(player, ContainerId, RecipeDisplayId, MakeAll);
    }

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
        this.RecipeDisplayId = reader.ReadVarInt();
        this.MakeAll = reader.ReadBoolean();
    }
}
