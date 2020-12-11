using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Items;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.Registry;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public class PlayerBlockPlacement : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public Hand Hand { get; set; } // hand it was placed from. 0 is main, 1 is off

        [Field(1)]
        public Position Location { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public BlockFace Face { get; set; }

        [Field(3)]
        public float CursorX { get; set; }

        [Field(4)]
        public float CursorY { get; set; }

        [Field(5)]
        public float CursorZ { get; set; }

        [Field(6)]
        public bool InsideBlock { get; set; }

        public int Id => 0x2E;

        public PlayerBlockPlacement() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Hand = (Hand)await stream.ReadVarIntAsync();
            this.Location = await stream.ReadPositionAsync();
            this.Face = (BlockFace)await stream.ReadVarIntAsync();
            this.CursorX = await stream.ReadFloatAsync();
            this.CursorY = await stream.ReadFloatAsync();
            this.CursorZ = await stream.ReadFloatAsync();
            this.InsideBlock = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            var currentItem = player.GetHeldItem();

            var block = Registry.GetBlock(currentItem.Type);

            var location = this.Location;

            var interactedBlock = server.World.GetBlock(location);

            if (interactedBlock.CanInteract() && !player.Sneaking)
            {
                var arg = await server.Events.InvokeBlockInteractAsync(new BlockInteractEventArgs(player, block, this.Location));

                if (arg.Cancel)
                    return;

                //TODO open chests/Crafting inventory ^ ^

                var maxId = Math.Max((byte)1, ++Inventory.LastId);
                if (maxId == byte.MaxValue)
                    maxId = 1;

                if (server.World.GetBlockMeta(location) is BlockMeta meta && meta.InventoryId != Guid.Empty)
                {
                    if (server.CachedWindows.TryGetValue(meta.InventoryId, out var inventory))
                    {
                        Globals.PacketLogger.LogDebug($"Opened window with id of: {meta.InventoryId} {(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                        await player.OpenInventoryAsync(inventory);
                        await player.client.QueuePacketAsync(new BlockAction
                        {
                            Location = location,
                            ActionId = 1,
                            ActionParam = 1,
                            BlockType = interactedBlock.Id
                        });
                        await player.SendSoundAsync(Sounds.BlockChestOpen, location.SoundPosition, SoundCategory.Blocks);

                        player.OpenedInventory = inventory;
                    }

                    return;
                }

                var type = interactedBlock.Type;

                if (type == Materials.Chest)
                {
                    var inventory = new Inventory(player.Uuid)
                    {
                        Title = "Chest",
                        Type = InventoryType.Generic,
                        Id = maxId,
                        Size = 9 * 3,
                        BlockPosition = location
                    };

                    await player.OpenInventoryAsync(inventory);
                    await player.client.QueuePacketAsync(new BlockAction
                    {
                        Location = location,
                        ActionId = 1,
                        ActionParam = 1,
                        BlockType = interactedBlock.Id
                    });
                    await player.SendSoundAsync(Sounds.BlockChestOpen, location.SoundPosition, SoundCategory.Blocks);

                    var invUuid = Guid.NewGuid();

                    var blockMeta = new BlockMetaBuilder().WithInventoryId(invUuid).Build();

                    server.World.SetBlockMeta(location, blockMeta);

                    server.CachedWindows.TryAdd(invUuid, inventory);

                    player.OpenedInventory = inventory;
                }
                else if (type == Materials.EnderChest)
                {
                    var enderChest = new Inventory(player.Uuid)
                    {
                        Title = "Ender Chest",
                        Type = InventoryType.Generic,
                        Size = 9 * 3,
                    };

                    var invUuid = Guid.NewGuid();

                    var blockMeta = new BlockMetaBuilder().WithInventoryId(invUuid).Build();

                    server.World.SetBlockMeta(location, blockMeta);

                    server.CachedWindows.TryAdd(invUuid, enderChest);

                    player.OpenedInventory = enderChest;

                    await player.OpenInventoryAsync(enderChest);
                    await player.client.QueuePacketAsync(new BlockAction
                    {
                        Location = location,
                        ActionId = 1,
                        ActionParam = 1,
                        BlockType = interactedBlock.Id
                    });
                    await player.SendSoundAsync(Sounds.BlockEnderChestOpen, location.SoundPosition, SoundCategory.Blocks);
                }
                else if (type == Materials.CraftingTable)
                {
                    var crafting = new Inventory
                    {
                        Title = "Crafting Table",
                        Type = InventoryType.Crafting,
                        Id = maxId,
                        Size = 2 * 5,
                        BlockPosition = location
                    };

                    player.OpenedInventory = crafting;

                    await player.OpenInventoryAsync(crafting);
                }
                else if (type == Materials.Furnace || type == Materials.BlastFurnace || type == Materials.Smoker)
                {
                    InventoryType actualType = type == Materials.Furnace ? InventoryType.Furnace :
                        type == Materials.BlastFurnace ? InventoryType.BlastFurnace : InventoryType.Smoker;

                    var furnace = new Inventory
                    {
                        Type = actualType,
                        Id = maxId,
                        Size = 3,
                        BlockPosition = location
                    };

                    player.OpenedInventory = furnace;

                    await player.OpenInventoryAsync(furnace);
                }
                else if (type == Materials.EnchantingTable)
                {
                    var enchantmentTable = new Inventory
                    {
                        Type = InventoryType.Enchantment,
                        Id = maxId,
                        Size = 2,
                        BlockPosition = location
                    };

                    player.OpenedInventory = enchantmentTable;

                    await player.OpenInventoryAsync(enchantmentTable);
                }
                else if (type == Materials.Anvil || type == Materials.SmithingTable)//TODO implement other anvil types
                {
                    var anvil = new Inventory
                    {
                        Type = InventoryType.Anvil,
                        Id = maxId,
                        Size = 3,
                        BlockPosition = location
                    };

                    player.OpenedInventory = anvil;

                    await player.OpenInventoryAsync(anvil);
                }
                else if (type >= Materials.ShulkerBox && type <= Materials.BlackShulkerBox)
                {
                    var box = new Inventory(player.Uuid)//TODO shulker box functionality
                    {
                        Title = "Shulker Box",
                        Type = InventoryType.ShulkerBox,
                        Id = maxId,
                        Size = 9 * 3,
                        BlockPosition = location
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Loom)
                {
                    var box = new Inventory
                    {
                        Type = InventoryType.Loom,
                        Id = maxId,
                        Size = 4,
                        BlockPosition = location
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Barrel)
                {
                    var box = new Inventory(player.Uuid)
                    {
                        Title = "Barrel",
                        Type = InventoryType.Generic,
                        Id = maxId,
                        Size = 9 * 3,
                        BlockPosition = location
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.CartographyTable)
                {
                    var box = new Inventory(player.Uuid)
                    {
                        Type = InventoryType.CartographyTable,
                        Id = maxId,
                        Size = 3,
                        BlockPosition = location
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Stonecutter)
                {
                    var box = new Inventory
                    {
                        Type = InventoryType.Stonecutter,
                        Id = maxId,
                        Size = 2,
                        BlockPosition = location
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Grindstone)
                {
                    var box = new Inventory
                    {
                        Type = InventoryType.Grindstone,
                        Id = maxId,
                        Size = 3,
                        BlockPosition = location
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }

                return;
            }

            if (player.Gamemode != Gamemode.Creative)
                player.Inventory.RemoveItem(player.CurrentSlot);

            switch (this.Face) // TODO fix this for logs
            {
                case BlockFace.Bottom:
                    location.Y -= 1;
                    break;

                case BlockFace.Top:
                    location.Y += 1;
                    break;

                case BlockFace.North:
                    location.Z -= 1;
                    break;

                case BlockFace.South:
                    location.Z += 1;
                    break;

                case BlockFace.West:
                    location.X -= 1;
                    break;

                case BlockFace.East:
                    location.X += 1;
                    break;

                default:
                    break;
            }

            block.Location = location;
            server.World.SetBlock(location, block);

            await server.BroadcastBlockPlacementAsync(player, block, location);
        }
    }

}