namespace Obsidian.API;
/// <summary>A sound event: the id of a sound and how far it can be heard.</summary>
public readonly record struct SoundEvent
{
    /// <summary>Gets the sound event's id, such as <c>minecraft:entity.pig.ambient</c>.</summary>
    public required string ResourceLocation { get; init; }
    /// <summary>
    /// Gets the fixed distance, in blocks, the sound can be heard from, or null to scale it with the volume.
    /// </summary>
    public float? FixedRange { get; init; }

    /// <summary>
    /// The sound event's id in the client's sound event registry, when it was received as a registry reference instead of
    /// inline data; it's sent back the same way.
    /// </summary>
    public int? RegistryId { get; init; }
}
