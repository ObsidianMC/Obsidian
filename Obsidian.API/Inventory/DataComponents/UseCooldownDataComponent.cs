using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class UseCooldownDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.UseCooldown;

    public override string Identifier => "minecraft:use_cooldown";

    public required float Seconds { get; set; }

    public string? CooldownGroup { get; set; }

    [SetsRequiredMembers]
    internal UseCooldownDataComponent() { }

    public override void Read(INetStreamReader reader)
    {
        this.Seconds = reader.ReadSingle();
        this.CooldownGroup = reader.ReadOptionalString();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteSingle(this.Seconds);
        writer.WriteOptional(this.CooldownGroup);
    }
}
