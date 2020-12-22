using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class BlockBreakAnimation : IPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public PositionF Location { get; set; }

        /// <summary>
        /// 0-9 to set it, any other value to remove it
        /// </summary>
        [Field(2)]
        public sbyte DestroyStage { get; set; }

        public int Id => 0x08;

        public BlockBreakAnimation() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
