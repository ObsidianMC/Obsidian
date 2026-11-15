using Obsidian.API.AI;
using Obsidian.API.Entities;

namespace Obsidian.API;

/// <summary>
/// An entity in a level: a player, a mob, an item, a projectile, a vehicle and so on.
/// </summary>
/// <remarks>
/// Setting most properties changes only the server's state; players see changes that are sent through methods such
/// as <see cref="UpdateAsync(VectorD, MovementFlags)"/> or the level's entity metadata updates.
/// </remarks>
public interface IEntity : Data.IAttachmentHolder
{
    /// <summary>
    /// Gets the level the entity is in.
    /// </summary>
    public ILevel Level { get; }
    /// <summary>
    /// Gets or sets the pathfinder that moves the entity, or <see langword="null"/> if it doesn't pathfind.
    /// </summary>
    public INavigator? Navigator { get; set; }

    /// <summary>
    /// Gets or sets the AI goals that drive the entity, or <see langword="null"/> if it has none.
    /// </summary>
    public IGoalController? GoalController { get; set; }

    /// <summary>
    /// Gets the entity's UUID, which stays the same across saves.
    /// </summary>
    public Guid Uuid { get; }

    /// <summary>
    /// Gets or sets where the entity was before its last move.
    /// </summary>
    public VectorD LastPosition { get; set; }
    /// <summary>
    /// Gets or sets the entity's position, in blocks. Setting it doesn't tell players; use
    /// <see cref="TeleportAsync(VectorD)"/> or <see cref="UpdateAsync(VectorD, MovementFlags)"/> to move it visibly.
    /// </summary>
    public VectorD Position { get; set; }

    /// <summary>
    /// The entity's pitch in degrees: positive looks down. A player's is the exact value their client sent, wrapped
    /// and clamped to [-90, 90] as vanilla does.
    /// </summary>
    /// <remarks>
    /// This was an <see cref="Angle"/> (256ths of a turn) before; it is the exact float now, as vanilla keeps it, so
    /// aimed uses see the direction the client sent. <see cref="Angle"/> still converts implicitly to and from float
    /// degrees, so assigning one keeps working.
    /// </remarks>
    public float Pitch { get; set; }

    /// <summary>
    /// The entity's yaw in degrees: 0 faces south (+Z) and 90 west (-X). A player's is the exact value their client
    /// sent, wrapped to [-180, 180) as vanilla does.
    /// </summary>
    /// <remarks>See <see cref="Pitch"/> about the change from <see cref="Angle"/>.</remarks>
    public float Yaw { get; set; }

    /// <summary>
    /// Gets the entity's numeric id, which identifies it to clients until the server restarts.
    /// </summary>
    public int EntityId { get; }

    /// <summary>
    /// Gets or sets the entity's pose, like standing, sneaking or swimming. A player's bounding box follows it.
    /// </summary>
    public Pose Pose { get; set; }
    /// <summary>
    /// The entity's vanilla type, or null for an entity of a plugin's type.
    /// </summary>
    public EntityType? Type { get; }

    /// <summary>
    /// The plugin entity type the entity is of, or null for a vanilla entity.
    /// </summary>
    public EntityDefinition? Definition { get; }

    /// <summary>
    /// Gets the entity's bounding box at its position, in blocks.
    /// </summary>
    public BoundingBox BoundingBox { get; }
    /// <summary>
    /// Gets the entity's width and height, in blocks.
    /// </summary>
    public EntityDimension Dimension { get; }
    /// <summary>
    /// Gets or sets the entity's remaining air, in ticks; 300 is full.
    /// </summary>
    public short Air { get; set; }

    /// <summary>
    /// Gets or sets the entity's health, in half-hearts.
    /// </summary>
    public float Health { get; set; }

    /// <summary>
    /// Gets or sets the entity's custom name, or <see langword="null"/> if it has none.
    /// </summary>
    public ChatMessage? CustomName { get; set; }

