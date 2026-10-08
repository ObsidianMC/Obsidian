using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

public abstract class Fish : PathfinderMob
{
    public bool FromBucket { get; set; }
    protected abstract Material BucketItem { get; }
    protected abstract Material FishItem { get; }
    protected override bool UsesAi => true;
    internal override bool SwimmingNavigation => true;
    protected override bool CanDespawn => !FromBucket;
    protected override bool TakesFallDamage => false;
    protected override int GetExperienceReward() => Random.Next(1, 4);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new PanicGoal(this, 1.25f));
        actions.AddGoal(2, new AvoidEntityGoal(this, target => target is IPlayer player &&
            player.GameMode is not GameMode.Creative and not GameMode.Spectator, 8, 1.6f));
        actions.AddGoal(4, new FishSwimGoal(this));
    }

    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);
    protected override ValueTask TickMobAsync()
    {
        if (!MobBitMask.HasFlag(MobBitmask.NoAi) && !InWater && MovementFlags.HasFlag(MovementFlags.OnGround))
        {
            Motion += new VectorD((Random.NextSingle() - 0.5f) * 0.1f, 0.4f, (Random.NextSingle() - 0.5f) * 0.1f);
            Yaw = Random.NextSingle() * 360;
            PlayMobSound("flop");
        }
        return default;
    }

    protected override async ValueTask TickAirSupplyAsync()
    {
        if (InWater) Air = 300;
        else if (--Air <= -20) { Air = 0; await DamageEnvironmentAsync(2); }
    }

    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        await TryCaptureInBucketAsync(player, hand, BucketItem);
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Burning ? FishItem switch { Material.Cod => Material.CookedCod, Material.Salmon => Material.CookedSalmon, _ => FishItem } : FishItem);
        if (Random.NextSingle() < 0.05f) DropItem(Material.BoneMeal);
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(FromBucket);
    }
    protected override void WriteAdditionalSave(INbtWriter writer) => writer.WriteBool("FromBucket", FromBucket);
    protected override void ReadAdditionalSave(NbtCompound tag) => FromBucket = tag.TryGetBool("FromBucket", out var bucket) && bucket;
}

public abstract class SchoolingFish : Fish
{
    internal SchoolingFish? Leader { get; private set; }
    private readonly HashSet<SchoolingFish> followers = [];
    internal virtual int MaximumSchoolSize => 8;
    internal bool HasFollowers => followers.Count > 0;
    internal virtual bool CanJoinSchool(SchoolingFish leader) => leader.Type == Type;

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(5, new FollowSchoolGoal(this));
    }

    internal void LeaveSchool()
    {
        Leader?.followers.Remove(this);
        Leader = null;
    }

    internal bool TryJoinSchool()
    {
        if (Leader != null || HasFollowers) return Leader != null;
        var leader = GetEntitiesNear(8).OfType<SchoolingFish>().Where(fish => fish.Alive && !fish.IsRemoved && fish.InWater &&
            fish.Leader == null && fish.EntityId < EntityId && CanJoinSchool(fish) && fish.followers.Count < fish.MaximumSchoolSize - 1)
            .MinBy(fish => (fish.Position - Position).MagnitudeSquared());
        if (leader == null) return false;
        Leader = leader;
        leader.followers.Add(this);
        return true;
    }

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (Leader != null && (!Leader.Alive || Leader.IsRemoved || Leader.Level != Level || !IsInRange(Leader, 11))) LeaveSchool();
        followers.RemoveWhere(fish => fish.Leader != this || !fish.Alive || fish.IsRemoved || fish.Level != Level);
    }
    public override async ValueTask RemoveAsync()
    {
        LeaveSchool();
        foreach (var fish in followers.ToArray()) fish.LeaveSchool();
        await base.RemoveAsync();
    }
}

