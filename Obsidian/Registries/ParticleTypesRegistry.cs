using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The particle types plugins registered, after vanilla's: their ids follow vanilla's, in registration order. They
/// have no behaviour on the server, so they stay as registered until the server stops.
/// </summary>
internal static class ParticleTypesRegistry
{
    private static readonly Lock registering = new();

    // Registering replaces the table whole, so lookups never lock.
    private static ParticleDefinition[] particles = [];
    private static FrozenDictionary<string, ParticleDefinition> byName =
        FrozenDictionary<string, ParticleDefinition>.Empty;

    /// <summary>
    /// Vanilla's particle types' identifiers, indexed by id.
    /// </summary>
    public static IReadOnlyList<string> VanillaIdentifiers => VanillaRegistryIds.Get("minecraft:particle_type");

    /// <summary>
    /// The particle types plugins registered, in id order.
    /// </summary>
    public static IReadOnlyList<ParticleDefinition> PluginParticles => particles;

    /// <summary>
    /// Finds a plugin's particle type by its identifier, ignoring case.
    /// </summary>
    public static bool TryGet(string identifier, [NotNullWhen(true)] out ParticleDefinition? particle) =>
        byName.TryGetValue(identifier, out particle);

    /// <summary>
    /// Finds a plugin's particle type by its id.
    /// </summary>
    public static bool TryGet(int id, [NotNullWhen(true)] out ParticleDefinition? particle)
    {
        var current = particles;
        var index = id - VanillaIdentifiers.Count;

        particle = index >= 0 && index < current.Length ? current[index] : null;
        return particle is not null;
    }

    /// <summary>
    /// Registers a particle type, or returns the one registered before under the same identifier, which keeps its id.
    /// </summary>
    /// <exception cref="ArgumentException">The identifier is in vanilla's namespace.</exception>
    public static ParticleDefinition Register(string identifier)
    {
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} is in vanilla's namespace.", nameof(identifier));

        lock (registering)
        {
            if (byName.TryGetValue(identifier, out var existing))
                return existing;

            var particle = new ParticleDefinition(VanillaIdentifiers.Count + particles.Length, identifier);

            particles = [.. particles, particle];
            byName = particles.ToFrozenDictionary(entry => entry.Identifier, StringComparer.OrdinalIgnoreCase);

            return particle;
        }
    }
}
