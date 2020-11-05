using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol#Update_View_Position
    /// </summary>
    public class UpdateViewPosition : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int ChunkX;

        [Field(1, Type = DataType.VarInt)]
        public int ChunkZ;

        public UpdateViewPosition(int chunkx, int chunkz)
        {
            this.ChunkX = chunkx;
            this.ChunkZ = chunkz;
        }

        public int Id => 0x40;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}