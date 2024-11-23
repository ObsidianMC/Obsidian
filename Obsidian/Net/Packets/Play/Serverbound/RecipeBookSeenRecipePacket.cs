using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class RecipeBookSeenRecipePacket
{
    [Field(0)]
    public string RecipeId { get; private set; } = default!;

    public override void Populate(INetStreamReader reader)
    {
        RecipeId = reader.ReadString();
    }
}
