namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// An entity's attributes, each with its base value and modifiers, like vanilla's
/// <c>ClientboundUpdateAttributesPacket</c>: clients compute the values from them.
/// </summary>
public partial class UpdateAttributesPacket
{
    internal UpdateAttributesPacket(int entityId, IReadOnlyList<AttributeSnapshot> attributes)
    {
        this.EntityId = entityId;
        this.Attributes = attributes;
    }

    public int EntityId { get; }

    internal IReadOnlyList<AttributeSnapshot> Attributes { get; }

    // Vanilla's stream codec: the entity id, then per attribute its registry id, base and modifiers (identifier,
    // amount and operation).
    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.EntityId);
        writer.WriteVarInt(this.Attributes.Count);

        foreach (var attribute in this.Attributes)
        {
            writer.WriteVarInt(attribute.ProtocolId);
            writer.WriteDouble(attribute.Base);
            writer.WriteVarInt(attribute.Modifiers.Length);

            foreach (var modifier in attribute.Modifiers)
            {
                writer.WriteString(modifier.Id);
                writer.WriteDouble(modifier.Amount);
                writer.WriteVarInt((int)modifier.Operation);
            }
        }
    }
}

/// <summary>
/// One attribute of an <see cref="UpdateAttributesPacket"/>.
/// </summary>
internal readonly record struct AttributeSnapshot(
    int ProtocolId,
    double Base,
    ImmutableArray<Entities.EntityAttributeModifier> Modifiers);