    /// <summary>
    /// Gets the translation key of the entity type's name, like <c>entity.minecraft.zombie</c>, or <see langword="null"/>.
    /// </summary>
    public string? TranslationKey { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the custom name shows above the entity even when it isn't looked at.
    /// </summary>
    public bool CustomNameVisible { get; set; }
    /// <summary>
    /// Gets a value indicating whether the entity makes no sounds.
    /// </summary>
    public bool Silent { get; }
    /// <summary>
    /// Gets a value indicating whether the entity ignores gravity.
    /// </summary>
    public bool NoGravity { get; }
    /// <summary>
    /// Gets the movement flags of the entity's last move, such as whether it is on the ground.
    /// </summary>
    public MovementFlags MovementFlags { get; }
    /// <summary>
    /// Gets or sets a value indicating whether the entity is sneaking.
    /// </summary>
    public bool Sneaking { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether the entity is sprinting.
    /// </summary>
    public bool Sprinting { get; set; }
    /// <summary>
    /// Gets a value indicating whether the entity has a glowing outline.
    /// </summary>
    public bool Glowing { get; }
    /// <summary>
    /// Gets a value indicating whether the entity is invisible.
    /// </summary>
    public bool Invisible { get; }
    /// <summary>
    /// Gets a value indicating whether the entity is on fire.
    /// </summary>
    public bool Burning { get; }
    /// <summary>
    /// Gets or sets a value indicating whether the entity is swimming.
    /// </summary>
    public bool Swimming { get; set; }
    /// <summary>
    /// Gets or sets a value indicating whether the entity is gliding with an elytra.
    /// </summary>
    public bool FlyingWithElytra { get; set; }
    /// <summary>
    /// Gets a value indicating whether the entity is marked as summonable.
    /// </summary>
    public bool Summonable { get; }
    /// <summary>
    /// Gets a value indicating whether fire and lava don't hurt the entity.
    /// </summary>
    public bool IsFireImmune { get; }

    /// <summary>
    /// Removes the entity from its level, which tells the players who see it.
    /// </summary>
    public ValueTask RemoveAsync();
    /// <summary>
    /// Runs one tick of the entity's behaviour. Its level calls it every tick; plugins shouldn't.
    /// </summary>
    public ValueTask TickAsync();
    /// <summary>
    /// Hurts the entity by <paramref name="amount"/>, plays its hurt animation and, for a player, sends their new health.
    /// </summary>
    /// <param name="source">The entity that caused the damage; it must be in the same level.</param>
    /// <param name="amount">The damage, in half-hearts.</param>
    /// <remarks>
    /// Does nothing for an amount that isn't a positive number, an entity with no health left, or a player in creative or
    /// spectator mode.
    /// </remarks>
    public ValueTask DamageAsync(IEntity source, float amount = 1.0f);

    /// <summary>
    /// Kills a mob with enabled AI, crediting <paramref name="source"/>, or removes a vehicle. The server's base entity
    /// implementation does nothing, including for players; use <see cref="KillAsync(IEntity, ChatMessage)"/> for
    /// players.
    /// </summary>
    /// <param name="source">The entity responsible for the death.</param>
    public ValueTask KillAsync(IEntity source);
    /// <summary>
    /// Kills a player with <paramref name="message"/>, crediting <paramref name="source"/>, or removes a vehicle.
    /// The server's base entity implementation does nothing, including for mobs; use <see cref="KillAsync(IEntity)"/>
    /// for mobs with enabled AI.
    /// </summary>
    /// <param name="source">The entity responsible for the death.</param>
    /// <param name="message">The death message.</param>
    public ValueTask KillAsync(IEntity source, ChatMessage message);

    /// <summary>
    /// The entity this one rides, or null.
    /// </summary>
    public IEntity? Vehicle => null;

    /// <summary>
    /// A read-only snapshot of the entities riding this one, in seat order; the first one steers a mount.
    /// </summary>
    public IReadOnlyList<IEntity> Passengers => [];

    /// <summary>
    /// Mounts this entity on another, like vanilla's <c>startRiding</c>: any entity can ride any other in the same
    /// level that has a free seat, leaving its current vehicle first. A vehicle can't ride its own passenger.
    /// </summary>
    /// <remarks>
    /// A player boards only a mob, and only while alive, not a spectator and not already riding. A plugin's mob
    /// carries as many passengers as its type has passenger attachments, at least one, and its first passenger steers
    /// it. From an inventory hook, boarding waits until its transaction releases the click locks; the seat and
    /// cycle checks run then.
    /// </remarks>
    /// <returns>Whether boarding succeeded, or was queued by an inventory hook.</returns>
    public bool StartRiding(IEntity vehicle) => false;

    /// <summary>
    /// Dismounts the entity from what it rides, if anything, after the transaction when called by an inventory hook.
    /// </summary>
    public void StopRiding()
    {
    }

    /// <summary>
    /// Moves this entity and its passengers to a level, preserving entity state and velocity.
    /// Null rotation components retain their current values. The destination is loaded before removal.
    /// </summary>
    public ValueTask TeleportAsync(ILevel level, VectorD position, float? yaw = null, float? pitch = null) =>
        throw new NotSupportedException("This entity doesn't support level travel.");

    /// <summary>
    /// Teleports the entity to <paramref name="world"/>'s spawn position.
    /// </summary>
    /// <param name="world">The world to go to.</param>
    public ValueTask TeleportAsync(IWorld world);
    /// <summary>
    /// Teleports the entity to another entity's level, position and rotation.
    /// </summary>
    /// <param name="to">The entity to go to.</param>
    public ValueTask TeleportAsync(IEntity to);
    /// <summary>
    /// Teleports the entity to <paramref name="pos"/> in its level.
    /// </summary>
    /// <param name="pos">The position to go to.</param>
    public ValueTask TeleportAsync(VectorD pos);

    /// <summary>
    /// Moves the entity to <paramref name="position"/> and sends the move to the players who see it.
    /// </summary>
    /// <param name="position">The new position.</param>
    /// <param name="movementFlags">The move's flags, such as whether the entity is on the ground.</param>
    public ValueTask UpdateAsync(VectorD position, MovementFlags movementFlags);
    /// <summary>
    /// Moves and turns the entity, and sends the change to the players who see it.
    /// </summary>
    /// <param name="position">The new position.</param>
    /// <param name="yaw">The new yaw, in degrees.</param>
    /// <param name="pitch">The new pitch, in degrees.</param>
    /// <param name="movementFlags">The move's flags, such as whether the entity is on the ground.</param>
    public ValueTask UpdateAsync(VectorD position, float yaw, float pitch, MovementFlags movementFlags);
    /// <summary>
    /// Turns the entity, and sends the change to the players who see it when it is large enough to show.
    /// </summary>
    /// <param name="yaw">The new yaw, in degrees.</param>
    /// <param name="pitch">The new pitch, in degrees.</param>
    /// <param name="movementFlags">The move's flags, such as whether the entity is on the ground.</param>
    public ValueTask UpdateAsync(float yaw, float pitch, MovementFlags movementFlags);

    /// <summary>
    /// Writes the entity's metadata entries, as the set entity data packet holds them, without the end marker.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    public void Write(INetStreamWriter writer);

    /// <summary>
    /// Returns whether <paramref name="entity"/> is in the same level and within <paramref name="distance"/> blocks.
    /// </summary>
    /// <param name="entity">The other entity.</param>
    /// <param name="distance">The distance, in blocks.</param>
    public bool IsInRange(IEntity entity, float distance);

    /// <summary>
    /// Spawns the specified entity to player nearby in the world.
    /// </summary>
    /// <param name="velocity">The velocity the entity should spawn with</param>
    /// <param name="additionalData">Additional data for the entity. More info here: <see href="https://wiki.vg/Object_Data"/></param>
    public void SpawnEntity(Velocity? velocity = null, int additionalData = 0);

    /// <summary>
    /// Gets the other entities in the level within <paramref name="distance"/> blocks of this one.
    /// </summary>
    /// <param name="distance">The distance, in blocks.</param>
    public IEnumerable<IEntity> GetEntitiesNear(float distance);

    /// <summary>
    /// The unit vector the entity looks along, from its <see cref="Pitch"/> and <see cref="Yaw"/>: vanilla's
    /// <c>getViewVector</c>.
    /// </summary>
    public VectorF GetLookDirection();

    /// <summary>
    /// Sends the entity's head yaw to the players in range. It doesn't change <see cref="Yaw"/>.
    /// </summary>
    /// <param name="headYaw">The head's yaw, in degrees.</param>
    public void SetHeadRotation(float headYaw);
    /// <summary>
    /// Turns the entity and sends its new rotation to the players in range.
    /// </summary>
    /// <param name="yaw">The new yaw, in degrees.</param>
    /// <param name="pitch">The new pitch, in degrees.</param>
    /// <param name="movementFlags">The move's flags, such as whether the entity is on the ground.</param>
    public void SetRotation(float yaw, float pitch, MovementFlags movementFlags);

    /// <summary>
    /// Returns whether the entity has the attribute, like <c>minecraft:max_health</c>.
    /// </summary>
    /// <param name="attributeResourceName">The attribute's identifier.</param>
    public bool HasAttribute(string attributeResourceName);
    /// <summary>
    /// Gives the entity an attribute it doesn't have yet, with base value <paramref name="value"/>.
    /// </summary>
    /// <param name="attributeResourceName">The attribute's identifier.</param>
    /// <param name="value">The attribute's base value.</param>
    /// <returns><see langword="true"/> if the attribute was added; <see langword="false"/> if the entity already has it.</returns>
    public bool TryAddAttribute(string attributeResourceName, float value);
    /// <summary>
    /// Sets the base value of an attribute the entity has.
    /// </summary>
    /// <param name="attributeResourceName">The attribute's identifier.</param>
    /// <param name="newValue">The new base value.</param>
    /// <returns><see langword="true"/> if the value was set; <see langword="false"/> if the entity doesn't have the attribute.</returns>
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
