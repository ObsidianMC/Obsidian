﻿using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityMetadata : ISerializablePacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Entity Entity { get; set; }

        public int Id => 0x44;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
