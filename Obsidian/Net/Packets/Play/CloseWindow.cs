﻿using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class CloseWindow : ISerializablePacket
    {
        [Field(0)]
        public byte WindowId { get; set; }

        public int Id => 0x0A;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.WindowId = await stream.ReadUnsignedByteAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            if (this.WindowId == 0)
                return;

            var loc = player.OpenedInventory.BlockPosition;

            var block = server.World.GetBlock(loc);

            if (block.Is(Material.Chest))
            {
                await player.client.QueuePacketAsync(new BlockAction
                {
                    Position = loc,
                    ActionId = 1,
                    ActionParam = 0,
                    BlockType = block.Id
                });
                await player.SendSoundAsync(Sounds.BlockChestClose, loc.SoundPosition);
            }
            else if (block.Is(Material.EnderChest))
            {
                await player.client.QueuePacketAsync(new BlockAction
                {
                    Position = loc,
                    ActionId = 1,
                    ActionParam = 0,
                    BlockType = block.Id
                });
                await player.SendSoundAsync(Sounds.BlockEnderChestClose, loc.SoundPosition);
            }

            player.OpenedInventory = null;
        }
    }
}
