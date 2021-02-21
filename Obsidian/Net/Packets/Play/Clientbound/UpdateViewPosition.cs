﻿using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    // Source: https://wiki.vg/index.php?title=Protocol#Update_View_Position
    [ClientOnly]
    public partial class UpdateViewPosition : ISerializablePacket
    {
        [Field(0), VarLength]
        public int ChunkX { get; }

        [Field(1), VarLength]
        public int ChunkZ { get; }

        public UpdateViewPosition(int chunkx, int chunkz)
        {
            ChunkX = chunkx;
            ChunkZ = chunkz;
        }

        public int Id => 0x40;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}