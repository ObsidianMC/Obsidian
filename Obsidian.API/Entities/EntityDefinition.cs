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

    // The server's copy of the plugin's properties, kept when the plugin unloads since its entities stay saved.
    internal EntitySettings Settings { get; set; } = EntitySettings.From(new EntityProperties(), EntityKind.Mob);

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
internal sealed record EntitySettings(
    float Width,
    float Height,
    float MaxHealth,
    float MovementSpeed,
    float Gravity,
    MobCategory Category,
    VectorF[] PassengerAttachments,
    EntityMovementMode Movement,
    float FlyingSpeed,
    EntityDataField[] DataFields,
    SpawnPlacementType? SpawnPlacement)
{
    // The first metadata index after the fields Obsidian's servers send a mob (Entity, LivingEntity and Mob fields)
    // or, for projectiles and objects, a plain entity.
    private const int FirstMobField = 16;
    private const int FirstEntityField = 8;

    /// <summary>
    /// Copies the properties, giving their data fields their indices for a type of <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A data field is listed twice, or another type gave it another index.
    /// </exception>
    public static EntitySettings From(EntityProperties properties, EntityKind kind)
    {
        EntityDataField[] fields = [.. properties.DataFields];
        var first = kind == EntityKind.Mob ? FirstMobField : FirstEntityField;

        if (fields.Distinct().Count() != fields.Length || first + fields.Length > byte.MaxValue)
            throw new ArgumentException("Each data field can be listed once, up to the metadata's last index.");

        for (var i = 0; i < fields.Length; i++)
        {
            if (fields[i].Index >= 0 && fields[i].Index != first + i)
                throw new ArgumentException("A data field has another index in another entity type.");
        }

        for (var i = 0; i < fields.Length; i++)
            fields[i].Index = first + i;

        return new(
            properties.Width,
            properties.Height,
            properties.MaxHealth,
            properties.MovementSpeed,
            properties.Gravity,
            properties.Category,
            [.. properties.PassengerAttachments],
            properties.Movement,
            properties.FlyingSpeed,
            fields,
            properties.SpawnPlacement);
    }

    /// <summary>
    /// The position of <paramref name="field"/> among the type's data fields, or -1.
    /// </summary>
    public int IndexOf(EntityDataField field) => Array.IndexOf(this.DataFields, field);

    /// <summary>
    /// Where a passenger sits, from the entity's position, for the entity turned to <paramref name="yaw"/> degrees.
    /// </summary>
    /// <remarks>
    /// Matches vanilla's <c>EntityAttachments.getClamped</c> for passengers, which clients position riders with: the
    /// point of the passenger's index, clamped to the last, turned about Y by the yaw; without points, the top of the
    /// hitbox, vanilla's default attachment.
    /// </remarks>
    /// <param name="index">The passenger's index among the entity's passengers.</param>
    /// <param name="yaw">The entity's yaw, in degrees.</param>
    public VectorF PassengerAttachment(int index, float yaw)
    {
        if (this.PassengerAttachments.Length == 0)
            return new VectorF(0, this.Height, 0);

        var point = this.PassengerAttachments[Math.Clamp(index, 0, this.PassengerAttachments.Length - 1)];
        var angle = yaw * MathF.PI / 180;
        var sin = MathF.Sin(angle);
        var cos = MathF.Cos(angle);

        return new VectorF(point.X * cos - point.Z * sin, point.Y, point.Z * cos + point.X * sin);
    }
}
