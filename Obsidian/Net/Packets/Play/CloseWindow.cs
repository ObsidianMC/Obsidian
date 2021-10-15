﻿using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class CloseWindow : IClientboundPacket, IServerboundPacket
    {
        [Field(0)]
        public byte WindowId { get; private set; }

        public int Id => 0x09;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            if (WindowId == 0 || !player.OpenedInventory.BlockPosition.HasValue)
                return;

            var position = player.OpenedInventory.BlockPosition.Value;

            var b = server.World.GetBlock(position);

            if (!b.HasValue)
                return;

            var block = (Block)b;
            if (block.Is(Material.Chest))
            {
                await player.client.QueuePacketAsync(new BlockAction
                {
                    Position = position,
                    ActionId = 1,
                    ActionParam = 0,
                    BlockType = block.Id
                });
                await player.SendSoundAsync(Sounds.BlockChestClose, position.SoundPosition);
            }
            else if (block.Is(Material.EnderChest))
            {
                await player.client.QueuePacketAsync(new BlockAction
                {
                    Position = position,
                    ActionId = 1,
                    ActionParam = 0,
                    BlockType = block.Id
                });
                await player.SendSoundAsync(Sounds.BlockEnderChestClose, position.SoundPosition);
            }

            player.OpenedInventory = null;
        }
    }
}
