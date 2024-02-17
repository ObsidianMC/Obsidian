namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class TreePropertyAttribute : Attribute
{
    public string ResourceLocation { get; init; }

    public TreePropertyAttribute(string resourceLocation) => this.ResourceLocation = resourceLocation;
}
