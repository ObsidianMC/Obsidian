using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian;

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
        var server = e.Server as Server;
        var player = e.Player as Player;

        if (e.Cancel)
            return;

        if (block is not null)
        {
            if (e.BlockLocation is not Vector blockPosition)
                return;

            player.LastClickedBlock = block;

            var type = block.Material;

            BaseContainer? container = type switch
            {
                Material.Anvil or Material.SmithingTable => new AnvilContainer(type.ToString().ToSnakeCase())
                {
                    Title = type == Material.Anvil ? "Anvil" : "Smithing Table"
                },
                Material.EnchantingTable => new EnchantmentTable
                {
                    BlockPosition = blockPosition
                },
                Material.Dropper or Material.Dispenser => new Container(9)
                {
                    Owner = player.Uuid,
                    Title = type.ToString(),
                    BlockPosition = blockPosition,
                    Id = type is Material.Dropper ? "dropper" : "dispenser"
                },
                Material.BrewingStand => new BrewingStand
                {
                    BlockPosition = blockPosition
                },
                Material.Hopper => new Container(5)
                {
                    BlockPosition = blockPosition
                },
                Material.CraftingTable => new CraftingTable(),
                Material.Loom => new Loom(),
                Material.CartographyTable => new CartographyTable(),
                Material.Stonecutter => new Stonecutter(),
                Material.Grindstone => new Grindstone(),

                _ => null
            };
            //TODO check if container is cached if so get that container
            if (type == Material.Chest) // TODO check if chest its next to another single chest
            {
                container = new Container
                {
                    Owner = player.Uuid,
                    Title = "Chest",
                    BlockPosition = blockPosition,
                    Id = "chest"
                };

                await player.OpenInventoryAsync(container);
                await player.client.QueuePacketAsync(new BlockActionPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                await player.SendSoundAsync(SoundId.BlockChestOpen, blockPosition.SoundPosition, SoundCategory.Blocks);
            }
            else if (type == Material.EnderChest)
            {
                container = new Container
                {
                    Owner = player.Uuid,
                    Title = "Ender Chest",
                    Id = type.ToString().ToSnakeCase()
                };

                await player.OpenInventoryAsync(container);
                await player.client.QueuePacketAsync(new BlockActionPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                await player.SendSoundAsync(SoundId.BlockEnderChestOpen, blockPosition.SoundPosition, SoundCategory.Blocks);
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

                container = new SmeltingContainer(actualType, actualType.ToString().ToSnakeCase())
                {
                    BlockPosition = blockPosition,
                    Title = actualType.ToString()
                };
            }
            else if (type is Material.ShulkerBox) //TODO colored shulker boxes as well
            {
                container = new Container // TODO shulker box functionality
                {
                    Owner = player.Uuid,
                    Title = "Shulker Box",
                    BlockPosition = blockPosition,
                    Id = "shulker_box"
                };

                await player.client.QueuePacketAsync(new BlockActionPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                await player.SendSoundAsync(SoundId.BlockShulkerBoxOpen, blockPosition.SoundPosition, SoundCategory.Blocks);
            }
            else if (type == Material.Barrel)
            {
                container = new Container
                {
                    //Owner = player.Uuid,
                    Title = "Barrel",
                    BlockPosition = blockPosition,
                    Id = "Barrel"
                };
                await player.SendSoundAsync(SoundId.BlockBarrelOpen, blockPosition.SoundPosition, SoundCategory.Blocks);
            }
            else if (type == Material.Lectern)
            {
                //TODO open lectern??
            }

            if (container is IBlockEntity)
            {
                var tileEntity = await player.World.GetBlockEntityAsync(blockPosition);

                if (tileEntity == null)
                {
                    tileEntity = new NbtCompound()
                    {
                        new NbtTag<string>("id", (container as IBlockEntity).Id),

                        new NbtTag<int>("x", blockPosition.X),
                        new NbtTag<int>("y", blockPosition.Y),
                        new NbtTag<int>("z", blockPosition.Z),

                        new NbtTag<string>("CustomName", container.Title.ToJson())
                    };

                    player.World.SetBlockEntity(blockPosition, tileEntity);
                }
                else if (tileEntity is NbtCompound dataCompound)
                {
                    if (dataCompound.TryGetTag("Items", out var tag))
                    {
                        var items = tag as NbtList;

                        foreach (NbtCompound i in items)
                        {
                            var inventoryItem = i.ItemFromNbt();

                            container.SetItem(inventoryItem.Slot, inventoryItem);
                        }
                    }
                }
            }

            await player.OpenInventoryAsync(container);
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

        player.World.TryRemovePlayer(player);

        var destroy = new RemoveEntitiesPacket(player.EntityId);

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

        joined.World.TryAddPlayer(joined);

        BroadcastMessage(new ChatMessage
        {
            Text = string.Format(Config.JoinMessage, e.Player.Username),
            Color = HexColor.Yellow
        });

        foreach (Player other in Players)
        {
            await other.client.AddPlayerToListAsync(joined);
        }
    }
}
