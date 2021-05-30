using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class BlockBreakAnimation : IClientboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public VectorF Position { get; set; }

        /// <summary>
        /// 0-9 to set it, any other value to remove it
        /// </summary>
        [Field(2)]
        public sbyte DestroyStage { get; set; }

        public int Id => 0x08;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
