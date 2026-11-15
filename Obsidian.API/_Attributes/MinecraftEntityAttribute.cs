namespace Obsidian.API;

/// <summary>
/// Marks a class as the implementation of a vanilla entity type, which a source generator matches with the vanilla
/// entity data.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class MinecraftEntityAttribute : Attribute
{
    /// <summary>
    /// Gets the entity type's identifier, like <c>minecraft:zombie</c>.
    /// </summary>
    public string ResourceLocation { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MinecraftEntityAttribute"/> class.
    /// </summary>
    /// <param name="resourceLocation">The entity type's identifier.</param>
    public MinecraftEntityAttribute(string resourceLocation) => this.ResourceLocation = resourceLocation;
}
