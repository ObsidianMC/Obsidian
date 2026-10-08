using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:copper_golem")]
public sealed partial class CopperGolem : PathfinderMob
{
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
    protected override void FinalizeSpawn() { PersistenceRequired = true; nextWeather = Level.LevelData.Time + Random.Next(504000, 552001); }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new CopperSortGoal(this));
        actions.AddGoal(2, new RandomStrollGoal(this, 1));
        actions.AddGoal(3, new LookAtPlayerGoal(this, 6));
    }
    internal bool Sorting => destination != null;
    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (nextWeather >= 0 && Level.LevelData.Time >= nextWeather)
        {
            nextWeather = Level.LevelData.Time + Random.Next(504000, 552001);
            if (weather < 3) { weather++; SynchronizeMetadata(); }
        }
        if (weather == 3 && nextWeather != -2 && MovementFlags.HasFlag(MovementFlags.OnGround) &&
            Terrain.GetBlock((Vector)Position.Floor())?.IsAir == true &&
            Terrain.GetBlock((Vector)(Position - new VectorD(0, 0.1f, 0)).Floor()) is { IsLiquid: false, IsAir: false })
        {
            if (!GetEquipment(EquipmentSlot.MainHand).IsAir) DropItem(GetEquipment(EquipmentSlot.MainHand));
            var facing = new[] { "south", "west", "north", "east" }[((int)Math.Floor(Yaw.Degrees / 90 + 0.5) % 4 + 4) % 4];
            var statue = BlocksRegistry.Get(Material.OxidizedCopperGolemStatue).WithProperty("facing", facing);
            await Level.SetBlockAsync((Vector)Position.Floor(), statue, true);
            await RemoveAsync(); return;
        }
        if (destination == null && AiTick >= searchAt && Level is AbstractLevel level)
        {
            searchAt = AiTick + 100;
            var origin = (Vector)Position.Floor();
            var candidates = new List<IBlockEntity>();
            for (var cx = (origin.X - 32) >> 4; cx <= (origin.X + 32) >> 4; cx++)
            for (var cz = (origin.Z - 32) >> 4; cz <= (origin.Z + 32) >> 4; cz++)
                if (level.GetLoadedChunk(cx, cz) is { } chunk) candidates.AddRange(chunk.GetBlockEntities());
            var held = GetEquipment(EquipmentSlot.MainHand);
            destination = candidates.Where(entity => Math.Abs(entity.BlockPosition.Y - origin.Y) <= 8 &&
                (new VectorD(entity.BlockPosition.X, entity.BlockPosition.Y, entity.BlockPosition.Z) - Position).MagnitudeSquared() <= 1024 &&
                Suitable(entity, held)).MinBy(entity => (new VectorD(entity.BlockPosition.X, entity.BlockPosition.Y, entity.BlockPosition.Z) - Position).MagnitudeSquared());
            if (destination != null) interactionState = held.IsAir ? 1 : 3;
            SynchronizeMetadata();
        }
        if (destination == null) return;
        var target = new VectorD(destination.BlockPosition.X + 0.5, destination.BlockPosition.Y, destination.BlockPosition.Z + 0.5);
        if ((target - Position).MagnitudeSquared() > 4)
        {
            ((Navigator)Navigator!).SpeedModifier = 1;
            if (AiTick % 20 == 0) Navigator!.NavigateTo(target);
            if (AiTick >= searchAt + 300) { destination = null; interactionState = 0; }
            return;
        }
        var container = ReadContainer(destination);
        if (container != null)
        {
            var held = GetEquipment(EquipmentSlot.MainHand);
            if (held.IsAir)
            {
                for (var i = 0; i < container.Size; i++)
                    if (container[i] is { Count: > 0 } item && !item.IsAir)
                    {
                        var amount = Math.Min(16, item.Count);
                        SetEquipment(EquipmentSlot.MainHand, new ItemStack(item, amount));
                        item.Count -= amount;
                        if (item.Count == 0) container[i] = null;
                        break;
                    }
            }
            else
            {
                for (var i = 0; i < container.Size && held.Count > 0; i++)
                {
                    var item = container[i];
                    if (item != null && !item.IsAir && item != held) continue;
                    var amount = Math.Min(held.Count, held.MaxStackSize - (item?.Count ?? 0));
                    if (amount <= 0) continue;
                    if (item == null || item.IsAir) container[i] = new ItemStack(held, amount);
                    else item.Count += amount;
                    held.Count -= amount;
                }
                SetEquipment(EquipmentSlot.MainHand, held.Count == 0 ? ItemStack.Air : new ItemStack(held, held.Count));
            }
            if (destination is DataBlockEntity data)
            {
                var items = new NbtList(NbtTagType.Compound, "Items");
                for (var i = 0; i < container.Size; i++)
                    if (container[i] is { Count: > 0 } item && !item.IsAir)
                    { var saved = item.ToNbt(); saved.Set(new NbtTag<byte>("Slot", (byte)i)); items.Add(saved); }
                data.Set(items);
            }
        }
        destination = null; interactionState = 0; searchAt = AiTick + 40; SynchronizeMetadata();
    }
    private BaseContainer? ReadContainer(IBlockEntity entity)
    {
        if (entity is BaseContainer container) return container.Viewers.Count == 0 ? container : null;
        if (entity is not DataBlockEntity data || data.Data.HasTag("LootTable")) return null;
        var inventory = new Container(27); BlockEntityNbt.LoadContents(data.Data, inventory); return inventory;
    }
    private bool Suitable(IBlockEntity entity, ItemStack held)
    {
        var block = Terrain.GetBlock(entity.BlockPosition);
        var copper = block?.UnlocalizedName.Contains("copper_chest", StringComparison.Ordinal) == true;
        if (held.IsAir ? !copper : block?.Material != Material.Chest && block?.Material != Material.TrappedChest) return false;
        var container = ReadContainer(entity);
        if (container == null) return false;
        if (held.IsAir) return container.Any(item => item is { Count: > 0 } && !item.IsAir);
        var nonempty = container.Where(item => item != null && !item.IsAir && item.Count > 0).ToArray();
        return (nonempty.Length == 0 || nonempty.Any(item => item == held)) && Enumerable.Range(0, container.Size).Any(i => container[i] == null || container[i]!.IsAir || container[i] == held && container[i]!.Count < held.MaxStackSize);
    }
    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4) || !CanSee(player)) return;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (item is { Count: > 0, Type: Material.Honeycomb } && nextWeather != -2)
        { nextWeather = -2; await ConsumeInteractionItemAsync(player, hand); }
        else if (item is { Count: > 0 } && item.Holder.UnlocalizedName.EndsWith("_axe", StringComparison.Ordinal) && (weather > 0 || nextWeather == -2))
        {
            if (nextWeather != -2) weather--;
            nextWeather = Level.LevelData.Time + Random.Next(504000, 552001);
            await DamageInteractionToolAsync(player, hand, 1); SynchronizeMetadata();
        }
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    { DropItem(Material.CopperIngot, Random.Next(1, 4)); if (!GetEquipment(EquipmentSlot.MainHand).IsAir) DropItem(GetEquipment(EquipmentSlot.MainHand)); return default; }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.WeatheringCopperState); writer.WriteVarInt(weather);
        writer.WriteEntityMetadataType(17, EntityMetadataType.CopperGolemState); writer.WriteVarInt(interactionState);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    { writer.WriteString("weather_state", new[] { "unaffected", "exposed", "weathered", "oxidized" }[weather]); writer.WriteLong("next_weather_age", nextWeather); }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        weather = tag.TryGetTagValue<string>("weather_state", out var state) ? Math.Max(0, Array.IndexOf(new[] { "unaffected", "exposed", "weathered", "oxidized" }, state)) : 0;
        nextWeather = tag.TryGetTagValue<long>("next_weather_age", out var ticks) ? ticks : Level.LevelData.Time + Random.Next(504000, 552001);
    }
}
internal sealed class CopperSortGoal(CopperGolem golem) : Goal
{ public override GoalFlags Flags => GoalFlags.Move; public override bool CanUse() => golem.Sorting; }

