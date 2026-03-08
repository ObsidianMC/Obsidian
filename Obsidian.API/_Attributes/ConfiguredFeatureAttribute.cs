namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ConfiguredFeatureAttribute(string resourceLocation) : Attribute
{
    public string ResourceLocation { get; } = resourceLocation;
}
