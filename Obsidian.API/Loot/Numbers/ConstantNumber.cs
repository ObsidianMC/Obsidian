namespace Obsidian.API.Loot.Numbers;

[LootType("minecraft:constant")]
public sealed class ConstantNumber : INumberProvider
{
    public required float Value { get; init; }

    public float GetFloat(LootContext context) => this.Value;
}
