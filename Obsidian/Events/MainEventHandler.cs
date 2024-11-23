using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Events;
public sealed class MainEventHandler : MinecraftEventHandler
{
    [EventPriority(Priority = Priority.Internal)]
    public Task OnIncomingChatMessage(IncomingChatMessageEventArgs e)
    {
        if (e.IsCancelled)
            return Task.CompletedTask;

        var server = e.Server;

        //TODO add bool for sending secure chat messages
        ChatColor nameColor = e.Player.IsOperator ? ChatColor.BrightGreen : ChatColor.Gray;
        server.BroadcastMessage(ChatMessage.Simple(e.Player.Username, nameColor).AppendText($": {e.Message}", ChatColor.White));

        return Task.CompletedTask;
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task PlayerAttack(PlayerAttackEntityEventArgs e)
    {
        if (e.IsCancelled)
            return;

        var entity = e.Entity;
        var attacker = e.Attacker;

        if (entity is IPlayer player)
        {
            await player.DamageAsync(attacker);
        }
    }

    //TODO fix sounds
    [EventPriority(Priority = Priority.Internal)]
    public async Task OnContainerClosed(ContainerClosedEventArgs e)
    {
        if (e.IsCancelled)
            return;

        var player = (e.Player as Player)!;

        //Player successfully exited container
        player.OpenedContainer = null;

        if (e.Container is not IBlockEntity blockEntity)
            return;

        var position = blockEntity.BlockPosition;
        var block = await e.Player.World.GetBlockAsync(position);

        if (block is null)
            return;

        switch (block.Material)
        {
            case Material.Chest:
            {
                await player.client.QueuePacketAsync(new BlockEventPacket
                {
                    Position = position,
                    ActionId = 1,
                    ActionParam = 0,
                    BlockType = block.BaseId
                });

                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockChestClose)
                //    .WithSoundPosition(position.SoundPosition)
                //    .Build());

                break;
            }
            case Material.EnderChest:
            {
                await player.client.QueuePacketAsync(new BlockEventPacket
                {
                    Position = position,
                    ActionId = 1,
                    ActionParam = 0,
                    BlockType = block.BaseId
                });

                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockEnderChestClose)
                //    .WithSoundPosition(position.SoundPosition)
                //    .Build());
                break;
            }
            case Material.Barrel://Barrels don't have a block action
            {
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockBarrelClose)
                //    .WithSoundPosition(position.SoundPosition)
                //    .Build());

                break;
            }
            case Material.ShulkerBox:
            {
                await player.client.QueuePacketAsync(new BlockEventPacket
                {
                    Position = position,
                    ActionId = 1,
                    ActionParam = 0,
                    BlockType = block.BaseId
                });

                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockShulkerBoxClose)
                //    .WithSoundPosition(position.SoundPosition)
                //    .Build());

                break;
            }
        }
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnPlayerInteract(PlayerInteractEventArgs e)
    {
        if (e.IsCancelled)
            return;

        var item = e.Item;

        var block = e.Block;
        var server = e.Server as Server;
        var player = e.Player as Player;

        if (e.IsCancelled)
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
                await player.client.QueuePacketAsync(new BlockEventPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockChestOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
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
                await player.client.QueuePacketAsync(new BlockEventPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockEnderChestOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
            }
            else if (type is Material.Furnace or Material.BlastFurnace or Material.Smoker)
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

                await player.client.QueuePacketAsync(new BlockEventPacket
                {
                    Position = blockPosition,
                    ActionId = 1,
                    ActionParam = 1,
                    BlockType = block.RegistryId
                });
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockShulkerBoxOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
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
                //await player.SendSoundAsync(SoundEffectBuilder.Create(SoundId.BlockBarrelOpen, SoundCategory.Blocks)
                //    .WithSoundPosition(blockPosition.SoundPosition)
                //    .Build());
            }
            else if (type == Material.Lectern)
            {
                //TODO open lectern??
            }

            if (container is IBlockEntity)
            {
                var tileEntity = await player.world.GetBlockEntityAsync(blockPosition);

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

                    await player.world.SetBlockEntity(blockPosition, tileEntity);
                }
                else if (tileEntity is NbtCompound)
                {
                    if (tileEntity.TryGetTag("Items", out var tag))
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

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnPlayerLeave(PlayerLeaveEventArgs e)
    {
        var player = e.Player as Player;
        var server = e.Server as Server;

        await player.SaveAsync();

        player.world.TryRemovePlayer(player);

        var destroy = new RemoveEntitiesPacket(player.EntityId);

        foreach (Player other in server.Players)
        {
            if (other == player)
                continue;

            await other.client.RemovePlayerFromListAsync(player);
            if (other.visiblePlayers.Contains(player))
                await other.client.QueuePacketAsync(destroy);
        }

        server.BroadcastMessage(string.Format(server.Configuration.Messages.Leave, e.Player.Username));
    }

    [EventPriority(Priority = Priority.Internal)]
    public async Task OnPlayerJoin(PlayerJoinEventArgs e)
    {
        var joined = e.Player as Player;
        var server = e.Server as Server;

        joined!.world.TryAddPlayer(joined);
        joined!.world.TryAddEntity(joined);

        server!.BroadcastMessage(new ChatMessage
        {
            Text = string.Format(server.Configuration.Messages.Join, e.Player.Username),
            Color = HexColor.Yellow
        });

        foreach (Player other in server.Players)
        {
            await other.client.AddPlayerToListAsync(joined);
        }
    }
}
