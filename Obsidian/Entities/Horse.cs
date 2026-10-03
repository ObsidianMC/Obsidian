namespace Obsidian.Entities;

[MinecraftEntity("minecraft:horse")]
public partial class Horse : AbstractHorse
{
    public Horse() => Type = EntityType.Horse;
    protected override bool UsesAi => true;
    protected override bool CanBreedHorse => true;
    public int Variant { get; set; }

    protected override void FinalizeSpawn() => Variant = Random.Next(7) | Random.Next(5) << 8;

    protected override IEntity CreateOffspring(Animal mate)
    {
        var child = (Horse)base.CreateOffspring(mate);
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
