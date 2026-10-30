using Microsoft.Extensions.Logging;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.IO;
using System.Threading;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // A player's saves run one at a time: autosaves, leaving and changing worlds can overlap, and each replaces the same
    // files. Loading takes it too, so a save never runs while the saved data is still being read.
    private readonly SemaphoreSlim saveLock = new(1, 1);

    // Whether LoadAsync finished. Until then the player holds defaults, and saving them would overwrite their real data,
    // as leaving during login or configuration would.
    private bool dataLoaded;

    // Whose files LoadAsync read: the player's own, or the world's singleplayer owner's (see LoadDataAsync). The player
    // always saves as themself.
    private Guid loadedUuid;

    /// <summary>
    /// Whether this is the singleplayer owner (an integrated server's local player), whose data vanilla also keeps in
    /// level.dat.
    /// </summary>
    internal bool IsSingleplayerOwner { get; init; }

    /// <summary>
    /// Vanilla <c>Player.blockInteractionRange</c>: how far <paramref name="player"/> reaches blocks, the
    /// <c>minecraft:block_interaction_range</c> attribute (vanilla's base 4.5 when it isn't set), half a block farther in
    /// creative, as vanilla's creative modifier adds.
    /// </summary>
    internal static double BlockInteractionRange(IPlayer player)
    {
        const string attribute = "minecraft:block_interaction_range";
        var range = player.HasAttribute(attribute) ? player.GetAttributeValue(attribute) : 4.5;

        return player.GameMode == GameMode.Creative ? range + 0.5 : range;
    }

    /// <summary>
    /// Where <paramref name="player"/>'s eyes are, which they reach blocks from: their pose's eye height, as vanilla's
    /// pose dimensions give it, scaled like the rest of their body.
    /// </summary>
    internal static VectorD EyePosition(IPlayer player)
    {
        var eyeHeight = player.Pose switch
        {
            Pose.FallFlying or Pose.Swimming or Pose.SpinAttack => 0.4f,
            Pose.Sneaking => 1.27f,
            Pose.Sleeping => 0.2f,
            _ => 1.62f
        };
        var scale = player.HasAttribute("minecraft:scale")
            ? Math.Clamp(player.GetAttributeValue("minecraft:scale"), 0.0625f, 16f)
            : 1f;

        return player.Position + new VectorD(0, eyeHeight * scale, 0);
    }

    /// <summary>
    /// Saves the player in vanilla's shape to the world's <c>players/data/&lt;uuid&gt;.dat</c> and their progress beside
    /// it, and the world they're in to <see cref="PersistentDataFile"/>. An integrated server's local player then becomes
    /// the world's singleplayer owner.
    /// </summary>
    public virtual async Task SaveAsync() => await this.RunTickAsync(this.SavePlayerAsync, allowStopped: true);

    private async ValueTask SavePlayerAsync()
    {
        await this.saveLock.WaitAsync();
        try
        {
            if (!this.dataLoaded)
                return;

            var world = this.Level is IDimension dimension ? dimension.ParentWorld : (IWorld)this.Level;

            //TODO make sure to save inventory in the right location if has using global data set to true
            await PlayerDataFile.WriteAsync(this.PersistentDataFile, new NbtCompound { new NbtTag<string>("worldName", world.Name) });

            await PlayerDataFile.WriteAsync(world.GetPlayerDataPath(this.Uuid), this.SaveData());
            await this.SaveProgressAsync();

            // Vanilla's singleplayer game opens the world as its owner, recorded in level.dat. Only once the player's
            // files are written: until then, the owner's files are the ones with their progress.
            if (this.IsSingleplayerOwner && world is World ownedWorld)
                ownedWorld.SingleplayerOwner = this.Uuid;
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
    /// Loads the player's saved data in their world from <c>players/data</c>, like vanilla's
    /// <c>PlayerList.loadPlayerData</c>: an integrated server's local player loads the world's saved singleplayer owner's,
    /// whoever they are, and everyone else their own. A player without data starts at the spawn.
    /// </summary>
    /// <param name="loadFromPersistentWorld">Whether to move the player to the world they were last saved in first.</param>
    public virtual async Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        await this.saveLock.WaitAsync();
        try
        {
            await this.LoadDataAsync(loadFromPersistentWorld);
            this.LoadProgress();
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
        this.loadedUuid = this.IsSingleplayerOwner && world is World { SingleplayerOwner: Guid owner } ? owner : this.Uuid;
        var data = PlayerDataFile.Read(world.GetPlayerDataPath(this.loadedUuid), this.Logger);
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
        this.savedVehicle = null;

        // ReadNbt moved the player into their saved dimension; one saved in a dimension the world doesn't have starts at
        // the spawn.
        if (data.TryGetTag<NbtTag<string>>("Dimension", out var dimension) && dimension.Value != this.Level.DimensionName)
        {
            Log.OtherDimension(this.Logger, this.Username, dimension.Value!);
            Position = Level.LevelData.SpawnPosition;
        }
        else
            this.savedVehicle = this.ReadRootVehicle(data);

        if (!Alive)
            Health = 20f;//Player should never load data that has health at 0
    }

    public virtual async Task LoadPermsAsync()
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

    public virtual async Task SavePermsAsync()
    {
        // Save permissions to JSON file
        var file = new FileInfo(Path.Combine(ServerConstants.PermissionPath, $"{Uuid}.json"));

        // Truncated, so that a shorter save (after a revoke) leaves no trailing bytes of the last.
        await using var fs = file.Open(FileMode.Create, FileAccess.Write);

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

        await this.QueuePacketAsync(new PlayerInfoUpdatePacket(dict));
        await this.QueuePacketAsync(new PlayerAbilitiesPacket
        {
            Abilities = this.Abilities
        });
    }

    public async ValueTask SendPlayerInfoAsync()
    {
        await this.QueuePacketAsync(new ContainerSetContentPacket(0, Inventory.ToList())
        {
            StateId = Inventory.StateId++,
            CarriedItem = CarriedItem,
        });

        await this.QueuePacketAsync(new SetEntityDataPacket
        {
            EntityId = EntityId,
            Entity = this
        });

        // Vanilla's placeNewPlayer and respawn send the selected hotbar slot, which the client otherwise starts at 0.
        await this.QueuePacketAsync(new SetHeldSlotPacket((byte)(CurrentHeldItemSlot - HotbarStart)));
    }

    // Vanilla grows the player's box by 1 horizontally and 0.5 vertically to find items to pick up; while riding, it
    // grows the box around both the player and the vehicle by 1 horizontally only. An item's box is 0.25 wide and tall.
    // These decide which items a player collects, so they match vanilla.
    private const double PickupReachXZ = 1.0, PickupReachY = 0.5, ItemHalfWidth = 0.125, ItemHeight = 0.25;

    // The inventory's hotbar (container slots 36-44) then its main slots (9-35), the order vanilla fills them in.
    private static readonly int[] hotbarThenMainSlots = [.. Enumerable.Range(36, 9), .. Enumerable.Range(9, 27)];

    /// <summary>
    /// The area a player whose box is <paramref name="player"/> picks items up from, riding a vehicle whose box is
    /// <paramref name="vehicle"/>, or on foot when it's null.
    /// </summary>
    internal static BoundingBox GetPickupArea(BoundingBox player, BoundingBox? vehicle)
    {
        if (vehicle is not BoundingBox mount)
        {
            var reach = new VectorD(PickupReachXZ, PickupReachY, PickupReachXZ);
            return new BoundingBox(player.Min - reach, player.Max + reach);
        }

        var ridingReach = new VectorD(PickupReachXZ, 0, PickupReachXZ);
        return new BoundingBox(VectorD.Min(player.Min, mount.Min) - ridingReach, VectorD.Max(player.Max, mount.Max) + ridingReach);
    }

    /// <summary>Whether the box of an item entity at <paramref name="item"/> overlaps a pickup <paramref name="area"/>.</summary>
    internal static bool IsInPickupArea(BoundingBox area, VectorD item) =>
        item.X + ItemHalfWidth > area.Min.X && item.X - ItemHalfWidth < area.Max.X &&
        item.Z + ItemHalfWidth > area.Min.Z && item.Z - ItemHalfWidth < area.Max.Z &&
        item.Y + ItemHeight > area.Min.Y && item.Y < area.Max.Y;

    /// <summary>
    /// Moves as much of <paramref name="item"/> into a player <paramref name="inventory"/> as fits, taking it off the
    /// stack's count, like vanilla's <c>Inventory.add</c>, and returns the container slots that changed.
    /// </summary>
    /// <remarks>
    /// Like vanilla, it tops up matching stacks first (the held slot, the offhand, the hotbar, then the main inventory)
    /// and only then fills empty slots, hotbar first. The held slot has no priority among empty slots, so which slot a
    /// picked-up item lands in matches the client's expectations. Damaged stacks don't stack, so they only fill an
    /// empty slot. With <paramref name="infiniteMaterials"/> (creative mode), what doesn't fit is used up anyway, as
    /// vanilla does.
    /// </remarks>
    internal static List<int> AddToInventory(
        Container inventory,
        int heldSlot,
        ItemStack item,
        bool infiniteMaterials = false)
    {
        var changed = new List<int>();
        foreach (var slot in (int[])[heldSlot, 45, .. hotbarThenMainSlots])
        {
            if (item.Count <= 0)
                break;

            var existing = inventory.GetItem(slot);
            if (existing.IsNullOrAir() || existing.Count <= 0 || existing != item || !IsStackable(existing))
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

        if (infiniteMaterials)
            item.Count = 0;

        return changed;
    }

    // Like vanilla's isStackable: more than one fits in a slot, and it isn't a damaged item.
    private static bool IsStackable(ItemStack item) =>
        item.MaxStackSize > 1 && (item.MaxDamage <= 0 || item.Unbreakable || item.Damage <= 0);

    /// <summary>The area this player picks up items and experience orbs from, as vanilla's <c>Player.aiStep</c> computes it.</summary>
    internal static BoundingBox PickupAreaOf(IEntity player)
    {
        var halfWidth = (player.Dimension.Width > 0 ? player.Dimension.Width : 0.6) / 2;
        var height = player.Dimension.Height > 0 ? player.Dimension.Height : player.Swimming ? 0.6 : player.Sneaking ? 1.5 : 1.8;
        var feet = player.Position;
        var playerBox = new BoundingBox(new VectorD(feet.X - halfWidth, feet.Y, feet.Z - halfWidth),
            new VectorD(feet.X + halfWidth, feet.Y + height, feet.Z + halfWidth));
        var vehicleBox = player.Vehicle is Mob { IsRemoved: false } vehicle ? vehicle.Dimension.CreateBBFromPosition(vehicle.Position) : (BoundingBox?)null;
        return GetPickupArea(playerBox, vehicleBox);
    }

    /// <summary>
    /// Collects the item entities in the player's pickup area. The world tick calls it once a tick, like vanilla's player
    /// tick; movement doesn't, so pickup delays and timing don't depend on how often the client sends packets.
    /// </summary>
    internal async Task PickupNearbyItemsAsync()
    {
        if (!this.Alive || this.GameMode == GameMode.Spectator || this.Respawning)
            return;
        var area = PickupAreaOf(this);
        // The item positions (the bottom centre of an item's box) that can overlap the area, and a sphere around them.
        var itemMin = area.Min - new VectorD(ItemHalfWidth, ItemHeight, ItemHalfWidth);
        var itemMax = area.Max + new VectorD(ItemHalfWidth, 0, ItemHalfWidth);
        var searchRadius = ((itemMax - itemMin) / 2).Magnitude;
        foreach (var entity in Level.GetNonPlayerEntitiesInRange((itemMin + itemMax) / 2, (float)searchRadius))
        {
            if (entity is not ItemEntity itemEntity || !IsInPickupArea(area, itemEntity.Position))
                continue;

            bool remove;
            TakeItemEntityPacket pickup;
            List<ClientboundPacket> slots;
            ClickWindowState state;

            // Taken like a click takes it, so that a click's check of the inventory and what it applies see the same
            // contents (see MainEventHandler.OnInventoryClick).
            using (await this.Inventory.LockAsync())
            lock (ItemEntity.TransferLock)
            {
                if (itemEntity.Removed || !itemEntity.CanPickup || itemEntity.Item.Count <= 0)
                    continue;
                var originalCount = itemEntity.Item.Count;
                var changed = AddToInventory(this.Inventory, this.CurrentHeldItemSlot, itemEntity.Item,
                    this.GameMode == GameMode.Creative);
                var collected = originalCount - itemEntity.Item.Count;
                if (collected == 0)
                    continue;

                this.Statistics.Increment(itemEntity.Item.Holder.UnlocalizedName, collected, "minecraft:picked_up");
                pickup = new TakeItemEntityPacket
                {
                    CollectedEntityId = itemEntity.EntityId,
                    CollectorEntityId = this.EntityId,
                    PickupItemCount = collected
                };
                slots = [];
                foreach (var slot in changed)
                {
                    var stack = this.Inventory.GetItem(slot)!;
                    slots.Add(new SetPlayerInventoryPacket
                    {
                        Slot = slot == 45 ? 40 : slot >= 36 ? slot - 36 : slot,
                        Contents = NetworkBuffer.SnapshotItemStack(stack)
                    });
                }

                state = ClickWindowState.Of(this);
                this.PacketBroadcaster.QueuePacketToLevel(this.Level, pickup, this.EntityId);
                remove = itemEntity.Removed = itemEntity.Item.Count == 0;
                if (!remove)
                    itemEntity.SendItemUpdate();
            }

            // In order with the window snapshots clicks send, which would otherwise overwrite it with what the
            // inventory held before; the pickup is bundled with the slots. When the inventory changed again first,
            // its slots are sent later, with the whole window, and the pickup is shown now on its own.
            await this.WindowSnapshots.SendChangeAsync(this, state, slots, lead: pickup);

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

        tag.Set(new NbtTag<int>("DataVersion", ServerConstants.DataVersion));
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
        var recipeBook = this.UnmodeledData.TryGetTag<NbtCompound>("recipeBook", out var existingBook)
            ? existingBook
            : new NbtCompound("recipeBook");
        var knownRecipes = new NbtList(NbtTagType.String, "recipes");
        foreach (var identifier in this.unlockedRecipes.Order(StringComparer.Ordinal))
            knownRecipes.Add(new NbtTag<string>("", identifier));

        recipeBook.Set(knownRecipes);
        tag.Set(recipeBook);

        tag.Set(new NbtTag<bool>("seenCredits", this.SeenCredits));
        this.WriteRespawnPoint(tag);
        tag.Set(new NbtTag<int>("PortalCooldown", this.portalCooldown));

        // Legacy field is retained for older Obsidian saves; stats now supplies the phantom timer.
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

        // Like vanilla, a player riding a vehicle saves it with them, so it comes back under them. A vehicle still
        // waiting for the join to restore it stays in the saved data, so leaving before the join doesn't drop it.
        if (this.savedVehicle is null)
            tag.SetOrRemove("RootVehicle", this.SaveRootVehicle());
    }

    // The vehicle loaded with this player, until they join and board it (see RestoreSavedVehicle).
    private Entity? savedVehicle;

    // The vehicle or mount this player is the only passenger of, saved with the seat's attachment (the player's UUID),
    // or null.
    private NbtCompound? SaveRootVehicle()
    {
        var root = this.RootVehicle;
        if (ReferenceEquals(root, this) || root.Passengers is not [var rider] || !ReferenceEquals(rider, this)
            || EntityNbt.Save(root) is not NbtCompound saved)
            return null;

        saved.Name = "Entity";
        var tag = new NbtCompound("RootVehicle") { new NbtArray<int>("Attach", EntityNbt.UuidToInts(this.Uuid)) };
        tag.Set(saved);
        return tag;
    }

    // The vehicle or mount saved with this player, when it's still in the world they're loading into.
    private Entity? ReadRootVehicle(NbtCompound data)
    {
        if (!data.TryGetTag<NbtCompound>("RootVehicle", out var root)
            || !root.TryGetTag<NbtArray<int>>("Attach", out var attach) || attach.Count != 4
            || EntityNbt.UuidFromInts(attach.GetArray()) != this.Uuid
            || !root.TryGetTag<NbtCompound>("Entity", out var entity))
            return null;

        return EntityNbt.Load(entity, this.Level);
    }

    /// <summary>
    /// Seats the player in the vehicle or mount they were riding when they left, once they've joined: it's reused when
    /// it's already in the level (a chunk saved it while they rode), otherwise it's added there, and the player boards
    /// it when it has room.
    /// </summary>
    internal void RestoreSavedVehicle()
    {
        var saved = this.savedVehicle;
        this.savedVehicle = null;
        if (saved is null || this.Level is not Obsidian.WorldData.AbstractLevel level)
            return;

        var vehicle = level.Regions.Values.SelectMany(region => region.Entities.Values)
            .OfType<Entity>()
            .FirstOrDefault(existing => existing.Uuid == saved.Uuid);
        if (vehicle is null)
        {
            var (chunkX, chunkZ) = Region.ChunkOf(saved.Position);
            level.LoadRegionByChunk(chunkX, chunkZ);
            if (!level.TrySpawnEntity(saved))
            {
                Log.VehicleNotRestored(this.Logger, this.Username);
                return;
            }

            vehicle = saved;
        }

        var seated = vehicle switch
        {
            VehicleEntity boatOrMinecart => boatOrMinecart.Board(this),
            Mob mount => mount.Mount(this),
            _ => false
        };
        if (!seated)
            Log.VehicleNotRestored(this.Logger, this.Username);
    }

    /// <summary>
    /// Reads the fields <see cref="WriteNbt"/> writes, keeping a new player's values for missing ones.
    /// </summary>
    /// <remarks>
    /// Obsidian's previous player files are read too: their selected slot and their inventory's slots were window slots,
    /// and their items had a byte <c>Count</c>.
    /// </remarks>
    // Like vanilla, a player keeps their profile's UUID whatever their data says.
    private protected override bool ReadsSavedUuid => false;

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

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

        this.SeenCredits = tag.TryGetBool("seenCredits", out var seenCredits) && seenCredits;
        this.ReadRespawnPoint(tag);
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
        this.unlockedRecipes.Clear();
        if (tag.TryGetTag<NbtCompound>("recipeBook", out var book)
            && book.TryGetTag<NbtList>("recipes", out var recipes))
        {
            foreach (var recipe in recipes.OfType<NbtTag<string>>())
            {
                if (recipe.Value is string identifier)
                    this.unlockedRecipes.Add(identifier);
            }
        }

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