internal sealed class FishSwimGoal(Fish fish) : NavigationGoal(fish, 1)
{
    private VectorD destination;
    public override bool CanUse()
    {
        if (!fish.InWater || fish is SchoolingFish { Leader: not null } || fish.Random.Next(40) != 0) return false;
        if (RandomPosition.Find(fish, 10) is not VectorD point) return false;
        destination = point;
        return true;
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => fish.InWater && Navigation.IsNavigating && fish is not SchoolingFish { Leader: not null };
}

internal sealed class FollowSchoolGoal(SchoolingFish fish) : NavigationGoal(fish, 1)
{
    private long nextSearch;
    private long nextPath;
    public override bool CanUse()
    {
        if (!fish.InWater || fish.HasFollowers) return false;
        if (fish.Leader != null) return true;
        if (fish.AiTick < nextSearch) return false;
        nextSearch = fish.AiTick + 200 + fish.Random.Next(20);
        return fish.TryJoinSchool();
    }
    public override bool CanContinue() => fish.InWater && fish.Leader is { Alive: true, IsRemoved: false } leader && fish.IsInRange(leader, 11);
    public override void Start() => nextPath = 0;
    public override ValueTask TickAsync()
    {
        if (fish.Leader is { } leader && fish.AiTick >= nextPath)
        {
            nextPath = fish.AiTick + 10;
            MoveTo(leader);
        }
        return default;
    }
    public override void Stop() { base.Stop(); fish.LeaveSchool(); }
}

[MinecraftEntity("minecraft:cod")]
public sealed partial class Cod : SchoolingFish
{
    public Cod() => Type = EntityType.Cod;
    protected override string? SoundName => "cod";
    protected override Material BucketItem => Material.CodBucket;
    protected override Material FishItem => Material.Cod;
    internal override int MaximumSchoolSize => 9;
}

[MinecraftEntity("minecraft:salmon")]
public sealed partial class Salmon : SchoolingFish
{
    private static readonly string[] sizeNames = ["small", "medium", "large"];
    private int size = 1;
    public Salmon() => Type = EntityType.Salmon;
    public int Size { get => size; set => size = (uint)value < 3 ? value : throw new ArgumentOutOfRangeException(nameof(value)); }
    protected override float DimensionScale => Size switch { 0 => 0.5f, 2 => 1.5f, _ => 1 };
    protected override string? SoundName => "salmon";
    protected override Material BucketItem => Material.SalmonBucket;
    protected override Material FishItem => Material.Salmon;
    internal override int MaximumSchoolSize => 5;
    protected override void FinalizeSpawn()
    {
        var choice = Random.Next(95);
        Size = choice < 30 ? 0 : choice < 80 ? 1 : 2;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(Size);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        base.WriteAdditionalSave(writer);
        writer.WriteString("type", sizeNames[Size]);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        base.ReadAdditionalSave(tag);
        var index = tag.TryGetTagValue<string>("type", out var name) ? Array.IndexOf(sizeNames, name) : -1;
        Size = index >= 0 ? index : 1;
    }
}

[MinecraftEntity("minecraft:tropical_fish")]
public sealed partial class TropicalFish : SchoolingFish
{
    // Vanilla packs the shape, pattern, base color, and pattern color into successive bytes.
    private static readonly int[] commonVariants = [117506305, 117899265, 185008129, 117441793, 118161664, 65536,
        50726144, 67764993, 234882305, 67110144, 117441025, 16778497, 101253888, 50660352, 918529, 235340288,
        918273, 67108865, 917504, 459008, 67699456, 67371009];
    public TropicalFish() => Type = EntityType.TropicalFish;
    public int Variant { get; set; }
    protected override string? SoundName => "tropical_fish";
    protected override Material BucketItem => Material.TropicalFishBucket;
    protected override Material FishItem => Material.TropicalFish;
    internal override bool CanJoinSchool(SchoolingFish leader) => leader is TropicalFish other && other.Variant == Variant;
    protected override void FinalizeSpawn()
    {
        var school = GetEntitiesNear(8).OfType<TropicalFish>().FirstOrDefault(fish => fish.Alive && !fish.IsRemoved);
        Variant = school?.Variant ?? (Random.NextSingle() < 0.9f ? commonVariants[Random.Next(commonVariants.Length)] :
            Random.Next(2) | Random.Next(6) << 8 | Random.Next(16) << 16 | Random.Next(16) << 24);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        base.WriteAdditionalSave(writer);
        writer.WriteInt("Variant", Variant);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        base.ReadAdditionalSave(tag);
        if (tag.TryGetTagValue<int>("Variant", out var variant))
            Variant = (variant & 1) | Math.Clamp((variant >> 8) & 255, 0, 5) << 8 | (variant & 0x0f0f0000);
    }
}

[MinecraftEntity("minecraft:pufferfish")]
public sealed partial class Pufferfish : Fish
{
    private readonly Dictionary<int, long> nextSting = [];
    private int inflateTicks;
    private int deflateTicks;
    public int PuffState { get; private set; }
    public Pufferfish() => Type = EntityType.Pufferfish;
    protected override float DimensionScale => PuffState switch { 0 => 0.5f, 1 => 0.7f, _ => 1 };
    protected override string? SoundName => "puffer_fish";
    protected override Material BucketItem => Material.PufferfishBucket;
    protected override Material FishItem => Material.Pufferfish;
    private bool IsThreat(IEntity target) => IsValidTarget(target) && target is Living &&
        target.Type != EntityType.Axolotl && !TagsRegistry.EntityType.Aquatic.Entries.Contains((int)target.Type);

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (AiTick % 200 == 0)
            foreach (var id in nextSting.Where(entry => entry.Value <= AiTick).Select(entry => entry.Key).ToArray())
                nextSting.Remove(id);
        var bounds = Dimension.CreateBBFromPosition(Position);
        var nearby = GetEntitiesNear(4).Where(IsThreat).ToArray();
        var threatened = nearby.Any(target => MobTerrain.Overlaps(new BoundingBox(bounds.Min - 2, bounds.Max + 2), target.Dimension.CreateBBFromPosition(target.Position)));
        if (threatened)
        {
            deflateTicks = 0;
            if (PuffState == 0) SetPuffState(1);
            else if (++inflateTicks > 40 && PuffState == 1) SetPuffState(2);
        }
        else
        {
            inflateTicks = 0;
            if (PuffState == 2 && ++deflateTicks > 60) SetPuffState(1);
            else if (PuffState == 1 && ++deflateTicks > 100) SetPuffState(0);
        }
        if (PuffState == 0) return;
        bounds = Dimension.CreateBBFromPosition(Position);
        var contact = new BoundingBox(bounds.Min - 0.3f, bounds.Max + 0.3f);
        foreach (var target in nearby.Where(target => MobTerrain.Overlaps(contact, target.Dimension.CreateBBFromPosition(target.Position))))
        {
            if (AiTick < nextSting.GetValueOrDefault(target.EntityId)) continue;
            nextSting[target.EntityId] = AiTick + 10;
            var health = target.Health;
            await target.DamageAsync(this, 1 + PuffState);
            if (target.Health < health && target is Living living)
            {
                living.AddPotionEffect((int)PotionEffect.Poison - 1, 60 * PuffState,
                    effect: EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
                PlayMobSound("sting");
            }
        }
    }
    private void SetPuffState(int state)
    {
        PlayMobSound(state > PuffState ? "blow_up" : "blow_out");
        PuffState = state;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        SynchronizeMetadata();
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(PuffState);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        base.WriteAdditionalSave(writer);
        writer.WriteInt("PuffState", PuffState);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        base.ReadAdditionalSave(tag);
        PuffState = Math.Clamp(tag.TryGetTagValue<int>("PuffState", out var state) ? state : 0, 0, 2);
    }
}
