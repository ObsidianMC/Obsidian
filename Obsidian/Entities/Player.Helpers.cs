using Microsoft.Extensions.Logging;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Buffers;
using System.IO;

namespace Obsidian.Entities;

public partial class Player
{
    public async Task SaveAsync()
    {
        var persistentDataFile = new FileInfo(PersistentDataFile);

        if (persistentDataFile.Exists)
        {
            persistentDataFile.CopyTo(PersistentDataBackupFile, true);
            persistentDataFile.Delete();
        }

        await using var persistentDataStream = persistentDataFile.Create();
        await using var persistentDataWriter = new NbtWriterStream(persistentDataStream, NbtCompression.GZip, "");

        var level = this.Level is IDimension dimension ? dimension.ParentWorld : this.Level as IWorld;
        var worldName = level.Name;

        persistentDataWriter.WriteString("worldName", worldName);
        //TODO make sure to save inventory in the right location if has using global data set to true

        persistentDataWriter.EndCompound();
        await persistentDataWriter.TryFinishAsync();

        var playerDatPath = level.GetPlayerDataPath(this.Uuid);

        var playerDataFile = new FileInfo(playerDatPath);

        if (playerDataFile.Exists)
        {
            playerDataFile.CopyTo(level.GetPlayerDataPath(this.Uuid, true), true);
            playerDataFile.Delete();
        }

        await using var playerFileStream = playerDataFile.Create();
        await using var writer = new NbtWriterStream(playerFileStream, NbtCompression.GZip, "");

        writer.WriteByte("MovementFlags", (byte)this.MovementFlags);

        writer.WriteInt("DataVersion", 3337);
        writer.WriteInt("playerGameType", (int)GameMode);
        writer.WriteInt("previousPlayerGameType", (int)GameMode);
        writer.WriteInt("Score", 0);
        writer.WriteInt("SelectedItemSlot", CurrentHeldItemSlot);
        writer.WriteInt("foodLevel", FoodLevel);
        writer.WriteInt("foodTickTimer", FoodTickTimer);
        writer.WriteInt("XpLevel", XpLevel);
        writer.WriteInt("XpTotal", XpTotal);
        writer.WriteInt("XpSeed", EnchantmentSeed);

        writer.WriteShort("Air", Air);
        writer.WriteShort("AttackTime", AttackTime);
        writer.WriteShort("DeathTime", DeathTime);
        writer.WriteShort("HurtTime", HurtTime);
        writer.WriteShort("SleepTimer", SleepTimer);
        writer.WriteBool("Sleeping", Sleeping);

        writer.WriteFloat("Health", Health);
        writer.WriteInt("ObsidianTimeSinceRest", TimeSinceRest);
        writer.WriteFloat("FallDistance", FallDistance);
        writer.WriteFloat("XpP", XpP);

        writer.WriteFloat("foodExhaustionLevel", FoodExhaustionLevel);
        writer.WriteFloat("foodSaturationLevel", FoodSaturationLevel);

        writer.WriteString("Dimension", Level.DimensionName);
        writer.WriteBool("seenCredits", this.SeenCredits);
        writer.WriteInt("PortalCooldown", this.portalCooldown);

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

                if (loadFromPersistentWorld && this.Server.WorldManager.TryGetWorld<IWorld>(worldName, out var resolvedWorld))
                {
                    this.Level = resolvedWorld;
                }
            }
        }

        var world = this.Level is IDimension dimension ? dimension.ParentWorld : this.Level as IWorld;
        // Then read player data
        var playerDataFile = new FileInfo(world.GetPlayerDataPath(this.Uuid));

        if (loadFromPersistentWorld)
            IsFirstJoin = !persistentDataFile.Exists && !playerDataFile.Exists;

        await LoadPermsAsync();

        if (!playerDataFile.Exists)
        {
            Position = Level.LevelData.SpawnPosition;
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
            Log.InvalidSavedData(this.Logger, ex, this.Username);
            Position = Level.LevelData.SpawnPosition;//Set spawn here cause the data loaded was invalid
        }

        if (!Alive)
            Health = 20f;//Player should never load data that has health at 0         
    }

    public async Task LoadPermsAsync()
    {
        // Load a JSON file that contains all permissions
        var file = new FileInfo(Path.Combine(ServerConstants.PermissionPath, $"{Uuid}.json"));

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
        var file = new FileInfo(Path.Combine(ServerConstants.PermissionPath, $"{Uuid}.json"));

        await using var fs = file.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);

        await PlayerPermissions.ToJsonAsync(fs);
    }

    public async ValueTask UpdatePlayerInfoAsync()
    {
        var server = this.Server;

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
                new UpdateListedInfoAction(player.ClientInformation.AllowsListing),
                new UpdateDisplayNameInfoAction(player.Username),
                new UpdatePingInfoAction(player.Ping)
            };

            dict.Add(player.Uuid, list);
        }

        await Client.QueuePacketAsync(new PlayerInfoUpdatePacket(dict));
        await Client.QueuePacketAsync(new PlayerAbilitiesPacket
        {
            Abilities = Client.Player!.Abilities
        });
    }

    public async ValueTask SendPlayerInfoAsync()
    {
        await Client.QueuePacketAsync(new ContainerSetContentPacket(0, Inventory.ToList())
        {
            StateId = Inventory.StateId++,
            CarriedItem = GetHeldItem(),
        });

        await Client.QueuePacketAsync(new SetEntityDataPacket
        {
            EntityId = EntityId,
            Entity = this
        });
    }

    internal ValueTask UnloadChunkAsync(int x, int z) => LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z)) ? this.Client.QueuePacketAsync(new ForgetLevelChunkPacket(x, z)) : default;

    private async ValueTask TrySpawnPlayerAsync(VectorD position)
    {
        var entityBroadcastDistance = this.Server.Configuration.EntityBroadcastRangePercentage;

        foreach (var player in Level.GetPlayersInRange(position, entityBroadcastDistance))
        {
            if (player.EntityId == this.EntityId)
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
            // A player who left or changed level isn't in this level anymore, wherever they were last.
            if (!visiblePlayer.IsInRange(this, entityBroadcastDistance) || !Level.Players.ContainsKey(visiblePlayer.Uuid))
            {
                removed[index++] = visiblePlayer.EntityId;
                return true;
            }
            return false;
        });

        if (index > 0)
            await Client.QueuePacketAsync(new RemoveEntitiesPacket(removed.AsSpan(0, index).ToArray()));

        ArrayPool<int>.Shared.Return(removed);
    }

    // Vanilla grows the player's box by 1 horizontally and 0.5 vertically to find items to pick up; an item's box is
    // 0.25 wide and tall. These decide which items a player collects, so they match vanilla.
    private const double PickupReachXZ = 1.0, PickupReachY = 0.5, ItemHalfWidth = 0.125, ItemHeight = 0.25;

    // The inventory's hotbar (container slots 36-44) then its main slots (9-35), the order vanilla fills them in.
    private static readonly int[] hotbarThenMainSlots = [.. Enumerable.Range(36, 9), .. Enumerable.Range(9, 27)];

    /// <summary>
    /// Whether an item entity at <paramref name="item"/> touches the pickup area of a player standing at
    /// <paramref name="feet"/> with a box of <paramref name="halfWidth"/> and <paramref name="height"/>.
    /// </summary>
    internal static bool IsInPickupArea(VectorD feet, double halfWidth, double height, VectorD item) =>
        Math.Abs(item.X - feet.X) < halfWidth + PickupReachXZ + ItemHalfWidth &&
        Math.Abs(item.Z - feet.Z) < halfWidth + PickupReachXZ + ItemHalfWidth &&
        item.Y + ItemHeight > feet.Y - PickupReachY &&
        item.Y < feet.Y + height + PickupReachY;

    /// <summary>
    /// Moves as much of <paramref name="item"/> into a player <paramref name="inventory"/> as fits, taking it off the
    /// stack's count, and returns the container slots that changed.
    /// </summary>
    /// <remarks>
    /// Like vanilla, it tops up matching stacks first (the held slot, the offhand, the hotbar, then the main inventory)
    /// and only then fills empty slots, hotbar first. The held slot has no priority among empty slots, so which slot a
    /// picked-up item lands in matches the client's expectations.
    /// </remarks>
    internal static List<int> AddPickedUpItem(Container inventory, int heldSlot, ItemStack item)
    {
        var changed = new List<int>();
        foreach (var slot in (int[])[heldSlot, 45, .. hotbarThenMainSlots])
        {
            if (item.Count <= 0)
                break;
            var existing = inventory.GetItem(slot);
            if (existing.IsNullOrAir() || existing.Count <= 0 || existing != item)
                continue;
            var count = Math.Min(item.Count, item.MaxStackSize - existing.Count);
            if (count <= 0)
                continue;
            existing.Count += count;
            item.Count -= count;
            changed.Add(slot);
        }

        foreach (var slot in hotbarThenMainSlots)
        {
            if (item.Count <= 0)
                break;
            var existing = inventory.GetItem(slot);
            if (!existing.IsNullOrAir() && existing.Count > 0)
                continue;
            var count = Math.Min(item.Count, item.MaxStackSize);
            inventory.SetItem(slot, new ItemStack(item, count));
            item.Count -= count;
            changed.Add(slot);
        }
        return changed;
    }

    /// <summary>
    /// Collects the item entities in the player's pickup area. The world tick calls it once a tick, like vanilla's player
    /// tick; movement doesn't, so pickup delays and timing don't depend on how often the client sends packets.
    /// </summary>
    internal async Task PickupNearbyItemsAsync()
    {
        if (!this.Alive || this.GameMode == GameMode.Spectator || this.Respawning)
            return;
        var halfWidth = (this.Dimension.Width > 0 ? this.Dimension.Width : 0.6) / 2;
        var height = this.Dimension.Height > 0 ? this.Dimension.Height : this.Swimming ? 0.6 : this.Sneaking ? 1.5 : 1.8;
        // Far enough from the feet to reach every corner of the pickup area.
        var reachXZ = halfWidth + PickupReachXZ + ItemHalfWidth;
        var searchRadius = Math.Sqrt(2 * reachXZ * reachXZ + Math.Pow(height + PickupReachY, 2));
        foreach (var entity in Level.GetNonPlayerEntitiesInRange(Position, (float)searchRadius))
        {
            if (entity is not ItemEntity itemEntity || !IsInPickupArea(this.Position, halfWidth, height, itemEntity.Position))
                continue;

            bool remove;
            lock (ItemEntity.TransferLock)
            {
                if (itemEntity.Removed || !itemEntity.CanPickup || itemEntity.Item.Count <= 0)
                    continue;
                var originalCount = itemEntity.Item.Count;
                var changed = AddPickedUpItem(this.Inventory, this.CurrentHeldItemSlot, itemEntity.Item);
                var collected = originalCount - itemEntity.Item.Count;
                if (collected == 0)
                    continue;
                var pickup = new TakeItemEntityPacket
                {
                    CollectedEntityId = itemEntity.EntityId,
                    CollectorEntityId = this.EntityId,
                    PickupItemCount = collected
                };
                var packets = new List<ClientboundPacket> { pickup };
                foreach (var slot in changed)
                {
                    var stack = this.Inventory.GetItem(slot)!;
                    packets.Add(new SetPlayerInventoryPacket
                    {
                        Slot = slot == 45 ? 40 : slot >= 36 ? slot - 36 : slot,
                        Contents = new ItemStack(stack, stack.Count)
                    });
                }
                this.Client.SendPacket(new BundledPacket(packets));
                this.PacketBroadcaster.QueuePacketToLevel(this.Level, pickup, this.EntityId);
                remove = itemEntity.Removed = itemEntity.Item.Count == 0;
                if (!remove)
                    itemEntity.SendItemUpdate();
            }
            if (remove)
                await itemEntity.RemoveAsync();
        }
    }

    private void WriteItems(NbtWriterStream writer, bool inventory = true)
    {
        var items = inventory ? Inventory.Select((item, slot) => (item, slot)) : EnderInventory.Select((item, slot) => (item, slot));

        var nonNullItems = items.Where(x => x.item != null).ToList();

        writer.WriteListStart(inventory ? "Inventory" : "EnderItems", NbtTagType.Compound, nonNullItems.Count);

        foreach (var (item, slot) in nonNullItems)
        {
            writer.WriteCompoundStart();

            writer.WriteByte("Count", (byte)item.Count);
            writer.WriteByte("Slot", (byte)slot);

            writer.WriteString("id", item.AsItem().UnlocalizedName);

            writer.WriteCompoundStart("tag");

            writer.WriteInt("Damage", item.Damage);
            writer.WriteBool("Unbreakable", item.Unbreakable);

            //TODO: item attributes

            writer.EndCompound();
            writer.EndCompound();
        }

        if (nonNullItems.Count == 0)
            writer.Write(NbtTagType.End);

        writer.EndList();
    }

    private Dictionary<Guid, List<InfoAction>> CompilePlayerInfo(params InfoAction[] actions) => new()
    {
        { Uuid, actions.ToList() }
    };

    private void InitializePlayer(NbtCompound compound)
    {
        MovementFlags = (MovementFlags)compound.GetByte("MovementFlags");
        Sleeping = compound.GetBool("Sleeping");
        Air = compound.GetShort("Air");
        AttackTime = compound.TryGetTagValue<short>("AttackTime", out var attackTime) ? attackTime : (short)0;
        DeathTime = compound.TryGetTagValue<short>("DeathTime", out var deathTime) ? deathTime : (short)0;
        Health = compound.GetFloat("Health");
        HurtTime = compound.TryGetTagValue<short>("HurtTime", out var hurtTime) ? hurtTime : (short)0;
        SleepTimer = compound.TryGetTagValue<short>("SleepTimer", out var sleepTimer) ? sleepTimer : (short)0;
        TimeSinceRest = compound.TryGetTagValue<int>("ObsidianTimeSinceRest", out var timeSinceRest) ? Math.Max(0, timeSinceRest) : 0;
        FoodLevel = compound.GetInt("foodLevel");
        FoodTickTimer = compound.GetInt("foodTickTimer");
        GameMode = (GameMode)compound.GetInt("playerGameType");
        XpLevel = compound.GetInt("XpLevel");
        XpTotal = compound.GetInt("XpTotal");
        if (compound.TryGetTagValue<int>("XpSeed", out var enchantmentSeed)) EnchantmentSeed = enchantmentSeed;
        FallDistance = compound.TryGetTagValue<float>("FallDistance", out var fallDistance) ? fallDistance : 0;
        FoodExhaustionLevel = compound.GetFloat("foodExhaustionLevel");
        FoodSaturationLevel = compound.GetFloat("foodSaturationLevel");
        XpP = compound.TryGetTagValue<float>("XpP", out var xpProgress) ? xpProgress :
            compound.TryGetTagValue<int>("XpP", out var legacyXpProgress) ? legacyXpProgress : 0;

        var dimensionName = compound.GetString("Dimension");
        var parentWorld = this.Level is IDimension dimension ? dimension.ParentWorld as World : this.Level as World;
        if (!string.IsNullOrWhiteSpace(dimensionName) && parentWorld is not null &&
            parentWorld.dimensions.TryGetValue(dimensionName, out var savedDimension))
        {
            var registered = this.Level.TryRemovePlayer(this);
            this.Level = savedDimension;
            if (registered)
                this.Level.TryAddPlayer(this);
        }
        this.SeenCredits = compound.GetBool("seenCredits");
        this.portalCooldown = compound.TryGetTagValue<int>("PortalCooldown", out var savedCooldown) ? Math.Max(0, savedCooldown) : 0;

        compound.TryGetTag("Pos", out var posTag);
        Position = (posTag as NbtList) switch
        {
            [NbtTag<double> a, NbtTag<double> b, NbtTag<double> c, ..] => new VectorD(a.Value, b.Value, c.Value),
            _ => Level.LevelData.SpawnPosition
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

                //TODO serialize components in nbt
                var slot = itemCompound.GetByte("Slot");

                var item = ItemsRegistry.GetSingleItem(itemCompound.GetString("id"));
                item.Count = itemCompound.GetByte("Count");

                Inventory.SetItem(slot, item);
            }
        }
    }
}
