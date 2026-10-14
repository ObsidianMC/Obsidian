using Obsidian.API.AI;
using Obsidian.API.Entities;

namespace Obsidian.API;

public interface IEntity : Data.IAttachmentHolder
{
    public ILevel Level { get; }
    public INavigator? Navigator { get; set; }

    public IGoalController? GoalController { get; set; }

    public Guid Uuid { get; set; }

    public VectorD LastPosition { get; set; }
    public VectorD Position { get; set; }
    public Angle Pitch { get; set; }
    public Angle Yaw { get; set; }

    public int EntityId { get; }

    public Pose Pose { get; set; }
    /// <summary>
    /// The entity's vanilla type, or null for an entity of a plugin's type.
    /// </summary>
    public EntityType? Type { get; }

    /// <summary>
    /// The plugin entity type the entity is of, or null for a vanilla entity.
    /// </summary>
    public EntityDefinition? Definition { get; }

    public BoundingBox BoundingBox { get; }
    public EntityDimension Dimension { get; }
    public short Air { get; set; }

    public float Health { get; set; }

    public ChatMessage? CustomName { get; set; }

    public string? TranslationKey { get; }

    public bool CustomNameVisible { get; set; }
    public bool Silent { get; }
    public bool NoGravity { get; }
    public MovementFlags MovementFlags { get; }
    public bool Sneaking { get; set; }
    public bool Sprinting { get; set; }
    public bool Glowing { get; }
    public bool Invisible { get; }
    public bool Burning { get; }
    public bool Swimming { get; set; }
    public bool FlyingWithElytra { get; set; }
    public bool Summonable { get; }
    public bool IsFireImmune { get; }

    public ValueTask RemoveAsync();
    public ValueTask TickAsync();
    public ValueTask DamageAsync(IEntity source, float amount = 1.0f);

    public ValueTask KillAsync(IEntity source);
    public ValueTask KillAsync(IEntity source, ChatMessage message);

    /// <summary>
    /// The entity this one rides, or null.
    /// </summary>
    public IEntity? Vehicle => null;

    /// <summary>
    /// The entities riding this one, in seat order; the first one steers a mount.
    /// </summary>
    public IReadOnlyList<IEntity> Passengers => [];

    /// <summary>
    /// Mounts this entity on another, like vanilla's <c>startRiding</c>: any entity can ride any other in the same
    /// level that has a free seat, leaving its current vehicle first. A vehicle can't ride its own passenger.
    /// </summary>
    /// <remarks>
    /// A player boards only a mob, and only while alive, not a spectator and not already riding. A plugin's mob
    /// carries as many passengers as its type has passenger attachments, at least one, and its first passenger steers
    /// it.
    /// </remarks>
    /// <returns>Whether the entity now rides <paramref name="vehicle"/>.</returns>
    public bool StartRiding(IEntity vehicle) => false;

    /// <summary>
    /// Dismounts the entity from what it rides, if anything.
    /// </summary>
    public void StopRiding()
    {
    }

    /// <summary>
    /// Moves this entity and its passengers to a level, preserving entity state and velocity.
    /// Null rotation components retain their current values. The destination is loaded before removal.
    /// </summary>
    public ValueTask TeleportAsync(ILevel level, VectorD position, Angle? yaw = null, Angle? pitch = null) =>
        throw new NotSupportedException("This entity doesn't support level travel.");

    public ValueTask TeleportAsync(IWorld world);
    public ValueTask TeleportAsync(IEntity to);
    public ValueTask TeleportAsync(VectorD pos);

    public ValueTask UpdateAsync(VectorD position, MovementFlags movementFlags);
    public ValueTask UpdateAsync(VectorD position, Angle yaw, Angle pitch, MovementFlags movementFlags);
    public ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags);

    public void Write(INetStreamWriter writer);

    public bool IsInRange(IEntity entity, float distance);

    /// <summary>
    /// Spawns the specified entity to player nearby in the world.
    /// </summary>
    /// <param name="velocity">The velocity the entity should spawn with</param>
    /// <param name="additionalData">Additional data for the entity. More info here: <see href="https://wiki.vg/Object_Data"/></param>
    public void SpawnEntity(Velocity? velocity = null, int additionalData = 0);

    public IEnumerable<IEntity> GetEntitiesNear(float distance);

    public VectorF GetLookDirection();

    public void SetHeadRotation(Angle headYaw);
    public void SetRotation(Angle yaw, Angle pitch, MovementFlags movementFlags);

    public bool HasAttribute(string attributeResourceName);
    public bool TryAddAttribute(string attributeResourceName, float value);
    public bool TryUpdateAttribute(string attributeResourceName, float newValue);

    /// <summary>
    /// The attribute's value: its base with its modifiers applied, clamped to vanilla's range for it; 0 if the entity
    /// doesn't have the attribute.
    /// </summary>
    public float GetAttributeValue(string attributeResourceName);

    /// <summary>
    /// Adds a modifier to one of the entity's attributes, replacing its modifier with the same <paramref name="id"/>,
    /// like vanilla's permanent modifiers: it is saved, and players tracking the entity are sent the change.
    /// </summary>
    /// <param name="attribute">The attribute, like <c>minecraft:movement_speed</c>.</param>
    /// <param name="id">The modifier's identifier, like <c>myplugin:charge</c>.</param>
    /// <param name="amount">What the modifier adds or multiplies by.</param>
    /// <param name="operation">
    /// How the amount applies, in vanilla's order: added to the base, then multiples of that sum added, then the
    /// result multiplied by one plus the amount.
    /// </param>
    /// <returns>Whether the entity has the attribute.</returns>
    public bool SetAttributeModifier(
        string attribute,
        string id,
        double amount,
        Inventory.AttributeOperation operation) => false;

    /// <summary>
    /// Removes the attribute's modifier with the identifier.
    /// </summary>
    /// <returns>Whether there was one.</returns>
    public bool RemoveAttributeModifier(string attribute, string id) => false;

    /// <summary>
    /// The value of one of the entity's synced data fields (see <see cref="EntityProperties.DataFields"/>).
    /// </summary>
    /// <exception cref="ArgumentException">The entity's type doesn't have the field.</exception>
    public T GetData<T>(EntityDataField<T> field) =>
        throw new ArgumentException("The entity's type doesn't have the field.", nameof(field));

    /// <summary>
    /// Sets one of the entity's synced data fields; players tracking the entity with a modded Obsidian client are
    /// sent the change on its next tick.
    /// </summary>
    /// <exception cref="ArgumentException">The entity's type doesn't have the field.</exception>
    public void SetData<T>(EntityDataField<T> field, T value) =>
        throw new ArgumentException("The entity's type doesn't have the field.", nameof(field));
}
