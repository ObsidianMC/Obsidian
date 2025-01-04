namespace Obsidian.Entities;

public class AgeableMob : PathfinderMob
{
    public bool IsBaby { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(IsBaby);
    }
}
