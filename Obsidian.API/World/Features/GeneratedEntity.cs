using Obsidian.Nbt;

namespace Obsidian.API.World.Features;

/// <summary>
/// An entity placed by world generation (end crystals, structure mobs...), spawned once its chunk is complete.
/// </summary>
/// <param name="Type">The entity type id, e.g. <c>minecraft:end_crystal</c>.</param>
/// <param name="Position">Where the entity is spawned.</param>
/// <param name="Yaw">The entity's yaw, in degrees.</param>
/// <param name="Pitch">The entity's pitch, in degrees.</param>
public sealed record GeneratedEntity(string Type, VectorD Position, float Yaw = 0f, float Pitch = 0f)
{
    /// <summary>
    /// Vanilla's saved entity fields besides the id, position and rotation, e.g. <c>Invulnerable</c>.
    /// </summary>
    public NbtCompound Data { get; init; } = new();
}
