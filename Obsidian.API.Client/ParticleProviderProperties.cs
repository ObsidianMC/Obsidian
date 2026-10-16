namespace Obsidian.API.Client;

/// <summary>
/// How the Obsidian client moves and draws a mod's particle type, like a NeoForge mod's particle provider making a
/// sprite particle. Register it with <see cref="IClientRegistry.RegisterParticleProvider"/>; without one, the type's
/// particles behave like vanilla's base particle.
/// </summary>
/// <remarks>
/// The particle's sprites are its definition's, <c>assets/&lt;ns&gt;/particles/&lt;name&gt;.json</c>, like
/// vanilla's: <c>{"textures": ["&lt;ns&gt;:&lt;texture&gt;", ...]}</c>, from
/// <c>assets/&lt;ns&gt;/textures/particle/</c>.
/// </remarks>
public sealed class ParticleProviderProperties
{
    /// <summary>
    /// How many ticks a particle lives; 0 for vanilla's base particle's random lifetime, from 4 to 40 ticks.
    /// </summary>
    public int Lifetime { get; init; }

    /// <summary>
    /// How strongly particles fall, like vanilla's particles' <c>gravity</c>: 1 speeds their fall by 0.04 blocks per
    /// tick each tick, and a negative value makes them rise.
    /// </summary>
    public float Gravity { get; init; }

    /// <summary>
    /// What a particle's velocity is multiplied by each tick, like vanilla's particles' <c>friction</c>.
    /// </summary>
    public float Friction { get; init; } = 0.98f;

    /// <summary>
    /// How much bigger than vanilla's base particle (0.1 to 0.2 blocks) particles are drawn.
    /// </summary>
    public float Scale { get; init; } = 1;

    /// <summary>
    /// Whether a particle's velocity is randomised like vanilla's base particle's (a random direction with a slight
    /// upward push, around the velocity the server sent), rather than exactly the server's.
    /// </summary>
    public bool RandomizeVelocity { get; init; } = true;

    /// <summary>
    /// Whether a particle shows its sprites in turn over its life, rather than one picked at random.
    /// </summary>
    public bool AnimateSprite { get; init; }
}
