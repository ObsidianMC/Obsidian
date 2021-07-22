using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class SetDisplayedRecipe : IServerboundPacket
    {
        [Field(0)]
        public string RecipeId { get; private set; }

        public int Id => 0x1E;

        public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    }
}
