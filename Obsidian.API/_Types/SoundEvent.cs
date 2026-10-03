namespace Obsidian.API;
public readonly record struct SoundEvent
{
    public required string ResourceLocation { get; init; }
    public float? FixedRange { get; init; }

    /// <summary>
    /// The sound event's id in the client's sound event registry, when it was received as a registry reference instead of
    /// inline data; it's sent back the same way.
    /// </summary>
    public int? RegistryId { get; init; }
}
