namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ConfiguredFeaturePropertyAttribute(string resourceLocation) : Attribute
{
    public string ResourceLocation { get; init; } = resourceLocation;
}
