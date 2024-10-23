﻿using Microsoft.Extensions.Logging;
using Obsidian.Nbt;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.WorldData;
using System.Buffers;
using System.IO;

namespace Obsidian.Entities;
public partial class Player
{
    public async Task SaveAsync()
    {
        var playerDataFile = new FileInfo(GetPlayerDataPath());
        var persistentDataFile = new FileInfo(PersistentDataFile);

        if (playerDataFile.Exists)
        {
            playerDataFile.CopyTo(GetPlayerDataPath(true), true);
            playerDataFile.Delete();
        }

        if (persistentDataFile.Exists)
        {
            persistentDataFile.CopyTo(PersistentDataBackupFile, true);
            persistentDataFile.Delete();
        }

        await using var persistentDataStream = persistentDataFile.Create();
        await using var persistentDataWriter = new NbtWriter(persistentDataStream, NbtCompression.GZip, "");

        persistentDataWriter.WriteString("worldName", world.ParentWorldName ?? world.Name);
        //TODO make sure to save inventory in the right location if has using global data set to true

        persistentDataWriter.EndCompound();
        await persistentDataWriter.TryFinishAsync();

        await using var playerFileStream = playerDataFile.Create();
        await using var writer = new NbtWriter(playerFileStream, NbtCompression.GZip, "");

        writer.WriteInt("DataVersion", 3337);
        writer.WriteInt("playerGameType", (int)Gamemode);
        writer.WriteInt("previousPlayerGameType", (int)Gamemode);
        writer.WriteInt("Score", 0);
        writer.WriteInt("SelectedItemSlot", inventorySlot);
        writer.WriteInt("foodLevel", FoodLevel);
        writer.WriteInt("foodTickTimer", FoodTickTimer);
        writer.WriteInt("XpLevel", XpLevel);
        writer.WriteInt("XpTotal", XpTotal);

        writer.WriteFloat("Health", Health);

        writer.WriteFloat("foodExhaustionLevel", FoodExhaustionLevel);
        writer.WriteFloat("foodSaturationLevel", FoodSaturationLevel);

        writer.WriteString("Dimension", world.DimensionName);

        writer.WriteListStart("Pos", NbtTagType.Double, 3);

        writer.WriteDouble(Position.X);
        writer.WriteDouble(Position.Y);
        writer.WriteDouble(Position.Z);

        writer.EndList();

        writer.WriteListStart("Rotation", NbtTagType.Float, 2);

        writer.WriteFloat(Yaw);
        writer.WriteFloat(Pitch);

        writer.EndList();

        WriteItems(writer);
        WriteItems(writer, false);

        writer.EndCompound();

        await writer.TryFinishAsync();
    }

    public async Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        // Read persistent data first
        var persistentDataFile = new FileInfo(PersistentDataFile);

        if (persistentDataFile.Exists)
        {
            await using var persistentDataStream = persistentDataFile.OpenRead();

            var persistentDataReader = new NbtReader(persistentDataStream, NbtCompression.GZip);

            //TODO use inventory if has using global data set to true
            if (persistentDataReader.ReadNextTag() is NbtCompound persistentDataCompound)
            {
                var worldName = persistentDataCompound.GetString("worldName")!;

                Logger.LogInformation("persistent world: {worldName}", worldName);

                if (loadFromPersistentWorld && this.world.WorldManager.TryGetWorld<World>(worldName, out var world))
                {
                    base.world = world;
                    Logger.LogInformation("Loading from persistent world: {worldName}", worldName);
                }
            }
        }

        // Then read player data
        var playerDataFile = new FileInfo(GetPlayerDataPath());

        await LoadPermsAsync();

        if (!playerDataFile.Exists)
        {
            Position = world.LevelData.SpawnPosition;
            return;
        }

        await using var playerFileStream = playerDataFile.OpenRead();
        try
        {
            var reader = new NbtReader(playerFileStream, NbtCompression.GZip);

            if (reader.TryReadNextTag<NbtCompound>(out var compound))
                this.InitializePlayer(compound);
        }
        catch (Exception ex)
        {
            this.Logger.LogWarning(ex, "Player has invalid saved data.");
            Position = world.LevelData.SpawnPosition;//Set spawn here cause the data loaded was invalid
        }

