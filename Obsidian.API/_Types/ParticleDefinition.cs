using Obsidian.API.Plugins;

namespace Obsidian.API;

/// <summary>
/// A particle type a plugin registered with <see cref="IPluginRegistry.RegisterParticle"/>, like a NeoForge mod's
/// <c>SimpleParticleType</c>: a particle without options, sent as it is with
/// <see cref="ILevel.SpawnParticles"/>.
/// </summary>
/// <remarks>
/// The server owns the definition: it keeps its instance, identifier and id until the server stops, even when the
/// plugin reloads or unloads. Clients draw it with the client mod's sprites
/// (<c>assets/&lt;namespace&gt;/particles/&lt;name&gt;.json</c>).
/// </remarks>
public sealed class ParticleDefinition : ParticleData
{
    internal ParticleDefinition(int id, string identifier)
    {
        this.Id = id;
        this.Identifier = identifier;
    }

    /// <summary>
    /// The particle type's identifier, like <c>myplugin:spark</c>.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The particle type's id in the particle type registry. Vanilla's come first; plugins' follow, in the order they
    /// were first registered.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The particle type's id as a <see cref="API.ParticleType"/>, which names only vanilla's.
    /// </summary>
    public override ParticleType ParticleType => (ParticleType)this.Id;

    public override string ToString() => this.Identifier;
}
