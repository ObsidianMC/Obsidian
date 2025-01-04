using Obsidian.API.Entities;
using Obsidian.Services;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Entities.Factories;
internal class EntitySpawner : IEntitySpawner
{
    private IWorld world;

    private EntityType? entityType = null;

    private int entityId = 0;
    private VectorF position = VectorF.Zero;
    private bool isBaby = false;
    private string? customName = null;
    private bool customNameVisible = false;
    private LivingBitMask livingBitMask;
    private bool ambientPotionEffect;
    private int absorbedArrows;
    private int absorbtionAmount;
    private int absorbedStingers;
    private bool burning;
    private bool glowing;

    public EntitySpawner(IWorld world)
    {
        this.world = world;
    }

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

    public IEntitySpawner WithEntityId(int entityId)
    {
        this.entityId = entityId;
        return this;
    }

    public IEntitySpawner WithCustomName(string name, bool visible = true)
    {
        customName = name;
        customNameVisible = visible;
        return this;
    }

    public IEntitySpawner WithLivingBitMask(LivingBitMask bitMask)
    {
        livingBitMask = bitMask;
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

    public IEntitySpawner WithAbsorbtionAmount(int amount)
    {
        absorbtionAmount = amount;
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
        var packetBroadcaster = (world as World).PacketBroadcaster;

        // This could get sgen'd, same for the entity classes. but for now, this is fine for implementation
        Entity entity = entityType switch
        {
            EntityType.Pig => new Pig()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world,
            },
            EntityType.Horse => new Horse()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world,
            },
            EntityType.Llama => new Llama()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world,
            },
            EntityType.Donkey => new Donkey()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world
            },
            EntityType.SkeletonHorse => new SkeletonHorse()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world
            },
            EntityType.ZombieHorse => new ZombieHorse()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world
            },

            null => throw new InvalidOperationException("Entity type must be set"),

            _ => entityType.Value.IsNonLiving() ?
            new Entity()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world,
            } :
            new Living()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world
            }
        };

        entity.Type = entityType.Value;
        entity.EntityId = entityId;
        entity.Position = position;

        if (entity is Living living && customName != null)
        {
            living.CustomName = customName;
            living.CustomNameVisible = customNameVisible;

            living.LivingBitMask = livingBitMask;
            living.AmbientPotionEffect = ambientPotionEffect;
            living.AbsorbedArrows = absorbedArrows;
            living.AbsorbtionAmount = absorbtionAmount;
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
