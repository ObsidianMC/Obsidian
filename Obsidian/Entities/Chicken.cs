using Obsidian.API.Inventory;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:chicken")]
public sealed partial class Chicken : FarmAnimal
{
    public Chicken() => Type = EntityType.Chicken;
    public int Variant { get; set; } = 1;
    public int EggLayTime { get; internal set; } = Random.Shared.Next(6000, 12000);
    public bool IsChickenJockey { get; set; }
    protected override string? SoundName => "chicken";
    protected override float PanicSpeed => 1.4f;
    protected override float TemptSpeed => 1;
    protected override bool CanEat(ItemStack? item) => item is { Count: > 0 } &&
        item.Type is Material.WheatSeeds or Material.MelonSeeds or Material.PumpkinSeeds or Material.BeetrootSeeds or Material.TorchflowerSeeds or Material.PitcherPod;
    protected override void FinalizeSpawn() => Variant = GetFarmVariant();

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (!IsBaby && !IsChickenJockey && --EggLayTime <= 0)
        {
            DropItem(Material.Egg);
            PlayMobSound("egg");
            EggLayTime = Random.Next(6000, 12000);
        }
        if (!MovementFlags.HasFlag(MovementFlags.OnGround) && Motion.Y < 0)
            Motion = new VectorF(Motion.X, Motion.Y * 0.6f, Motion.Z);
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
        if (!IsBaby)
        {
            DropItem(Material.Feather, Random.Next(3));
            DropItem(Burning ? Material.CookedChicken : Material.Chicken);
        }
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(17, EntityMetadataType.ChickenVariant);
        writer.WriteVarInt(Variant);
    }
}
