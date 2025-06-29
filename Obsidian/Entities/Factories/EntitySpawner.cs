using Obsidian.API.Entities;
using Obsidian.WorldData;

namespace Obsidian.Entities.Factories;
internal class EntitySpawner(IWorld world) : IEntitySpawner
{
    private readonly IWorld world = world;

    private EntityType? entityType = null;

    private VectorF position = VectorF.Zero;
    private bool isBaby = false;
    private string? customName = null;
    private bool customNameVisible = false;
    private bool ambientPotionEffect = false;
    private int absorbedArrows = 0;
    private int absorbedStingers = 0;
    private bool burning = false;
    private bool glowing = false;

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
        // This could get sgen'd, same for the entity classes. but for now, this is fine for implementation
        Entity entity = entityType switch
        {
            EntityType.Pig => new Pig()
            {
                World = world,
            },
            EntityType.Horse => new Horse()
            {
                World = world,
            },
            EntityType.Llama => new Llama()
            {
                World = world,
            },
            EntityType.Donkey => new Donkey()
            {
                World = world
            },
            EntityType.SkeletonHorse => new SkeletonHorse()
            {
                World = world
            },
            EntityType.ZombieHorse => new ZombieHorse()
            {
                World = world
            },

            null => throw new InvalidOperationException("Entity type must be set"),

            _ => entityType.Value.IsNonLiving() ?
            new Entity()
            {
                World = world,
            } :
            new Living()
            {
                World = world
            }
        };

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

        return (world as World).SpawnEntity(entity);
    }
}