        if (!Alive)
            Health = 20f;//Player should never load data that has health at 0         
    }

    public async Task LoadPermsAsync()
    {
        // Load a JSON file that contains all permissions
        var file = new FileInfo(Path.Combine(Server.PermissionPath, $"{Uuid}.json"));

        if (file.Exists)
        {
            await using var fs = file.OpenRead();
            if (await fs.FromJsonAsync<Permission>() is Permission permission)
                PlayerPermissions = permission;
        }
    }

    public async Task SavePermsAsync()
    {
        // Save permissions to JSON file
        var file = new FileInfo(Path.Combine(Server.PermissionPath, $"{Uuid}.json"));

        await using var fs = file.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);

        await PlayerPermissions.ToJsonAsync(fs);
    }

    public async ValueTask UpdatePlayerInfoAsync()
    {
        var server = client.server;

        var dict = new Dictionary<Guid, List<InfoAction>>();
        foreach (var player in server.OnlinePlayers.Values)
        {
            var addPlayerInforAction = new AddPlayerInfoAction()
            {
                Name = player.Username,
            };

            if (server.Configuration.OnlineMode)
                addPlayerInforAction.Properties.AddRange(player.SkinProperties);

            var list = new List<InfoAction>
            {
                addPlayerInforAction,
                new UpdateListedInfoAction(player.ClientInformation.AllowServerListings),
                new UpdateDisplayNameInfoAction(player.Username),
                new UpdatePingInfoAction(player.Ping)
            };

            dict.Add(player.Uuid, list);
        }

        await client.QueuePacketAsync(new PlayerInfoUpdatePacket(dict));
        await client.QueuePacketAsync(new PlayerAbilitiesPacket(true)
        {
            Abilities = client.Player!.Abilities
        });
    }

    public async ValueTask AddPlayerToListAsync(IPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player, nameof(player));

        var server = client.server;

        var addAction = new AddPlayerInfoAction
        {
            Name = player.Username,
        };

        if (server.Configuration.OnlineMode)
            addAction.Properties.AddRange(player.SkinProperties);

        var list = new List<InfoAction>()
        {
            addAction,
            new UpdatePingInfoAction(player.Ping),
            new UpdateListedInfoAction(player.ClientInformation.AllowServerListings),
        };

        await client.QueuePacketAsync(new PlayerInfoUpdatePacket(new Dictionary<Guid, List<InfoAction>>()
        {
            { player.Uuid, list }
        }));
    }

    internal async ValueTask SendInitialInfoAsync()
    {
        await client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(world.LevelData.SpawnPosition));
        await client.QueuePacketAsync(new UpdateTimePacket(world.LevelData.Time, world.LevelData.DayTime));
        await client.QueuePacketAsync(new GameEventPacket(world.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
        await client.QueuePacketAsync(new SetContainerContentPacket(0, Inventory.ToList())
        {
            StateId = Inventory.StateId++,
            CarriedItem = GetHeldItem(),
        });

        await client.QueuePacketAsync(new SetEntityMetadataPacket
        {
            EntityId = EntityId,
            Entity = this
        });
    }

    internal ValueTask UnloadChunkAsync(int x, int z) => LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z)) ? this.client.QueuePacketAsync(new UnloadChunkPacket(x, z)) : default;

    private async ValueTask TrySpawnPlayerAsync(VectorF position)
    {
        //TODO PROPER DISTANCE CALCULATION
        var entityBroadcastDistance = this.world.Configuration.EntityBroadcastRangePercentage;

        foreach (var player in world.GetPlayersInRange(position, entityBroadcastDistance))
        {
            if (player == this)
                continue;

            if (player.Alive && !visiblePlayers.Contains(player))
            {
                visiblePlayers.Add(player);

                player.SpawnEntity();
            }
        }

        if (visiblePlayers.Count == 0)
            return;

        var removed = ArrayPool<int>.Shared.Rent(visiblePlayers.Count);

        var index = 0;
        visiblePlayers.RemoveWhere(visiblePlayer =>
        {
            if (!visiblePlayer.IsInRange(this, entityBroadcastDistance))
            {
                removed[index++] = visiblePlayer.EntityId;
                return true;
            }
            return false;
        });

        if (index > 0)
            await client.QueuePacketAsync(new RemoveEntitiesPacket(removed.ToArray()));

        ArrayPool<int>.Shared.Return(removed);
    }

    private async Task PickupNearbyItemsAsync(float distance = 1.5f)
    {
        foreach (var entity in world.GetNonPlayerEntitiesInRange(Position, distance))
        {
            if (entity is not ItemEntity item)
                continue;

            if (!item.CanPickup)
                continue;

            this.PacketBroadcaster.QueuePacketToWorld(this.World, new PickupItemPacket
            {
                CollectedEntityId = item.EntityId,
                CollectorEntityId = EntityId,
                PickupItemCount = item.Count
            });

            var slot = Inventory.AddItem(new ItemStack(item.Material, item.Count, item.ItemMeta));

            client.SendPacket(new SetContainerSlotPacket
            {
                Slot = (short)slot,
                WindowId = 0,
                SlotData = Inventory.GetItem(slot)!,
                StateId = Inventory.StateId++
            });

            await item.RemoveAsync();
        }
    }

    private void WriteItems(NbtWriter writer, bool inventory = true)
    {
        var items = inventory ? Inventory.Select((item, slot) => (item, slot)) : EnderInventory.Select((item, slot) => (item, slot));

        var nonNullItems = items.Where(x => x.item != null);

        writer.WriteListStart(inventory ? "Inventory" : "EnderItems", NbtTagType.Compound, nonNullItems.Count());

        foreach (var (item, slot) in nonNullItems)
        {
            writer.WriteCompoundStart();

            writer.WriteByte("Count", (byte)item.Count);
            writer.WriteByte("Slot", (byte)slot);

            writer.WriteString("id", item.AsItem().UnlocalizedName);

            writer.WriteCompoundStart("tag");

            writer.WriteInt("Damage", item.ItemMeta.Durability);
            writer.WriteBool("Unbreakable", item.ItemMeta.Unbreakable);

            //TODO: item attributes

            writer.EndCompound();
            writer.EndCompound();
        }

        if (!nonNullItems.Any())
            writer.Write(NbtTagType.End);

        writer.EndList();
    }

    private Dictionary<Guid, List<InfoAction>> CompilePlayerInfo(params InfoAction[] actions) => new()
    {
        { Uuid, actions.ToList() }
    };

    private string GetPlayerDataPath(bool isOld = false) => Path.Join(world.PlayerDataPath, isOld ? $"{Uuid}.dat.old" : $"{Uuid}.dat");

    private void InitializePlayer(NbtCompound compound)
    {
        OnGround = compound.GetBool("OnGround");
        Sleeping = compound.GetBool("Sleeping");
        Air = compound.GetShort("Air");
        AttackTime = compound.GetShort("AttackTime");
        DeathTime = compound.GetShort("DeathTime");
        Health = compound.GetFloat("Health");
        HurtTime = compound.GetShort("HurtTime");
        SleepTimer = compound.GetShort("SleepTimer");
        FoodLevel = compound.GetInt("foodLevel");
        FoodTickTimer = compound.GetInt("foodTickTimer");
        Gamemode = (Gamemode)compound.GetInt("playerGameType");
        XpLevel = compound.GetInt("XpLevel");
        XpTotal = compound.GetInt("XpTotal");
        FallDistance = compound.GetFloat("FallDistance");
        FoodExhaustionLevel = compound.GetFloat("foodExhaustionLevel");
        FoodSaturationLevel = compound.GetFloat("foodSaturationLevel");
        XpP = compound.GetInt("XpP");

        var dimensionName = compound.GetString("Dimension");
        if (!string.IsNullOrWhiteSpace(dimensionName) && CodecRegistry.TryGetDimension(dimensionName, out var codec))
        {
            //TODO load into dimension ^ ^
        }

        compound.TryGetTag("Pos", out var posTag);
        Position = (posTag as NbtList) switch
        {
        [NbtTag<double> a, NbtTag<double> b, NbtTag<double> c, ..] => new VectorF((float)a.Value, (float)b.Value, (float)c.Value),
            _ => world.LevelData.SpawnPosition
        };

        if (compound.TryGetTag("Rotation", out var rotationTag))
        {
            if (rotationTag is NbtList and [NbtTag<float> yaw, NbtTag<float> pitch, ..])
            {
                Yaw = yaw.Value;
                Pitch = pitch.Value;
            }
        }

        if (compound.TryGetTag("Inventory", out var rawTag) && rawTag is NbtList inventory)
        {
            foreach (var rawItemTag in inventory)
            {
                if (rawItemTag.Type == NbtTagType.End)
                    break;

                if (rawItemTag is not NbtCompound itemCompound)
                    continue;

                var slot = itemCompound.GetByte("Slot");

                var itemMetaBuilder = new ItemMetaBuilder()
                    .WithDurability(itemCompound.GetInt("Damage"))
                    .IsUnbreakable(itemCompound.GetBool("Unbreakable"));

                var item = ItemsRegistry.GetSingleItem(itemCompound.GetString("id"), itemMetaBuilder.Build());
                item.Count = itemCompound.GetByte("Count");
                item.Slot = slot;

                Inventory.SetItem(slot, item);
            }
        }
    }
}
