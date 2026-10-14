namespace Obsidian.API.Entities;

/// <summary>
/// What the entities of a plugin's entity type do. Implement the methods the entity needs.
/// </summary>
/// <remarks>
/// The behaviour belongs to the plugin and is dropped when the plugin unloads, while its entities are kept in their
/// chunks as saved until it's loaded again, so keep state on the entity or in the plugin, not here.
/// </remarks>
public interface IEntityBehavior
{
    /// <summary>
    /// Called every tick the entity is alive, after its movement.
    /// </summary>
    public ValueTask TickAsync(IEntity entity) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a player uses the entity (right-clicks it), before the held item's use on it.
    /// </summary>
    /// <returns>
    /// Whether the entity used the interaction, which keeps the held item from being used on it (see
    /// <see cref="Inventory.IItemBehavior.UseOnEntityAsync"/>), like a vanilla entity's interaction succeeding.
    /// </returns>
    public ValueTask<bool> InteractAsync(IEntity entity, IPlayer player, InteractionHand hand) =>
        ValueTask.FromResult(false);

    /// <summary>
    /// Called when a projectile hits a block or a living entity, before it's removed.
    /// </summary>
    public ValueTask HitAsync(IEntity projectile, EntityHit hit) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a player who isn't sneaking uses a mob and <see cref="InteractAsync"/> didn't use the interaction:
    /// whether the player mounts it (see <see cref="IPlayer.StartRiding"/>).
    /// </summary>
    public bool CanMount(IEntity entity, IPlayer player) => false;

    /// <summary>
    /// Called when a mob is about to take damage, after vanilla's immunities (its fire immunity, say) and before its
    /// hurt cooldown, armour and absorption: like NeoForge's <c>LivingIncomingDamageEvent</c>, change
    /// <see cref="EntityDamage.Amount"/> or set <see cref="EntityDamage.Cancelled"/> to take none.
    /// </summary>
    public ValueTask HurtAsync(IEntity entity, EntityDamage damage) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a mob dies, before it drops its loot table.
    /// </summary>
    public ValueTask DiedAsync(IEntity entity, DamageSource source) => ValueTask.CompletedTask;

    /// <summary>
    /// Called before natural spawning places a mob of a type with a <see cref="EntityProperties.SpawnPlacement"/>,
    /// once its placement fits, like a vanilla entity type's spawn predicate: whether it may spawn there, its feet in
    /// the block at <paramref name="position"/>. Runs on the level's tick; read the level's loaded blocks only.
    /// </summary>
    public bool CanSpawn(ILevel level, Vector position) => true;
}

/// <summary>
/// What damaged an entity, like vanilla's <c>DamageSource</c>.
/// </summary>
/// <param name="Type">
/// The damage type's identifier, like <c>minecraft:player_attack</c>, <c>minecraft:mob_attack</c>,
/// <c>minecraft:arrow</c>, <c>minecraft:fall</c>, <c>minecraft:on_fire</c> or <c>minecraft:magic</c>.
/// </param>
/// <param name="Attacker">The entity responsible, like the player who shot an arrow, if any.</param>
/// <param name="DirectEntity">The entity that dealt it, like the arrow, if any.</param>
public sealed record DamageSource(string Type, IEntity? Attacker, IEntity? DirectEntity);

/// <summary>
/// Damage about to be dealt to an entity, which <see cref="IEntityBehavior.HurtAsync"/> may change or cancel.
/// </summary>
public sealed class EntityDamage
{
    public required DamageSource Source { get; init; }

    /// <summary>
    /// How much damage the entity takes, before its armour and absorption.
    /// </summary>
    public float Amount { get; set; }

    /// <summary>
    /// Whether the entity takes no damage at all.
    /// </summary>
    public bool Cancelled { get; set; }
}

/// <summary>
/// What a projectile hit.
/// </summary>
public sealed class EntityHit
{
    /// <summary>
    /// The living entity it hit, or null when it hit a block.
    /// </summary>
    public IEntity? Entity { get; init; }

    /// <summary>
    /// The block it hit, or null when it hit an entity.
    /// </summary>
    public Vector? Block { get; init; }

    /// <summary>
    /// Where it hit.
    /// </summary>
    public required VectorD Position { get; init; }

    /// <summary>
    /// The entity that shot the projectile, if any.
    /// </summary>
    public IEntity? Owner { get; init; }
}
