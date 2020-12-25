using Obsidian.API;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Events.EventArgs;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using Obsidian.Util.Registry;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class PlayerBlockPlacement : IPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Hand Hand { get; set; } // hand it was placed from. 0 is main, 1 is off

        [Field(1)]
        public Position Position { get; set; }

        [Field(2), ActualType(typeof(int)), VarLength]
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

        public PlayerBlockPlacement()
        {
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Hand = (Hand)await stream.ReadVarIntAsync();
            this.Position = await stream.ReadPositionAsync();
            this.Face = (BlockFace)await stream.ReadVarIntAsync();
            this.CursorX = await stream.ReadFloatAsync();
            this.CursorY = await stream.ReadFloatAsync();
            this.CursorZ = await stream.ReadFloatAsync();
            this.InsideBlock = await stream.ReadBooleanAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            var currentItem = player.GetHeldItem();

            var block = Registry.GetBlock(currentItem.Type);

            var position = this.Position;

            var interactedBlock = server.World.GetBlock(position);

            if (interactedBlock.IsInteractable && !player.Sneaking)
            {
                var arg = await server.Events.InvokeBlockInteractAsync(new BlockInteractEventArgs(player, block, this.Position));

                if (arg.Cancel)
                    return;

                player.LastClickedBlock = interactedBlock;

                //TODO open chests/Crafting inventory ^ ^

                if (Server.LastInventoryId == byte.MaxValue)
                    Server.LastInventoryId = 1;

                var maxId = Math.Max((byte)1, ++Server.LastInventoryId);

                if (server.World.GetBlockMeta(position) is BlockMeta meta && meta.InventoryId != Guid.Empty)
                {
                    if (server.CachedWindows.TryGetValue(meta.InventoryId, out var inventory))
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

                if (type == Materials.Chest) // TODO check if chest its next to another single chest
                {
                    var inventory = new Inventory(InventoryType.Generic)
                    {
                        Owner = player.Uuid,
                        Title = IChatMessage.Simple("Chest"),
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
                else if (type == Materials.EnderChest)
                {
                    var enderChest = new Inventory(InventoryType.Generic)
                    {
                        Owner = player.Uuid,
                        Title = IChatMessage.Simple("Ender Chest"),
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
                else if (type == Materials.CraftingTable)
                {
                    var crafting = new Inventory(InventoryType.Crafting)
                    {
                        Title = IChatMessage.Simple("Crafting Table"),
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = crafting;

                    await player.OpenInventoryAsync(crafting);
                }
                else if (type == Materials.Furnace || type == Materials.BlastFurnace || type == Materials.Smoker)
                {
                    InventoryType actualType = type == Materials.Furnace ? InventoryType.Furnace :
                        type == Materials.BlastFurnace ? InventoryType.BlastFurnace : InventoryType.Smoker;

                    var furnace = new Inventory(actualType)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = furnace;

                    await player.OpenInventoryAsync(furnace);
                }
                else if (type == Materials.EnchantingTable)
                {
                    var enchantmentTable = new Inventory(InventoryType.Enchantment)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = enchantmentTable;

                    await player.OpenInventoryAsync(enchantmentTable);
                }
                else if (type == Materials.Anvil || type == Materials.SmithingTable)//TODO implement other anvil types
                {
                    var anvil = new Inventory(InventoryType.Anvil)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = anvil;

                    await player.OpenInventoryAsync(anvil);
                }
                else if (type >= Materials.ShulkerBox && type <= Materials.BlackShulkerBox)
                {
                    var box = new Inventory(InventoryType.ShulkerBox)//TODO shulker box functionality
                    {
                        Owner = player.Uuid,
                        Title = IChatMessage.Simple("Shulker Box"),
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Loom)
                {
                    var box = new Inventory(InventoryType.Loom)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Barrel)
                {
                    var box = new Inventory(InventoryType.Generic)
                    {
                        Owner = player.Uuid,
                        Title = IChatMessage.Simple("Barrel"),
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.CartographyTable)
                {
                    var box = new Inventory(InventoryType.CartographyTable)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Stonecutter)
                {
                    var box = new Inventory(InventoryType.Stonecutter)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Grindstone)
                {
                    var box = new Inventory(InventoryType.Grindstone)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.BrewingStand)
                {
                    var box = new Inventory(InventoryType.BrewingStand)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Lectern)
                {
                    var box = new Inventory(InventoryType.Lectern)
                    {
                        Id = maxId,
                        BlockPosition = position
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Materials.Hopper || type == Materials.HopperMinecart)
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
                player.Inventory.RemoveItem(player.CurrentSlot);

            switch (this.Face) // TODO fix this for logs
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

            server.World.SetBlock(position, block);

            await server.BroadcastBlockPlacementAsync(player, block, position);
        }
    }
}