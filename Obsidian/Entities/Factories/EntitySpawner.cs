using Obsidian.API.Entities;
using Obsidian.API.World;
using Obsidian.WorldData;

namespace Obsidian.Entities.Factories;

internal class EntitySpawner(ILevel level) : IEntitySpawner
{
    private readonly ILevel level = level;

    private EntityType? entityType;

    private VectorF position = VectorF.Zero;
    private bool isBaby;
    private string? customName;
    private bool customNameVisible;
    private bool ambientPotionEffect;
    private int absorbedArrows;
    private int absorbedStingers;
    private bool burning;
    private bool glowing;

    public IEntitySpawner WithEntityType(EntityType type)
    {
        entityType = type;
        return this;
    }

    public IEntitySpawner AsBaby()
    {
        isBaby = true;
        return this;
    }

    public IEntitySpawner AtPosition(VectorF position)
    {
        this.position = position;
        return this;
    }

    public IEntitySpawner WithCustomName(string name, bool visible = true)
    {
        customName = name;
        customNameVisible = visible;
        return this;
    }

    public IEntitySpawner WithAmbientPotionEffect(bool ambient)
    {
        ambientPotionEffect = ambient;
        return this;
    }

    public IEntitySpawner WithAbsorbedArrows(int arrows)
    {
        absorbedArrows = arrows;
        return this;
    }

    public IEntitySpawner WithAbsorbedStingers(int stingers)
    {
        absorbedStingers = stingers;
        return this;
    }

    public IEntitySpawner IsBurning()
    {
        this.burning = true;
        return this;
    }

    public IEntitySpawner IsGlowing()
    {
        this.glowing = true;
        return this;
    }

    public IEntity Spawn()
    {
        if (entityType is null)
            throw new InvalidOperationException("Entity type must be set");

        var entity = Create(entityType.Value, level);

        entity.Type = entityType.Value;
        entity.EntityId = Server.GetNextEntityId();
        entity.Position = position;

        if (entity is Living living && customName != null)
        {
            living.CustomName = customName;
            living.CustomNameVisible = customNameVisible;
            living.AmbientPotionEffect = ambientPotionEffect;
            living.AbsorbedArrows = absorbedArrows;
            living.AbsorbedStingers = absorbedStingers;
        }

        if (entity is AgeableMob ageable && isBaby)
        {
            ageable.IsBaby = isBaby;
        }

        entity.Burning = burning;
        entity.Glowing = glowing;

        return level.SpawnEntity(entity);
    }

    /// <summary>
    /// A new entity of the class Obsidian has for <paramref name="type"/>: a <see cref="Living"/> or plain
    /// <see cref="Entity"/> for types without one. The caller sets the type, id and position.
    /// </summary>
    // This could get sgen'd, same for the entity classes. but for now, this is fine for implementation
    internal static Entity Create(EntityType type, ILevel level) => type switch
    {
        EntityType.Pig => new Pig { Level = level },
        EntityType.Horse => new Horse { Level = level },
        EntityType.Llama => new Llama { Level = level },
        EntityType.Donkey => new Donkey { Level = level },
        EntityType.SkeletonHorse => new SkeletonHorse { Level = level },
        EntityType.ZombieHorse => new ZombieHorse { Level = level },
        _ => type.IsNonLiving() ? new Entity { Level = level } : new Living { Level = level }
    };
}
