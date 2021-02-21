﻿using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class NameItem : IPacket
    {
        [Field(0)]
        public string ItemName { get; set; }

        public int Id => 0x20;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.ItemName = await stream.ReadStringAsync();
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
