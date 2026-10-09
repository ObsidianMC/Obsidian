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
using System.Threading;

namespace Obsidian.Entities;

public partial class Player
{
    // A player's saves run one at a time: autosaves, leaving and changing worlds can overlap, and each replaces the same
    // files. Loading takes it too, so a save never runs while the saved data is still being read.
    private readonly SemaphoreSlim saveLock = new(1, 1);

    // Whether LoadAsync finished. Until then the player holds defaults, and saving them would overwrite their real data,
    // as leaving during login or configuration would.
    private bool dataLoaded;

    /// <summary>
    /// Whether this is the singleplayer owner (an integrated server's local player), whose data vanilla also keeps in
    /// level.dat.
    /// </summary>
    internal bool IsSingleplayerOwner { get; init; }

    /// <summary>
    /// Saves the player in vanilla's shape to the world's <c>playerdata/&lt;uuid&gt;.dat</c> (and for the singleplayer
    /// owner of a vanilla-shaped world, to its level.dat's <c>Data.Player</c> too), and the world they're in to
    /// <see cref="PersistentDataFile"/>.
    /// </summary>
    public async Task SaveAsync()
    {
        await this.saveLock.WaitAsync();
        try
        {
            if (!this.dataLoaded)
                return;

            var world = this.Level is IDimension dimension ? dimension.ParentWorld : (IWorld)this.Level;

            //TODO make sure to save inventory in the right location if has using global data set to true
            await PlayerDataFile.WriteAsync(this.PersistentDataFile, new NbtCompound { new NbtTag<string>("worldName", world.Name) });

            var data = this.SaveData();
            if (this.IsSingleplayerOwner && world is World { UsesVanillaLayout: true } vanillaWorld)
                vanillaWorld.SingleplayerPlayerData = data;

            await PlayerDataFile.WriteAsync(world.GetPlayerDataPath(this.Uuid), data);
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    /// <summary>
    /// The player's saved data: the data they were loaded with, with the fields Obsidian models written over it.
    /// </summary>
    internal NbtCompound SaveData()
    {
        var tag = new NbtCompound();
        foreach (var (name, child) in this.UnmodeledData)
            tag.Add(name, child);

        this.WriteNbt(tag);
        return tag;
    }

    /// <summary>
    /// Loads the player's saved data in their world, like vanilla's <c>PlayerList.loadPlayerData</c>: the singleplayer
    /// owner's from level.dat when it has it, otherwise from <c>playerdata</c>. A player without data starts at the spawn.
    /// </summary>
    /// <param name="loadFromPersistentWorld">Whether to move the player to the world they were last saved in first.</param>
    public async Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        await this.saveLock.WaitAsync();
        try
        {
            await this.LoadDataAsync(loadFromPersistentWorld);
            this.dataLoaded = true;
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    private async Task LoadDataAsync(bool loadFromPersistentWorld)
    {
        //TODO use inventory if has using global data set to true
        if (loadFromPersistentWorld
            && PlayerDataFile.Read(this.PersistentDataFile, this.Logger) is NbtCompound persistentData
            && persistentData.TryGetTag<NbtTag<string>>("worldName", out var worldName)
            && this.Server.WorldManager.TryGetWorld<IWorld>(worldName.Value!, out var resolvedWorld))
        {
            this.Level = resolvedWorld;
        }

        await LoadPermsAsync();

        var world = this.Level is IDimension currentDimension ? currentDimension.ParentWorld : (IWorld)this.Level;
        var ownerData = this.IsSingleplayerOwner && world is World { UsesVanillaLayout: true } vanillaWorld
            ? vanillaWorld.SingleplayerPlayerData
            : null;

        var data = ownerData ?? PlayerDataFile.Read(world.GetPlayerDataPath(this.Uuid), this.Logger);
        if (loadFromPersistentWorld)
            IsFirstJoin = data is null;

        if (data is null)
        {
            // Like vanilla, a new player starts in the world's default game mode.
            this.UnmodeledData = new();
            Position = Level.LevelData.SpawnPosition;
            GameMode = Level.LevelData.DefaultGamemode;
            return;
        }

        this.UnmodeledData = data;
        this.ReadNbt(data);

        // ReadNbt moved the player into their saved dimension; one saved in a dimension the world doesn't have starts at
        // the spawn.
        if (data.TryGetTag<NbtTag<string>>("Dimension", out var dimension) && dimension.Value != this.Level.DimensionName)
        {
            Log.OtherDimension(this.Logger, this.Username, dimension.Value!);
            Position = Level.LevelData.SpawnPosition;
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

    internal ValueTask UnloadChunkAsync(int x, int z) => LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z))
        ? this.Client.QueuePacketAsync(new ForgetLevelChunkPacket(x, z))
        : default;

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

    internal async Task PickupNearbyItemsAsync()
    {
        if (!this.Alive || this.GameMode == GameMode.Spectator || this.Respawning)
            return;
        const double pickupPadding = 0.5;
        const double itemHalfWidth = 0.125;
        var halfWidth = (this.Dimension.Width > 0 ? this.Dimension.Width : 0.6) / 2;
        var height = this.Dimension.Height > 0 ? this.Dimension.Height : this.Swimming ? 0.6 : this.Sneaking ? 1.5 : 1.8;
        foreach (var entity in Level.GetNonPlayerEntitiesInRange(Position, (float)(height + 1)))
        {
            if (entity is not ItemEntity itemEntity)
                continue;
            if (Math.Abs(itemEntity.Position.X - this.Position.X) >= halfWidth + pickupPadding + itemHalfWidth ||
                Math.Abs(itemEntity.Position.Z - this.Position.Z) >= halfWidth + pickupPadding + itemHalfWidth ||
                itemEntity.Position.Y + 0.25 <= this.Position.Y - pickupPadding ||
                itemEntity.Position.Y >= this.Position.Y + height + pickupPadding)
                continue;

            bool remove;
            lock (ItemEntity.TransferLock)
            {
                if (itemEntity.Removed || !itemEntity.CanPickup || itemEntity.Item.Count <= 0)
                    continue;
                var originalCount = itemEntity.Item.Count;
                var slots = new[] { (int)this.CurrentHeldItemSlot, 45 }
                    .Concat(Enumerable.Range(36, 9)).Concat(Enumerable.Range(9, 27)).Distinct().ToArray();
                var changed = new HashSet<int>();
                // Fill matching stacks before using empty slots, preserving the item's components.
                for (var pass = 0; pass < 2 && itemEntity.Item.Count > 0; pass++)
                    foreach (var slot in slots)
                    {
                        if (pass == 1 && slot == 45)
                            continue;
                        var existing = this.Inventory.GetItem(slot);
                        var empty = existing.IsNullOrAir() || existing.Count <= 0;
                        if (pass == 0 ? empty || existing != itemEntity.Item : !empty)
                            continue;
                        var count = Math.Min(itemEntity.Item.Count,
                            itemEntity.Item.MaxStackSize - (empty ? 0 : existing.Count));
                        if (count <= 0)
                            continue;
                        if (empty)
                            this.Inventory.SetItem(slot, new ItemStack(itemEntity.Item, count));
                        else
                            existing.Count += count;
                        itemEntity.Item.Count -= count;
                        changed.Add(slot);
                    }
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

    private Dictionary<Guid, List<InfoAction>> CompilePlayerInfo(params InfoAction[] actions) => new()
    {
        { Uuid, actions.ToList() }
    };

    #region NBT
    // The player's inventory window: armor in 5 to 8, the main inventory in 9 to 35, the hotbar in 36 to 44 and the offhand
    // in 45. Vanilla's saved inventory numbers the hotbar 0 to 8 and the main inventory 9 to 35.
    private const int HotbarStart = 36;
    private const int SavedInventorySize = 36;

    // Vanilla's equipment slot names, with the window slots holding them.
    private static readonly (string Name, int Slot)[] equipmentSlots =
        [("head", 5), ("chest", 6), ("legs", 7), ("feet", 8), ("offhand", 45)];

    private static int InventoryWindowSlot(int savedSlot) => savedSlot < 9 ? HotbarStart + savedSlot : savedSlot;

    /// <summary>
    /// Writes the fields vanilla's <c>ServerPlayer</c> saves that Obsidian models, with vanilla's names and types.
    /// </summary>
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        // A mob's field, which players don't have.
        tag.Remove("PersistenceRequired");

        // Obsidian's previous names, replaced by fall_distance and OnGround.
        tag.Remove("FallDistance");
        tag.Remove("MovementFlags");

        tag.Set(new NbtTag<int>("DataVersion", VanillaLevelData.DataVersion));
        tag.Set(new NbtTag<string>("Dimension", this.Level.DimensionName));
        tag.Set(new NbtTag<int>("playerGameType", (int)this.GameMode));

        tag.Set(new NbtTag<short>("HurtTime", this.HurtTime));
        tag.Set(new NbtTag<short>("DeathTime", this.DeathTime));
        tag.Set(new NbtTag<short>("SleepTimer", this.SleepTimer));
        tag.Set(new NbtTag<double>("fall_distance", this.FallDistance));

        tag.Set(new NbtTag<int>("foodLevel", this.FoodLevel));
        tag.Set(new NbtTag<int>("foodTickTimer", this.FoodTickTimer));
        tag.Set(new NbtTag<float>("foodSaturationLevel", this.FoodSaturationLevel));
        tag.Set(new NbtTag<float>("foodExhaustionLevel", this.FoodExhaustionLevel));

        tag.Set(new NbtTag<int>("XpLevel", this.XpLevel));
        tag.Set(new NbtTag<int>("XpTotal", this.XpTotal));
        tag.Set(new NbtTag<float>("XpP", this.XpP));
        tag.Set(new NbtTag<int>("XpSeed", this.EnchantmentSeed));

        tag.Set(new NbtTag<bool>("seenCredits", this.SeenCredits));
        tag.Set(new NbtTag<int>("PortalCooldown", this.portalCooldown));

        // Vanilla counts this in the player's stats, which Obsidian doesn't save; phantoms spawn from it.
        tag.Set(new NbtTag<int>("ObsidianTimeSinceRest", this.TimeSinceRest));

        tag.Set(new NbtTag<int>("SelectedItemSlot", this.CurrentHeldItemSlot - HotbarStart));

        var inventory = new NbtList(NbtTagType.Compound, "Inventory");
        for (var slot = 0; slot < SavedInventorySize; slot++)
            AddSlot(inventory, this.Inventory.GetItem(InventoryWindowSlot(slot)), slot);

        tag.Set(inventory);

        // Like vanilla, the equipment is only saved when there's some.
        var equipment = new NbtCompound("equipment");
        foreach (var (name, slot) in equipmentSlots)
        {
            if (this.Inventory.GetItem(slot) is { IsAir: false, Count: > 0 } item)
                equipment.Add(item.ToNbt(name));
        }

        tag.SetOrRemove("equipment", equipment.Count > 0 ? equipment : null);

        var enderItems = new NbtList(NbtTagType.Compound, "EnderItems");
        for (var slot = 0; slot < this.EnderInventory.Size; slot++)
            AddSlot(enderItems, this.EnderInventory.GetItem(slot), slot);

        tag.Set(enderItems);
    }

    /// <summary>
    /// Reads the fields <see cref="WriteNbt"/> writes, keeping a new player's values for missing ones.
    /// </summary>
    /// <remarks>
    /// Obsidian's previous player files are read too: their selected slot and their inventory's slots were window slots,
    /// and their items had a byte <c>Count</c>.
    /// </remarks>
    internal override void ReadNbt(NbtCompound tag)
    {
        // Like vanilla, a player keeps their profile's UUID whatever their data says.
        var uuid = this.Uuid;
        base.ReadNbt(tag);
        this.Uuid = uuid;

        T Read<T>(string name, T fallback) => tag.TryGetTagValue<T>(name, out var value) ? value : fallback;

        var gameMode = (GameMode)Read("playerGameType", (int)this.Level.LevelData.DefaultGamemode);
        this.GameMode = Enum.IsDefined(gameMode) ? gameMode : this.Level.LevelData.DefaultGamemode;

        var dimensionName = Read<string?>("Dimension", null);
        var parentWorld = this.Level is IDimension dimension ? dimension.ParentWorld as World : this.Level as World;
        if (!string.IsNullOrWhiteSpace(dimensionName)
            && parentWorld is not null
            && parentWorld.dimensions.TryGetValue(dimensionName, out var savedDimension))
        {
            var registered = this.Level.TryRemovePlayer(this);
            this.Level = savedDimension;
            if (registered)
                this.Level.TryAddPlayer(this);
        }

        this.SeenCredits = Read("seenCredits", false);
        this.portalCooldown = Math.Max(0, Read("PortalCooldown", 0));
        this.TimeSinceRest = Math.Max(0, Read("ObsidianTimeSinceRest", 0));
        if (tag.TryGetTagValue<int>("XpSeed", out var enchantmentSeed))
            this.EnchantmentSeed = enchantmentSeed;

        this.HurtTime = Read<short>("HurtTime", 0);
        this.DeathTime = Read<short>("DeathTime", 0);
        this.SleepTimer = Read<short>("SleepTimer", 0);
        this.FallDistance = (float)Read("fall_distance", (double)Read("FallDistance", 0f));

        this.FoodLevel = Read("foodLevel", 20);
        this.FoodTickTimer = Read("foodTickTimer", 0);
        this.FoodSaturationLevel = Read("foodSaturationLevel", 5f);
        this.FoodExhaustionLevel = Read("foodExhaustionLevel", 0f);

        this.XpLevel = Read("XpLevel", 0);
        this.XpTotal = Read("XpTotal", 0);
        this.XpP = Read("XpP", 0f);

        var selectedSlot = Read("SelectedItemSlot", 0);
        if (selectedSlot >= HotbarStart)
            selectedSlot -= HotbarStart;

        this.CurrentHeldItemSlot = (short)(selectedSlot is >= 0 and < 9 ? selectedSlot : 0);

        for (var slot = 0; slot < this.Inventory.Size; slot++)
            this.Inventory.SetItem(slot, null);

        foreach (var (slot, stack, previousFormat) in ReadSlots(tag, "Inventory"))
        {
            var windowSlot = previousFormat ? slot : InventoryWindowSlot(slot);
            var inRange = previousFormat ? windowSlot < this.Inventory.Size : slot < SavedInventorySize;

            if (inRange)
                this.Inventory.SetItem(windowSlot, stack);
        }

        if (tag.TryGetTag<NbtCompound>("equipment", out var equipment))
        {
            foreach (var (name, slot) in equipmentSlots)
            {
                if (equipment.TryGetTag<NbtCompound>(name, out var item) && item.ItemFromNbt() is ItemStack stack)
                    this.Inventory.SetItem(slot, stack);
            }
        }

        for (var slot = 0; slot < this.EnderInventory.Size; slot++)
            this.EnderInventory.SetItem(slot, null);

        foreach (var (slot, stack, _) in ReadSlots(tag, "EnderItems"))
        {
            if (slot < this.EnderInventory.Size)
                this.EnderInventory.SetItem(slot, stack);
        }
    }

    // Vanilla's ItemStackWithSlot: the stack's fields and its Slot (an unsigned byte).
    private static void AddSlot(NbtList list, ItemStack? item, int slot)
    {
        if (item is not { IsAir: false, Count: > 0 })
            return;

        var compound = item.ToNbt();
        compound.Add(new NbtTag<byte>("Slot", (byte)slot));
        list.Add(compound);
    }

    /// <summary>
    /// The stacks of a saved slot list, with whether each is in Obsidian's previous format (a byte <c>Count</c>).
    /// </summary>
    private static IEnumerable<(int Slot, ItemStack Stack, bool PreviousFormat)> ReadSlots(NbtCompound tag, string name)
    {
        if (!tag.TryGetTag<NbtList>(name, out var list))
            yield break;

        foreach (var compound in list.OfType<NbtCompound>())
        {
            if (compound.ItemFromNbt() is not ItemStack stack)
                continue;

            var slot = compound.TryGetTag<NbtTag<byte>>("Slot", out var slotTag) ? slotTag.Value : 0;
            yield return (slot, stack, compound.HasTag("Count"));
        }
    }
    #endregion NBT
}
