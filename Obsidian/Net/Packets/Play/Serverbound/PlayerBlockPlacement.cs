using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class PlayerBlockPlacement : IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Hand Hand { get; private set; } // Hand it was placed from. 0 = Main, 1 = Off

        [Field(1)]
        public Vector Position { get; private set; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public BlockFace Face { get; private set; }

        [Field(3), DataFormat(typeof(float))]
        public VectorF Cursor { get; private set; }

        [Field(6)]
        public bool InsideBlock { get; private set; }

        public int Id => 0x2E;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            var currentItem = player.GetHeldItem();

            var itemType = currentItem.Type;

            // TODO: this better
            if (itemType == Material.WaterBucket)
                itemType = Material.Water;
            if (itemType == Material.LavaBucket)
                itemType = Material.Lava;

            Block block;
            try
            {
                block = Registry.GetBlock(itemType);
            }
            catch //item is not a block so just return
            { 
                return; 
            }

            var position = this.Position;

            var b = server.World.GetBlock(position);
            if (b is null) { return; }
            var interactedBlock = (Block)b;

            if (interactedBlock.IsInteractable && !player.Sneaking)
            {
                var arg = await server.Events.InvokeBlockInteractAsync(new BlockInteractEventArgs(player, block, this.Position));

                if (arg.Cancel)
                    return;

                player.LastClickedBlock = interactedBlock;

                // TODO open chests/Crafting inventory ^ ^

                if (Server.LastInventoryId == byte.MaxValue)
                    Server.LastInventoryId = 1;

                var maxId = Math.Max((byte)1, ++Server.LastInventoryId);
                var meta = server.World.GetBlockMeta(position);

                if (meta is not null && meta.Value.InventoryId != Guid.Empty)
                {
                    if (server.CachedWindows.TryGetValue(meta.Value.InventoryId, out var inventory))
                    {
                        // Globals.PacketLogger.LogDebug($"Opened window with id of: {meta.InventoryId} {(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                        await player.OpenInventoryAsync(inventory);
                        await player.client.QueuePacketAsync(new BlockAction
                        {
                            Position = position,
                            ActionId = 1,
                            ActionParam = 1,
                            BlockType = interactedBlock.Id
                        });
                        await player.SendSoundAsync(Sounds.BlockChestOpen, position.SoundPosition, SoundCategory.Blocks);

                        player.OpenedInventory = inventory;
                    }

                    return;
                }

                var type = interactedBlock.Material;

                if (type == Material.Chest) // TODO check if chest its next to another single chest
                {
                    var inventory = new Inventory(InventoryType.Generic)
                    {
                        Owner = player.Uuid,
                        Title = ChatMessage.Simple("Chest"),
                        Id = maxId,
                        BlockPosition = position
                    };

                    await player.OpenInventoryAsync(inventory);
                    await player.client.QueuePacketAsync(new BlockAction
                    {
                        Position = position,
                        ActionId = 1,
                        ActionParam = 1,
                        BlockType = interactedBlock.Id
                    });
                    await player.SendSoundAsync(Sounds.BlockChestOpen, position.SoundPosition, SoundCategory.Blocks);

                    var invUuid = Guid.NewGuid();

                    var blockMeta = new BlockMetaBuilder().WithInventoryId(invUuid).Build();

                    server.World.SetBlockMeta(position, blockMeta);

                    server.CachedWindows.TryAdd(invUuid, inventory);

                    player.OpenedInventory = inventory;
                }
                else if (type == Material.EnderChest)
                {
                    var enderChest = new Inventory(InventoryType.Generic)
                    {
                        Owner = player.Uuid,
                        Title = ChatMessage.Simple("Ender Chest"),
                        Id = maxId
                    };

                    var invUuid = Guid.NewGuid();

                    var blockMeta = new BlockMetaBuilder().WithInventoryId(invUuid).Build();

                    server.World.SetBlockMeta(position, blockMeta);

                    server.CachedWindows.TryAdd(invUuid, enderChest);

                    player.OpenedInventory = enderChest;

                    await player.OpenInventoryAsync(enderChest);
                    await player.client.QueuePacketAsync(new BlockAction
                    {
                        Position = position,
                        ActionId = 1,
                        ActionParam = 1,
                        BlockType = interactedBlock.Id
                    });
                    await player.SendSoundAsync(Sounds.BlockEnderChestOpen, position.SoundPosition, SoundCategory.Blocks);
                }
                else if (type == Material.CraftingTable)
                {
                    var crafting = new Inventory(InventoryType.Crafting)
                    {
                        Title = ChatMessage.Simple("Crafting Table"),
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = crafting;

                    await player.OpenInventoryAsync(crafting);
                }
                else if (type == Material.Furnace || type == Material.BlastFurnace || type == Material.Smoker)
                {
                    InventoryType actualType = type == Material.Furnace ? InventoryType.Furnace :
                        type == Material.BlastFurnace ? InventoryType.BlastFurnace : InventoryType.Smoker;

                    var furnace = new Inventory(actualType)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = furnace;

                    await player.OpenInventoryAsync(furnace);
                }
                else if (type == Material.EnchantingTable)
                {
                    var enchantmentTable = new Inventory(InventoryType.Enchantment)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = enchantmentTable;

                    await player.OpenInventoryAsync(enchantmentTable);
                }
                else if (type == Material.Anvil || type == Material.SmithingTable) // TODO implement other anvil types
                {
                    var anvil = new Inventory(InventoryType.Anvil)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = anvil;

                    await player.OpenInventoryAsync(anvil);
                }
                else if (type >= Material.ShulkerBox && type <= Material.BlackShulkerBox)
                {
                    var box = new Inventory(InventoryType.ShulkerBox) // TODO shulker box functionality
                    {
                        Owner = player.Uuid,
                        Title = ChatMessage.Simple("Shulker Box"),
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Loom)
                {
                    var box = new Inventory(InventoryType.Loom)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Barrel)
                {
                    var box = new Inventory(InventoryType.Generic)
                    {
                        //Owner = player.Uuid,
                        Title = ChatMessage.Simple("Barrel"),
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.CartographyTable)
                {
                    var box = new Inventory(InventoryType.CartographyTable)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Stonecutter)
                {
                    var box = new Inventory(InventoryType.Stonecutter)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Grindstone)
                {
                    var box = new Inventory(InventoryType.Grindstone)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.BrewingStand)
                {
                    var box = new Inventory(InventoryType.BrewingStand)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Lectern)
                {
                    var box = new Inventory(InventoryType.Lectern)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Hopper || type == Material.HopperMinecart)
                {
                    var box = new Inventory(InventoryType.Hopper)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }

                return;
            }

            if (player.Gamemode != Gamemode.Creative)
                player.Inventory.RemoveItem(player.inventorySlot);

            switch (Face) // TODO fix this for logs
            {
                case BlockFace.Bottom:
                    position.Y -= 1;
                    break;

                case BlockFace.Top:
                    position.Y += 1;
                    break;

                case BlockFace.North:
                    position.Z -= 1;
                    break;

                case BlockFace.South:
                    position.Z += 1;
                    break;

                case BlockFace.West:
                    position.X -= 1;
                    break;

                case BlockFace.East:
                    position.X += 1;
                    break;

                default:
                    break;
            }

            // TODO calculate the block state
            server.World.SetBlock(position, block, true);
            await server.BroadcastBlockPlacementAsync(player, block, position);
        }
    }
}
