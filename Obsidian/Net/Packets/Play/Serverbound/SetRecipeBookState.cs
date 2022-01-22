using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetRecipeBookState : IServerboundPacket
{
    [Field(0), VarLength, ActualType(typeof(int))]
    public RecipeBookType BookId { get; private set; }

    [Field(1)]
    public bool BookOpen { get; private set; }

    [Field(2)]
    public bool FilterActive { get; private set; }

    public int Id => 0x1E;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;

}
