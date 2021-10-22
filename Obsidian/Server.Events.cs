using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities;
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
                    }

                    return;
                }

                var type = interactedBlock.Material;

                //TODO check if container is cached if so get that container
                if (type == Material.Chest) // TODO check if chest its next to another single chest
                {
                    var container = new Inventory
                    {
                        Owner = player.Uuid,
                        Title = "Chest",
                        BlockPosition = blockPosition,
                        Id = "chest"
                    };

                    await player.OpenInventoryAsync(container);
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

                    this.CachedWindows.TryAdd(invUuid, container);
                }
                else if (type == Material.EnderChest)
                {
                    var container = new Inventory
                    {
                        Owner = player.Uuid,
                        Title = "Ender Chest",
                        Id = type.ToString().ToSnakeCase()
                    };

                    var invUuid = Guid.NewGuid();

                    var blockMeta = new BlockMetaBuilder().WithInventoryId(invUuid).Build();

                    this.World.SetBlockMeta(blockPosition, blockMeta);

                    this.CachedWindows.TryAdd(invUuid, container);

                    await player.OpenInventoryAsync(container);
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
                    var container = new CraftingTable();

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.Furnace || type == Material.BlastFurnace || type == Material.Smoker)
                {
                    InventoryType actualType = type switch
                    {
                        Material.Furnace => InventoryType.Furnace,
                        Material.BlastFurnace => InventoryType.BlastFurnace,
                        Material.Smoker => InventoryType.Smoker,
                        _ => InventoryType.Furnace
                    };

                    var container = new SmeltingContainer(actualType, actualType.ToString().ToSnakeCase())
                    {
                        BlockPosition = blockPosition,
                        Title = actualType.ToString()
                    };

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.EnchantingTable)
                {
                    var container = new EnchantmentTable
                    {
                        BlockPosition = blockPosition
                    };

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.Anvil || type == Material.SmithingTable) // TODO implement other anvil types
                {
                    var container = new AnvilContainer(type.ToString().ToSnakeCase())
                    {
                        Title = type == Material.Anvil ? "Anvil" : "Smithing Table"
                    };

                    await player.OpenInventoryAsync(container);
                }
                else if(type == Material.Dropper || type == Material.Dispenser)
                {
                    var container = new Inventory(9)
                    {
                        Owner = player.Uuid,
                        Title = type.ToString(),
                        BlockPosition = blockPosition
                    };

                    await player.OpenInventoryAsync(container);
                }
                else if (type >= Material.ShulkerBox && type <= Material.BlackShulkerBox)
                {
                    var container = new Inventory // TODO shulker box functionality
                    {
                        Owner = player.Uuid,
                        Title = "Shulker Box",
                        BlockPosition = blockPosition
                    };

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.Loom)
                {
                    var container = new Loom();

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.Barrel)
                {
                    var container = new Inventory
                    {
                        //Owner = player.Uuid,
                        Title = "Barrel",
                        BlockPosition = blockPosition
                    };

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.CartographyTable)
                {
                    var container = new CartographyTable();

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.Stonecutter)
                {
                    var container = new Stonecutter();

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.Grindstone)
                {
                    var container = new Grindstone();

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.BrewingStand)
                {
                    var container = new BrewingStand
                    {
                        BlockPosition = blockPosition
                    };

                    await player.OpenInventoryAsync(container);
                }
                else if (type == Material.Lectern)
                {
                    //TODO open lectern??
                }
                else if (type == Material.Hopper || type == Material.HopperMinecart)
                {
                    var container = new Inventory(5)
                    {
                        BlockPosition = blockPosition
                    };

                    await player.OpenInventoryAsync(container);
                }
            }
            else
            {
                //TODO check for other
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
