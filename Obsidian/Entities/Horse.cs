using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:horse")]
public partial class Horse : AbstractHorse
{
    public Horse()
    {
        Type = EntityType.Horse;
        // Vanilla Horse: other damaging and dangerous blocks are impassable.
        SetPathfindingMalus(PathType.DANGER_OTHER, -1f);
        SetPathfindingMalus(PathType.DAMAGE_OTHER, -1f);
    }
    protected override bool UsesAi => true;
    protected override bool CanBreedHorse => true;
    public int Variant { get; set; }

    protected override void FinalizeSpawn() => Variant = Random.Next(7) | Random.Next(5) << 8;

    protected override IEntity CreateOffspring(Animal mate)
    {
        var offspring = base.CreateOffspring(mate);
        if (offspring is not Horse child) return offspring;
        HorseMask |= HorseMask.HasBred;
        ((Horse)mate).HorseMask |= HorseMask.HasBred;
        child.Variant = Random.Next(2) == 0 ? Variant : ((Horse)mate).Variant;
        child.SynchronizeMetadata();
        return child;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(18, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
}
