using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:copper_golem")]
public sealed partial class CopperGolem : PathfinderMob
{
    private static readonly string[] WeatherStates = ["unaffected", "exposed", "weathered", "oxidized"];
    private static readonly string[] StatueFacings = ["south", "west", "north", "east"];

    private int weather;
    private long nextWeather;
    private IBlockEntity? destination;
    private long searchAt;
    private int interactionState;

    public CopperGolem() => Type = EntityType.CopperGolem;

    protected override bool UsesAi => true;
    protected override bool CanDespawn => false;
    protected override string? SoundName => "copper_golem";
    internal override float MovementSpeed => base.MovementSpeed * (1 - weather * 0.1f);

    protected override void FinalizeSpawn()
    {
        PersistenceRequired = true;
        nextWeather = Level.LevelData.GameTime + Random.Next(504000, 552001);
    }

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new CopperSortGoal(this));
        actions.AddGoal(2, new RandomStrollGoal(this, 1));
        actions.AddGoal(3, new LookAtPlayerGoal(this, 6));
    }

    internal bool Sorting => destination != null;

    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi))
            return;

        TickWeathering();
        if (CanBecomeStatue())
        {
            await BecomeStatueAsync();
            return;
        }

        if (destination == null && AiTick >= searchAt && Level is AbstractLevel level)
            FindDestination(level);

        if (destination == null)
            return;

        var blockPosition = destination.BlockPosition;
        var target = new VectorD(blockPosition.X + 0.5, blockPosition.Y, blockPosition.Z + 0.5);
        if ((target - Position).MagnitudeSquared() > 4)
        {
            WalkToDestination(target);
            return;
        }

        var container = ReadContainer(destination);
        if (container != null)
            SortWith(container);

        destination = null;
        interactionState = 0;
        searchAt = AiTick + 40;
        SynchronizeMetadata();
    }

    private void TickWeathering()
    {
        if (nextWeather >= 0 && Level.LevelData.GameTime >= nextWeather)
        {
            nextWeather = Level.LevelData.GameTime + Random.Next(504000, 552001);
            if (weather < 3)
            {
                weather++;
                SynchronizeMetadata();
            }
        }
    }

    /// <summary>A fully oxidized, unwaxed golem standing in the open on solid ground turns into a statue.</summary>
    private bool CanBecomeStatue()
    {
        var feet = (Vector)Position.Floor();
        var below = (Vector)(Position - new VectorD(0, 0.1f, 0)).Floor();
        return weather == 3
            && nextWeather != -2
            && MovementFlags.HasFlag(MovementFlags.OnGround)
            && Terrain.GetBlock(feet)?.IsAir == true
            && Terrain.GetBlock(below) is { IsLiquid: false, IsAir: false };
    }

    private async ValueTask BecomeStatueAsync()
    {
        if (!GetEquipment(EquipmentSlot.MainHand).IsAir)
            DropItem(GetEquipment(EquipmentSlot.MainHand));

        var quarter = ((int)Math.Floor(Yaw.Degrees / 90 + 0.5) % 4 + 4) % 4;
        var facing = StatueFacings[quarter];
        var statue = BlocksRegistry.OxidizedCopperGolemStatue.WithProperty("facing", facing);
        await Level.SetBlockAsync((Vector)Position.Floor(), statue, true);
        await RemoveAsync();
    }

    /// <summary>The nearest chest within 32 blocks the golem can take from or sort its held items into.</summary>
    private void FindDestination(AbstractLevel level)
    {
        searchAt = AiTick + 100;
        var origin = (Vector)Position.Floor();
        var candidates = CollectBlockEntities(level, origin);
        var held = GetEquipment(EquipmentSlot.MainHand);
        destination = candidates
            .Where(entity => Math.Abs(entity.BlockPosition.Y - origin.Y) <= 8
                && DistanceSquaredTo(entity) <= 1024
                && Suitable(entity, held))
            .MinBy(DistanceSquaredTo);
        if (destination != null)
            interactionState = held.IsAir ? 1 : 3;

        SynchronizeMetadata();
    }

    private static List<IBlockEntity> CollectBlockEntities(AbstractLevel level, Vector origin)
    {
        var candidates = new List<IBlockEntity>();
        for (var cx = (origin.X - 32) >> 4; cx <= (origin.X + 32) >> 4; cx++)
        {
            for (var cz = (origin.Z - 32) >> 4; cz <= (origin.Z + 32) >> 4; cz++)
            {
                if (level.GetLoadedChunk(cx, cz) is { } chunk)
                    candidates.AddRange(chunk.GetBlockEntities());
            }
        }

        return candidates;
    }

    private double DistanceSquaredTo(IBlockEntity entity)
    {
        var position = new VectorD(entity.BlockPosition.X, entity.BlockPosition.Y, entity.BlockPosition.Z);
        return (position - Position).MagnitudeSquared();
    }

    private void WalkToDestination(VectorD target)
    {
        ((Navigator)Navigator!).SpeedModifier = 1;
        if (AiTick % 20 == 0)
            Navigator!.NavigateTo(target);

        if (AiTick >= searchAt + 300)
        {
            destination = null;
            interactionState = 0;
        }
    }

    /// <summary>
    /// An empty-handed golem takes up to 16 of the first stack; otherwise it puts what it holds away.
    /// </summary>
    private void SortWith(BaseContainer container)
    {
        var held = GetEquipment(EquipmentSlot.MainHand);
        if (held.IsAir)
            TakeFrom(container);
        else
            PutInto(container, held);
    }

    private void TakeFrom(BaseContainer container)
    {
        for (var i = 0; i < container.Size; i++)
        {
            if (container[i] is { Count: > 0 } item && !item.IsAir)
            {
                var amount = Math.Min(16, item.Count);
                SetEquipment(EquipmentSlot.MainHand, new ItemStack(item, amount));
                item.Count -= amount;
                if (item.Count == 0)
                    container[i] = null;

                return;
            }
        }
    }

    private void PutInto(BaseContainer container, ItemStack held)
    {
        for (var i = 0; i < container.Size && held.Count > 0; i++)
        {
            var item = container[i];
            if (item != null && !item.IsAir && item != held)
                continue;

            var amount = Math.Min(held.Count, held.MaxStackSize - (item?.Count ?? 0));
            if (amount <= 0)
                continue;

            if (item == null || item.IsAir)
                container[i] = new ItemStack(held, amount);
            else
                item.Count += amount;

            held.Count -= amount;
        }

        var remainder = held.Count == 0 ? ItemStack.Air : new ItemStack(held, held.Count);
        SetEquipment(EquipmentSlot.MainHand, remainder);
    }

    // A chest's items, unless players have it open or it's still waiting for its loot table.
    private static BaseContainer? ReadContainer(IBlockEntity entity) =>
        entity is ContainerBlockEntity { LootTable: null, Inventory.Viewers.Count: 0 } container
            ? container.Inventory
            : null;

    private bool Suitable(IBlockEntity entity, ItemStack held)
    {
        var block = Terrain.GetBlock(entity.BlockPosition);
        var copper = block?.UnlocalizedName.Contains("copper_chest", StringComparison.Ordinal) == true;
        var wrongChest = held.IsAir
            ? !copper
            : block?.RegistryId != BlockIds.Chest && block?.RegistryId != BlockIds.TrappedChest;
        if (wrongChest)
            return false;

        var container = ReadContainer(entity);
        if (container == null)
            return false;

        if (held.IsAir)
            return container.Any(item => item is { Count: > 0 } && !item.IsAir);

        var nonempty = container.Where(item => item != null && !item.IsAir && item.Count > 0).ToArray();
        var holdsSameItem = nonempty.Length == 0 || nonempty.Any(item => item == held);
        return holdsSameItem && Enumerable.Range(0, container.Size).Any(i => HasRoomFor(container[i], held));
    }

    private static bool HasRoomFor(ItemStack? slot, ItemStack held) =>
        slot == null || slot.IsAir || slot == held && slot.Count < held.MaxStackSize;
    internal override async ValueTask<bool> InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive
            || IsRemoved
            || player.Level != Level
            || player.GameMode == GameMode.Spectator
            || !IsInRange(player, 4)
            || !CanSee(player))
        {
            return false;
        }

        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (item is { Count: > 0, Holder.Id: ItemIds.Honeycomb } && nextWeather != -2)
        {
            nextWeather = -2;
            await ConsumeInteractionItemAsync(player, hand);
        }
        else if (item is { Count: > 0 }
            && item.Holder.UnlocalizedName.EndsWith("_axe", StringComparison.Ordinal)
            && (weather > 0 || nextWeather == -2))
        {
            if (nextWeather != -2)
                weather--;

            nextWeather = Level.LevelData.GameTime + Random.Next(504000, 552001);
            await DamageInteractionToolAsync(player, hand, 1);
            SynchronizeMetadata();
        }
        else
        {
            return false;
        }

        return true;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(ItemsRegistry.CopperIngot, Random.Next(1, 4));
        if (!GetEquipment(EquipmentSlot.MainHand).IsAir)
            DropItem(GetEquipment(EquipmentSlot.MainHand));

        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.WeatheringCopperState);
        writer.WriteVarInt(weather);
        writer.WriteEntityMetadataType(17, EntityMetadataType.CopperGolemState);
        writer.WriteVarInt(interactionState);
    }

    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteString("weather_state", WeatherStates[weather]);
        writer.WriteLong("next_weather_age", nextWeather);
    }

    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        weather = tag.TryGetTagValue<string>("weather_state", out var state)
            ? Math.Max(0, Array.IndexOf(WeatherStates, state))
            : 0;
        if (tag.TryGetNumber<long>("next_weather_age", out var ticks))
            nextWeather = ticks;
        else
            nextWeather = Level.LevelData.GameTime + Random.Next(504000, 552001);
    }
}

internal sealed class CopperSortGoal(CopperGolem golem) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move;

    public override bool CanUse() => golem.Sorting;
}

