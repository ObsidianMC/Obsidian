using Obsidian.Net;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:area_effect_cloud")]
public sealed partial class AreaEffectCloud : Entity
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

    public IParticle? Effect { get; private set; }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteEntityMetadataType(8, EntityMetadataType.Float);
        writer.WriteSingle(this.Radius);

        writer.WriteEntityMetadataType(9, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.Color);

        writer.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        writer.WriteBoolean(this.SinglePoint);

        //TODO write particle
        //this.Effect.Write(effect);
    }
}
