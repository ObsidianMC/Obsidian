using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API.Entities;

/// <summary>
/// Can be used to spawn entities. Can be retrieved from <see cref="ILevel.GetNewEntitySpawner"/>.
/// </summary>
public interface IEntitySpawner
{
    /// <summary>
    /// Sets the entity type to spawn.
    /// </summary>
    /// <param name="type">The entity type to spawn</param>
    /// <returns></returns>
    public IEntitySpawner WithEntityType(EntityType type);

    /// <summary>
    /// Whether this entity is a baby entity. Only applicable to Ageable entities.
    /// </summary>
    /// <returns></returns>
    public IEntitySpawner AsBaby();

    /// <summary>
    /// Position to spawn the entity at.
    /// </summary>
    /// <param name="position">Entity position</param>
    /// <returns></returns>
    public IEntitySpawner AtPosition(VectorD position);

    /// <summary>
    /// Spawns the entity with a custom name plate.
    /// </summary>
    /// <param name="name">The name this entity has.</param>
    /// <param name="visible">Whether the name is visible.</param>
    /// <returns></returns>
    public IEntitySpawner WithCustomName(string name, bool visible = true);

    /// <summary>
    /// Sets whether this entity has an ambient potion effect.
    /// </summary>
    /// <param name="ambient">Whether this entity has the ambient potion effect</param>
    /// <returns></returns>
    public IEntitySpawner WithAmbientPotionEffect(bool ambient);

    /// <summary>
    /// Sets the amount of arrows this entity has absorbed.
    /// </summary>
    /// <param name="arrows">The amount of arrows absorbed</param>
    /// <returns></returns>
    public IEntitySpawner WithAbsorbedArrows(int arrows);

    /// <summary>
    /// Sets the amount of absorbed stingers this entity has.
    /// </summary>
    /// <param name="stingers">Amount of absorbed stingers</param>
    /// <returns></returns>
    public IEntitySpawner WithAbsorbedStingers(int stingers);

    /// <summary>
    /// Makes the entity spawn burning.
    /// </summary>
    /// <returns></returns>
    public IEntitySpawner IsBurning();

    /// <summary>
    /// Makes the entity spawn glowing.
    /// </summary>
    /// <returns></returns>
    public IEntitySpawner IsGlowing();

    /// <summary>
    /// Spawns the entity.
    /// </summary>
    /// <returns>The newly spawned entity. Can be cast to the apropriate type.</returns>
    public IEntity Spawn();
}
