namespace Obsidian.Entities;

[MinecraftEntity("minecraft:dolphin")]
public sealed partial class Dolphin : AgeableMob
{
    public Dolphin() => Type = EntityType.Dolphin;
    public int Moistness { get; internal set; } = 2400;
    public bool GotFish { get; internal set; }
    internal Vector? TreasurePosition { get; set; }
    protected override bool UsesAi => true;
    internal override bool SwimmingNavigation => true;
    protected override bool TakesFallDamage => false;
    protected override string? SoundName => "dolphin";
    protected override float DimensionScale => IsBaby ? 0.65f : 1;
    protected override VectorF Travel() => AI.VolumeMovement.Travel(this, InWater);
    protected override void FinalizeSpawn() => Air = 4800;
    protected override void RegisterGoals(AI.GoalSelector actions, AI.GoalSelector targets)
    {
        actions.AddGoal(0, new DolphinBreatheGoal(this));
        actions.AddGoal(1, new SeekWaterGoal(this));
        actions.AddGoal(2, new DolphinSwimGoal(this));
        actions.AddGoal(3, new AI.MeleeAttackGoal(this, 1.2f));
        actions.AddGoal(6, new AI.RandomStrollGoal(this, 1));
        actions.AddGoal(7, new AI.LookAtPlayerGoal(this, 6));
        targets.AddGoal(1, new AI.HurtByTargetGoal(this));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (InWater || Terrain.IsRainingAt((Vector)Position.Floor())) Moistness = 2400;
        else
        {
            if (--Moistness <= 0) await DamageEnvironmentAsync(1);
            if (MovementFlags.HasFlag(MovementFlags.OnGround))
                Motion += new VectorF((Random.NextSingle() - 0.5f) * 0.4f, 0.5f, (Random.NextSingle() - 0.5f) * 0.4f);
        }
        if (InWater && AiTick % 100 == 0)
        {
            var item = GetEntitiesNear(2).OfType<ItemEntity>().FirstOrDefault(entity => entity.CanPickup);
            if (item != null)
            {
                item.Motion = GetLookDirection() * 0.3f + new VectorF(0, 0.2f, 0);
                PlayMobSound("play");
            }
        }
    }
    protected override async ValueTask TickAirSupplyAsync()
    {
        if (Terrain.GetBlock((Vector)EyePosition.Floor())?.Material != Material.Water) Air = 4800;
        else if (--Air <= -20) { Air = 0; await DamageEnvironmentAsync(2); }
    }
    internal override async ValueTask InteractAsync(IPlayer player, Hand hand)
    {
        if (!Alive || player.Health <= 0 || player.Level != Level || player.Gamemode == Gamemode.Spectator || !IsInRange(player, 4)) return;
        var item = hand == Hand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (item is not { Count: > 0, Type: Material.Cod or Material.Salmon }) return;
        await ConsumeInteractionItemAsync(player, hand);
        if (IsBaby) Age += -Age / 10;
        else
        {
            GotFish = true;
            TreasurePosition = Level is Obsidian.WorldData.AbstractLevel level && level.Generator is Obsidian.WorldData.Generators.MojangGenerator generator
                ? generator.FindDolphinTreasure((Vector)Position.Floor()) : null;
            SendEntityEvent(38);
        }
        SynchronizeMetadata();
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        if (!IsBaby) DropItem(Material.Cod, Random.Next(0, 2));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(GotFish);
        writer.WriteEntityMetadataType(18, EntityMetadataType.VarInt);
        writer.WriteVarInt(Moistness);
    }
    protected override void WriteAdditionalSave(Obsidian.Nbt.Interfaces.INbtWriter writer)
    {
        writer.WriteBool("GotFish", GotFish);
        writer.WriteInt("Moistness", Moistness);
        if (TreasurePosition is Vector point) writer.WriteArray("ObsidianTreasurePos", new[] { point.X, point.Y, point.Z });
    }
    protected override void ReadAdditionalSave(Obsidian.Nbt.NbtCompound tag)
    {
        GotFish = tag.TryGetBool("GotFish", out var fish) && fish;
        Moistness = tag.TryGetTagValue<int>("Moistness", out var moistness) ? Math.Clamp(moistness, 0, 2400) : 2400;
        if (tag.TryGetTag<Obsidian.Nbt.NbtArray<int>>("ObsidianTreasurePos", out var point) && point.Count == 3)
            TreasurePosition = new Vector(point[0], point[1], point[2]);
    }
}

internal sealed class DolphinBreatheGoal(Dolphin dolphin) : AI.NavigationGoal(dolphin, 1.5f)
{
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => dolphin.InWater && dolphin.Air < 140;
    public override bool CanContinue() => dolphin.InWater && dolphin.Air < 4800;
    public override ValueTask TickAsync()
    {
        var origin = (Vector)dolphin.Position.Floor();
        for (var y = 1; y <= 8; y++)
        {
            var point = new Vector(origin.X, origin.Y + y, origin.Z);
            if (dolphin.Terrain.GetBlock(point)?.IsAir == true)
            {
                MoveTo(new VectorF(point.X + 0.5f, point.Y - 0.4f, point.Z + 0.5f));
                break;
            }
        }
        dolphin.Motion += new VectorF(0, 0.02f, 0);
        return default;
    }
}

internal sealed class DolphinSwimGoal(Dolphin dolphin) : AI.NavigationGoal(dolphin, 1.5f)
{
    private IPlayer? swimmer;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse()
    {
        if (!dolphin.InWater) return false;
        if (dolphin.GotFish && dolphin.TreasurePosition != null) return true;
        swimmer = dolphin.Level.GetPlayersInRange(dolphin.Position, 10).FirstOrDefault(player => player.Swimming &&
            player.Health > 0 && player.Gamemode != Gamemode.Spectator);
        return swimmer != null;
    }
    public override ValueTask TickAsync()
    {
        if (dolphin.GotFish && dolphin.TreasurePosition is Vector treasure)
        {
            var delta = (VectorF)treasure - dolphin.Position;
            delta.Y = 0;
            if (delta.MagnitudeSquared() < 16)
            {
                dolphin.GotFish = false;
                dolphin.SynchronizeMetadata();
            }
            else
                MoveTo(dolphin.Position + delta / delta.Magnitude * 8);
        }
        else if (swimmer != null && swimmer.Level == dolphin.Level && swimmer.Swimming)
        {
            MoveTo(swimmer.Position);
            if (swimmer is Living living) living.AddPotionEffect((int)PotionEffect.DolphinsGrace - 1, 100);
        }
        return default;
    }
}
