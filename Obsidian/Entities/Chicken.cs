using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:chicken")]
public sealed partial class Chicken : FarmAnimal
{
    public int SoundVariant { get; set; }

    public Chicken() => Type = EntityType.Chicken;
    public int Variant { get; set; } = 1;
    public int EggLayTime { get; internal set; } = Random.Shared.Next(6000, 12000);
    public bool IsChickenJockey { get; set; }
    protected override string? SoundName => "chicken";
    protected override float PanicSpeed => 1.4f;
    protected override float TemptSpeed => 1;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0 } &&
        item.Holder.Id is ItemIds.WheatSeeds
            or ItemIds.MelonSeeds
            or ItemIds.PumpkinSeeds
            or ItemIds.BeetrootSeeds
            or ItemIds.TorchflowerSeeds
            or ItemIds.PitcherPod;
    protected override void FinalizeSpawn() => Variant = GetFarmVariant();

    // Vanilla's chicken sound variants, classic and picky, in registry order.
    private const int SoundVariants = 2;

    /// <summary>
    /// Vanilla's <c>Chicken.finalizeSpawn</c> for a chicken jockey, drawing from the level's random
    /// (<paramref name="random"/>) in vanilla's order: the variant, the sound variant, then <c>Mob</c>'s spawn draws.
    /// </summary>
    /// <remarks>
    /// Vanilla picks the variant uniformly among the highest-priority variants whose biome condition holds; exactly one
    /// chicken variant holds anywhere (cold, warm, or else temperate), so the draw is <c>nextInt(1)</c>. A jockey's
    /// group has no one in it yet, so <c>AgeableMob</c> doesn't draw for a baby.
    /// </remarks>
    internal void FinalizeJockeySpawnFromLevel(API.World.Generator.RandomSources.IRandomSource random)
    {
        random.NextInt(1);
        Variant = GetFarmVariant();
        SoundVariant = random.NextInt(SoundVariants);
        FinalizeMobSpawnFromLevel(random);
    }

    // Vanilla's egg hatch: a chick takes the variant of its biome, as a spawned chicken does.
    internal void FinalizeHatchling() => Variant = GetFarmVariant();

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (!IsBaby && !IsChickenJockey && --EggLayTime <= 0)
        {
            DropItem(ItemsRegistry.Egg);
            PlayMobSound("egg");
            EggLayTime = Random.Next(6000, 12000);
        }
        if (!MovementFlags.HasFlag(MovementFlags.OnGround) && Motion.Y < 0)
            Motion = new VectorD(Motion.X, Motion.Y * 0.6f, Motion.Z);
    }

    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Chicken)base.CreateOffspring(mate);
        child.Variant = Random.Next(2) == 0 ? Variant : ((Chicken)mate).Variant;
        child.SynchronizeMetadata();
        return child;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        this.DropVanillaLoot(source);
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(AgeableMetadataCount, EntityMetadataType.ChickenVariant);
        writer.WriteVarInt(Variant);
        writer.WriteEntityMetadataType(AgeableMetadataCount + 1, EntityMetadataType.ChickenSoundVariant);
        writer.WriteVarInt(SoundVariant);
    }
}
