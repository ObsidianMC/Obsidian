using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class UseCooldownDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.UseCooldown;

    public string Identifier => "minecraft:use_cooldown";

    public required float Seconds { get; set; }

    public string? CooldownGroup { get; set; }

    [SetsRequiredMembers]
    internal UseCooldownDataComponent() { }

    public void Read(INetStreamReader reader)
    {
        this.Seconds = reader.ReadSingle();
        this.CooldownGroup = reader.ReadOptionalString();
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteSingle(this.Seconds);
        writer.WriteOptional(this.CooldownGroup);
    }
}
