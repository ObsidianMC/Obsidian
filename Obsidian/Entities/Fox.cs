using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:fox")]
public sealed partial class Fox : FarmAnimal
{
    private int sleepDelay = 100;
    private Guid breedingPlayer;
    public Fox() => Type = EntityType.Fox;
    public int Variant { get; internal set; }
    public bool Sleeping { get; internal set; }
    public Guid TrustedPlayer { get; internal set; }
    public Guid SecondTrustedPlayer { get; internal set; }
    protected override string? SoundName => "fox";
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0, Type: Material.SweetBerries or Material.GlowBerries };
    protected internal override float GetPathCost(IBlock feet, IBlock floor) => feet.Material == Material.SweetBerryBush ? 0 : base.GetPathCost(feet, floor);
    protected override void FinalizeSpawn()
    {
        Variant = Terrain.GetTemperature((Vector)Position.Floor()) < 0.15f ? 1 : 0;
        CanPickUpLoot = true;
        if (Random.NextSingle() < 0.2f)
        {
            ReadOnlySpan<Material> items = [Material.Emerald, Material.Egg, Material.RabbitFoot, Material.RabbitHide, Material.Wheat, Material.Leather, Material.Feather];
            SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(items[Random.Next(items.Length)]));
        }
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(1, new FoxSleepGoal(this));
        actions.AddGoal(2, new AvoidEntityGoal(this, entity => entity is IPlayer player && !player.Sneaking &&
            player.Uuid != TrustedPlayer && player.Uuid != SecondTrustedPlayer && player.Gamemode is not Gamemode.Creative and not Gamemode.Spectator ||
            entity.Type is EntityType.Wolf or EntityType.PolarBear, 8, 1.6f));
        actions.AddGoal(3, new FoxPounceGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => entity.Type is EntityType.Chicken or EntityType.Rabbit ||
            entity.Type is EntityType.Cod or EntityType.Salmon));
    }
    internal override async ValueTask FeedAsync(IPlayer player, Hand hand)
    {
        var love = LoveTicks;
        await base.FeedAsync(player, hand);
        if (LoveTicks > love) breedingPlayer = player.Uuid;
    }
    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Fox)base.CreateOffspring(mate);
        child.Variant = Random.Next(2) == 0 ? Variant : ((Fox)mate).Variant;
        child.TrustedPlayer = breedingPlayer;
        child.SecondTrustedPlayer = ((Fox)mate).breedingPlayer;
        child.PersistenceRequired = true;
        child.SynchronizeMetadata();
        return child;
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        Sleeping = false;
        sleepDelay = 200;
        SynchronizeMetadata();
        return default;
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        if (sleepDelay > 0) sleepDelay--;
        if (!Sleeping && AttackTarget == null && AiTick % 100 == 0)
        {
            var origin = (Vector)Position.Floor();
            for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
            {
                var point = new Vector(origin.X + x, origin.Y, origin.Z + z);
                if (Terrain.GetBlock(point) is { Material: Material.SweetBerryBush } berries && int.TryParse(berries.GetProperty("age"), out var age) && age >= 2)
                {
                    await Level.SetBlockAsync(point, berries.WithProperty("age", 1), true);
                    DropItem(Material.SweetBerries, Random.Next(1, 3) + (age == 3 ? 1 : 0));
                    PlayMobSound("eat");
                    return;
                }
            }
        }
    }
    internal bool CanSleep => sleepDelay == 0 && AttackTarget == null && !InWater && !Burning &&
        Level.DayTime is >= 0 and < 12000 && Terrain.GetSkyLight((Vector)EyePosition.Floor()) < 15 &&
        !GetEntitiesNear(12).Any(entity => entity is IPlayer player && player.Gamemode != Gamemode.Spectator &&
            !player.Sneaking && player.Uuid != TrustedPlayer && player.Uuid != SecondTrustedPlayer || entity.Type is EntityType.Wolf or EntityType.PolarBear);
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Byte);
        writer.WriteByte(Sleeping ? (byte)32 : (byte)0);
        writer.WriteEntityMetadataType(19, EntityMetadataType.OptionalLivingEntityReference);
        writer.WriteOptional(TrustedPlayer == Guid.Empty ? (Guid?)null : TrustedPlayer);
        writer.WriteEntityMetadataType(20, EntityMetadataType.OptionalLivingEntityReference);
        writer.WriteOptional(SecondTrustedPlayer == Guid.Empty ? (Guid?)null : SecondTrustedPlayer);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteString("Type", Variant == 1 ? "snow" : "red");
        writer.WriteBool("Sleeping", Sleeping);
        writer.WriteArray("ObsidianTrusted", EntityNbt.UuidToInts(TrustedPlayer));
        writer.WriteArray("ObsidianSecondTrusted", EntityNbt.UuidToInts(SecondTrustedPlayer));
        writer.WriteArray("ObsidianBreedingPlayer", EntityNbt.UuidToInts(breedingPlayer));
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        Variant = (tag.TryGetTagValue<string>("Type", out var savedType) ? savedType : null) == "snow" ? 1 : 0;
        Sleeping = tag.TryGetBool("Sleeping", out var sleeping) && sleeping;
        if (tag.TryGetTag<NbtArray<int>>("ObsidianTrusted", out var trusted) && trusted.Count == 4) TrustedPlayer = EntityNbt.UuidFromInts(trusted.GetArray());
        if (tag.TryGetTag<NbtArray<int>>("ObsidianSecondTrusted", out var second) && second.Count == 4) SecondTrustedPlayer = EntityNbt.UuidFromInts(second.GetArray());
        if (tag.TryGetTag<NbtArray<int>>("ObsidianBreedingPlayer", out var feeder) && feeder.Count == 4) breedingPlayer = EntityNbt.UuidFromInts(feeder.GetArray());
    }
}

internal sealed class FoxSleepGoal(Fox fox) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
    public override bool CanUse() => fox.CanSleep;
    public override void Start() { ((Navigator)fox.Navigator!).Stop(); fox.Sleeping = true; fox.SynchronizeMetadata(); }
    public override void Stop() { fox.Sleeping = false; fox.SynchronizeMetadata(); }
}

internal sealed class FoxPounceGoal(Fox fox) : MeleeAttackGoal(fox, 1.2f)
{
    public override async ValueTask TickAsync()
    {
        if (fox.AttackTarget is IEntity target && fox.MovementFlags.HasFlag(MovementFlags.OnGround) &&
            (target.Position - fox.Position).MagnitudeSquared() is > 4 and < 36 && fox.CanSee(target) && fox.Random.Next(20) == 0)
        {
            var delta = target.Position - fox.Position;
            delta.Y = 0;
            fox.Motion = delta / delta.Magnitude * 0.8f + new VectorF(0, 0.9f, 0);
        }
        await base.TickAsync();
    }
}
