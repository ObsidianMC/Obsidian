using Obsidian.Net;

namespace Obsidian.Entities;

public class AreaEffectCloud : Entity
{
    public float Radius { get; private set; }

    /// <summary>
    /// Color for mob spell particle.
    /// </summary>
    public int Color { get; private set; }

    /// <summary>
    /// Ignore radius and show effect as single point, instead of area.
    /// </summary>
    public bool SinglePoint { get; private set; }

    public IParticle Effect { get; private set; }

    public override void Write(MinecraftStream stream)
    {
        stream.WriteEntityMetadataType(8, EntityMetadataType.Float);
        stream.WriteFloat(Radius);

        stream.WriteEntityMetadataType(9, EntityMetadataType.VarInt);
        stream.WriteVarInt(Color);

        stream.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        stream.WriteBoolean(SinglePoint);

        //TODO write particle
        //this.Effect.Write(effect);
    }
}
