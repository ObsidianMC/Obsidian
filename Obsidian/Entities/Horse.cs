namespace Obsidian.Entities;

[MinecraftEntity("minecraft:horse")]
public partial class Horse : AbstractHorse
{
    public Horse()
    {
        Type = EntityType.Horse;
    }
    protected override bool UsesAi => true;
    protected override bool CanBreedHorse => true;
    public int Variant { get; set; }

    protected override void FinalizeSpawn() => Variant = Random.Next(7) | Random.Next(5) << 8;

    protected override IEntity CreateOffspring(Animal mate)
    {
        var offspring = base.CreateOffspring(mate);
        if (offspring is not Horse child)
            return offspring;

        var mateHorse = (Horse)mate;
        HorseMask |= HorseMask.HasBred;
        mateHorse.HorseMask |= HorseMask.HasBred;

        // Vanilla draws the coat (4/9 this parent, 4/9 the mate, 1/9 random) and then the markings
        // (2/5, 2/5, 1/5) separately; the draw order matters for seeded breeding.
        var otherVariant = mateHorse.Variant;
        var coatRoll = Random.Next(9);
        var coat = coatRoll switch
        {
            < 4 => Variant & 255,
            < 8 => otherVariant & 255,
            _ => Random.Next(7)
        };

        var markingsRoll = Random.Next(5);
        var markings = markingsRoll switch
        {
            < 2 => (Variant >> 8) & 255,
            < 4 => (otherVariant >> 8) & 255,
            _ => Random.Next(5)
        };

        child.Variant = coat | (markings << 8);
        child.SynchronizeMetadata();
        return child;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(HorseMetadataCount, EntityMetadataType.VarInt);
        writer.WriteVarInt(Variant);
    }
}
