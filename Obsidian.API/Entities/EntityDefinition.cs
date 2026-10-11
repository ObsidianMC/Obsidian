using Obsidian.API.Plugins;

namespace Obsidian.API.Entities;

/// <summary>
/// A kind of entity a plugin registered with <see cref="IPluginRegistry.RegisterEntity"/>: mobs, projectiles or
/// objects (see <see cref="EntityKind"/>).
/// </summary>
/// <remarks>
/// The server owns the definition: it keeps its instance, identifier and id until the server stops, even when the
/// plugin reloads or unloads; a reload replaces only its properties and behaviour.
/// </remarks>
public sealed class EntityDefinition
{
    internal EntityDefinition(int id, string identifier, EntityKind kind)
    {
        this.Id = id;
        this.Identifier = identifier;
        this.Kind = kind;
    }

    /// <summary>
    /// What the type's entities are.
    /// </summary>
    public EntityKind Kind { get; }

    /// <summary>
    /// The entity type's identifier, like <c>myplugin:golem</c>.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The entity type's id in the entity type registry. Vanilla's come first; plugins' follow, in the order they were
    /// first registered.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The plugin that registered the entity type and provides its behaviour, or null while the plugin is unloaded.
    /// </summary>
    public IPluginContainer? Owner => this.Binding?.Owner;

    // The plugin and its behaviour, replaced together when the plugin registers the type again, and dropped when it
    // unloads.
    internal EntityBinding? Binding { get; set; }

    // The server's copy of the plugin's properties, kept when the plugin unloads since its entities stay in the world.
    internal EntitySettings Settings { get; set; } = EntitySettings.From(new EntityProperties());

    public override string ToString() => this.Identifier;
}

/// <summary>
/// The plugin that registered an entity type, and what its entities do.
/// </summary>
internal sealed record EntityBinding(IPluginContainer Owner, IEntityBehavior? Behavior);

/// <summary>
/// The parts of a plugin entity type's <see cref="EntityProperties"/> the server keeps: plain values, so that keeping
/// them doesn't keep the plugin loaded.
/// </summary>
internal sealed record EntitySettings(float Width, float Height, float MaxHealth, float MovementSpeed, float Gravity)
{
    public static EntitySettings From(EntityProperties properties) => new(
        properties.Width,
        properties.Height,
        properties.MaxHealth,
        properties.MovementSpeed,
        properties.Gravity);
}
