using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using System;
using System.Threading.Tasks;

namespace Obsidian
{
    public partial class Server
    {
        private async Task PlayerAttack(PlayerAttackEntityEventArgs e)
        {
            var entity = e.Entity;
            var attacker = e.Attacker;

            if (entity is IPlayer player)
            {
                await player.DamageAsync(attacker);
            }
        }

        private async Task OnPlayerInteract(PlayerInteractEventArgs e)
        {
            var item = e.Item;

            var block = e.Block;

            var player = e.Player as Player;

            if (e.Cancel)
                return;

            if (block.HasValue)
            {
                var interactedBlock = block.Value;
                var blockPosition = (Vector)e.BlockLocation;

                player.LastClickedBlock = interactedBlock;

                if (LastInventoryId == byte.MaxValue)
                    LastInventoryId = 1;

                var maxId = Math.Max((byte)1, ++LastInventoryId);
                var meta = this.World.GetBlockMeta(blockPosition);

                if (meta is not null && meta.Value.InventoryId != Guid.Empty)
                {
                    if (this.CachedWindows.TryGetValue(meta.Value.InventoryId, out var inventory))
                    {
                        // Globals.PacketLogger.LogDebug($"Opened window with id of: {meta.InventoryId} {(inventory.HasItems() ? JsonConvert.SerializeObject(inventory.Items.Where(x => x != null), Formatting.Indented) : "No Items")}");

                        await player.OpenInventoryAsync(inventory);
                        await player.client.QueuePacketAsync(new BlockAction
                        {
                            Position = blockPosition,
                            ActionId = 1,
                            ActionParam = 1,
                            BlockType = interactedBlock.Id
                        });
                        await player.SendSoundAsync(Sounds.BlockChestOpen, blockPosition.SoundPosition, SoundCategory.Blocks);

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
                        BlockPosition = blockPosition
                    };

                    await player.OpenInventoryAsync(inventory);
                    await player.client.QueuePacketAsync(new BlockAction
                    {
                        Position = blockPosition,
                        ActionId = 1,
                        ActionParam = 1,
                        BlockType = interactedBlock.Id
                    });
                    await player.SendSoundAsync(Sounds.BlockChestOpen, blockPosition.SoundPosition, SoundCategory.Blocks);

                    var invUuid = Guid.NewGuid();

                    var blockMeta = new BlockMetaBuilder().WithInventoryId(invUuid).Build();

                    this.World.SetBlockMeta(blockPosition, blockMeta);

                    this.CachedWindows.TryAdd(invUuid, inventory);

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

                    this.World.SetBlockMeta(blockPosition, blockMeta);

                    this.CachedWindows.TryAdd(invUuid, enderChest);

                    player.OpenedInventory = enderChest;

                    await player.OpenInventoryAsync(enderChest);
                    await player.client.QueuePacketAsync(new BlockAction
                    {
                        Position = blockPosition,
                        ActionId = 1,
                        ActionParam = 1,
                        BlockType = interactedBlock.Id
                    });
                    await player.SendSoundAsync(Sounds.BlockEnderChestOpen, blockPosition.SoundPosition, SoundCategory.Blocks);
                }
                else if (type == Material.CraftingTable)
                {
                    var crafting = new Inventory(InventoryType.Crafting)
                    {
                        Title = ChatMessage.Simple("Crafting Table"),
                        Id = maxId,
                        BlockPosition = blockPosition
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
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = furnace;

                    await player.OpenInventoryAsync(furnace);
                }
                else if (type == Material.EnchantingTable)
                {
                    var enchantmentTable = new Inventory(InventoryType.Enchantment)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = enchantmentTable;

                    await player.OpenInventoryAsync(enchantmentTable);
                }
                else if (type == Material.Anvil || type == Material.SmithingTable) // TODO implement other anvil types
                {
                    var anvil = new Inventory(InventoryType.Anvil)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
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
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Loom)
                {
                    var box = new Inventory(InventoryType.Loom)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
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
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.CartographyTable)
                {
                    var box = new Inventory(InventoryType.CartographyTable)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Stonecutter)
                {
                    var box = new Inventory(InventoryType.Stonecutter)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Grindstone)
                {
                    var box = new Inventory(InventoryType.Grindstone)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.BrewingStand)
                {
                    var box = new Inventory(InventoryType.BrewingStand)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Lectern)
                {
                    var box = new Inventory(InventoryType.Lectern)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
                else if (type == Material.Hopper || type == Material.HopperMinecart)
                {
                    var box = new Inventory(InventoryType.Hopper)
                    {
                        Id = maxId,
                        BlockPosition = blockPosition
                    };

                    player.OpenedInventory = box;

                    await player.OpenInventoryAsync(box);
                }
            }
            else
            {

            }
        }

        private async Task OnPlayerLeave(PlayerLeaveEventArgs e)
        {
            var player = e.Player as Player;

            await player.SaveAsync();

            World.RemovePlayer(player);

            var destroy = new DestroyEntities(player.EntityId);

            foreach (Player other in Players)
            {
                if (other == player)
                    continue;

                await other.client.RemovePlayerFromListAsync(player);
                if (other.visiblePlayers.Contains(player.EntityId))
                    await other.client.QueuePacketAsync(destroy);
            }

            BroadcastMessage(string.Format(Config.LeaveMessage, e.Player.Username));
        }

        private async Task OnPlayerJoin(PlayerJoinEventArgs e)
        {
            var joined = e.Player as Player;

            World.AddPlayer(joined); // TODO Add the player to the last world they were in

            BroadcastMessage(string.Format(Config.JoinMessage, e.Player.Username));
            foreach (Player other in Players)
            {
                await other.client.AddPlayerToListAsync(joined);
            }
        }
    }
}
