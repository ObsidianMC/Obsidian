using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Events;

public sealed partial class MainEventHandler : MinecraftEventHandler
{
    [EventPriority(Priority = Priority.Internal)]
    public Task OnIncomingChatMessage(IncomingChatMessageEventArgs e)
    {
        if (e.IsCancelled)
            return Task.CompletedTask;

        var server = e.Server;

        //TODO add bool for sending secure chat messages
        ChatColor nameColor = e.Server.Operators.IsOperator(e.Player) ? ChatColor.BrightGreen : ChatColor.Gray;
        var message = ChatMessage.Simple(e.Player.Username, nameColor).AppendText($": {e.Message}", ChatColor.White);
        server.BroadcastMessage(message);

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

        if (e.Container is CraftingTable { Type: InventoryType.Crafting })
            await ReturnCraftingItemsAsync(player, e.Container, 3);

        //Player successfully exited container
        player.OpenedContainer = null;

        if (e.Container is not IBlockEntity blockEntity)
            return;

        var position = blockEntity.BlockPosition;
        var block = await e.Player.Level.GetBlockAsync(position);

        if (block is null)
            return;

        switch (block.Material)
        {
            case Material.Chest:
            case Material.TrappedChest:
                {
                    await player.Client.QueuePacketAsync(new BlockEventPacket
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
                    await player.Client.QueuePacketAsync(new BlockEventPacket
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
                    await player.Client.QueuePacketAsync(new BlockEventPacket
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
            if (type is Material.Chest or Material.TrappedChest) // TODO check if chest its next to another single chest
            {
                container = new Container
                {
                    Owner = player.Uuid,
                    Title = "Chest",
                    BlockPosition = blockPosition,
                    Id = "chest"
                };

                // The inventory opens below, once the chest's contents (and loot) are in place.
                await player.Client.QueuePacketAsync(new BlockEventPacket
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
                await player.Client.QueuePacketAsync(new BlockEventPacket
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

                await player.Client.QueuePacketAsync(new BlockEventPacket
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

            if (container is IBlockEntity containerTileEntity)
            {
                var tileEntity = await player.Level.GetBlockEntityAsync(blockPosition);

                if (tileEntity == null)
                {
                    tileEntity = containerTileEntity.Clone();

                    await player.Level.SetBlockEntity(blockPosition, tileEntity);
                }
                else if (tileEntity is BaseContainer tileEntityContainer)
                {
                    // The stored container itself is opened, so changes reach the block entity that's saved with the chunk.
                    container = tileEntityContainer;
                }
                else if (tileEntity is DataBlockEntity dataBlockEntity)
                {
                    // A container from world generation: fill it once, then keep it as the block entity. Players opening it
                    // before it's stored get the first one's container, so its loot isn't generated twice.
                    lock (generatedContainers)
                    {
                        if (generatedContainers.TryGetValue(dataBlockEntity, out var generated))
                        {
                            container = generated;
                        }
                        else
                        {
                            FillGeneratedContainer(dataBlockEntity, container, player);
                            generatedContainers.Add(dataBlockEntity, container);
                        }
                    }

                    await player.Level.SetBlockEntity(blockPosition, (IBlockEntity)container);
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
        var player = e.Player;
        var server = e.Server;

        var packetBroadcaster = player.Level.PacketBroadcaster;

        if (player.OpenedContainer is CraftingTable { Type: InventoryType.Crafting } table)
            await ReturnCraftingItemsAsync(player, table, 3);
        await ReturnCraftingItemsAsync(player, player.Inventory, 2);

        if (player is Player portalPlayer)
            await portalPlayer.CancelPortalTravelAsync();

        await player.SaveAsync();

        packetBroadcaster.Broadcast(new PlayerInfoRemovePacket
        {
            UUIDs = [player.Uuid]
        }, player.EntityId);

        server.BroadcastMessage(string.Format(server.Configuration.Messages.Leave, e.Player.Username));
    }

    [EventPriority(Priority = Priority.Internal)]
    public ValueTask OnPlayerJoin(PlayerJoinEventArgs e)
    {
        var joined = e.Player;
        var server = e.Server;

        var packetBroadcaster = joined.Level.PacketBroadcaster;

        joined!.Level.TryAddPlayer(joined);
        joined!.Level.TryAddEntity(joined);

        server!.BroadcastMessage(new ChatMessage
        {
            Text = string.Format(server.Configuration.Messages.Join, e.Player.Username),
            Color = HexColor.Yellow
        });

        var addAction = new AddPlayerInfoAction
        {
            Name = joined.Username,
        };

        if (server.Configuration.OnlineMode)
            addAction.Properties.AddRange(joined.SkinProperties);

        var list = new List<InfoAction>()
        {
            addAction,
            new UpdatePingInfoAction(joined.Ping),
            new UpdateListedInfoAction(joined.ClientInformation.AllowsListing),
        };

        packetBroadcaster.Broadcast(new PlayerInfoUpdatePacket(new Dictionary<Guid, List<InfoAction>>()
        {
            { joined.Uuid, list }
        }));

        return default;
    }
}
