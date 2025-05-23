using Obsidian.API.Utilities;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.DamageTypes;
public sealed class DamageTypeCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public required DamageTypeElement Element { get; init; }

    public void WriteElement(INbtWriter writer)
    {
        var damageTypeElement = this.Element;

        if (damageTypeElement.DeathMessageType is DeathMessageType deathMessageType)
            writer.WriteString("death_message_type", deathMessageType.ToString().ToSnakeCase());
        if (damageTypeElement.Effects is DamageEffects damageEffects)
            writer.WriteString("effects", damageEffects.ToString().ToSnakeCase());

        writer.WriteFloat("exhaustion", damageTypeElement.Exhaustion);
        writer.WriteString("message_id", damageTypeElement.MessageId);
        writer.WriteString("scaling", damageTypeElement.Scaling.ToString().ToSnakeCase());
    }
}

