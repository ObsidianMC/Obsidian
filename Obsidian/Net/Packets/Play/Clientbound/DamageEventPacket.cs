namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Vanilla's ClientboundDamageEventPacket. Clients play an entity's hurt sound from it when it takes damage, so the
/// victim hears it, as well as everyone tracking it. Hand-written because the generated fields carry no serializer.
/// </summary>
public partial class DamageEventPacket
{
    public DamageEventPacket(int entityId, string damageType, int? sourceCauseEntityId, int? sourceDirectEntityId, VectorD? sourcePosition)
    {
        EntityId = entityId;
        DamageType = damageType;
        SourceCauseEntityId = sourceCauseEntityId;
        SourceDirectEntityId = sourceDirectEntityId;
        SourcePosition = sourcePosition;
    }

    public int EntityId { get; }

    /// <summary>The damage type's name, such as <c>minecraft:fall</c>.</summary>
    public string DamageType { get; }

    public int? SourceCauseEntityId { get; }

    public int? SourceDirectEntityId { get; }

    public VectorD? SourcePosition { get; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        writer.WriteRegistryId("minecraft:damage_type", CodecRegistry.DamageType.All[this.DamageType].Id);
        writer.WriteVarInt(this.SourceCauseEntityId is int cause ? cause + 1 : 0);
        writer.WriteVarInt(this.SourceDirectEntityId is int direct ? direct + 1 : 0);
        writer.WriteBoolean(this.SourcePosition is VectorD position);
        if (this.SourcePosition is VectorD present)
        {
            writer.WriteDouble(present.X);
            writer.WriteDouble(present.Y);
            writer.WriteDouble(present.Z);
        }
    }
}
