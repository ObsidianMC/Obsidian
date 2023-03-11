namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MinecraftEntityAttribute : Attribute
{
    public string ResourceLocation { get; init; }

    public MinecraftEntityAttribute(string resourceLocation) => this.ResourceLocation = resourceLocation;
}
