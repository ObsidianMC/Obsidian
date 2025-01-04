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

    private EntityType entityType;

    private int entityId = 0;
    private VectorF position = VectorF.Zero;
    private bool isBaby = false;
    private string? customName = null;
    private bool customNameVisible = false;

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
            _ => entityType.IsNonLiving() ?
            new Entity()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world,
            } :
            new Living()
            {
                PacketBroadcaster = packetBroadcaster,
                World = world,
            }
        };

        entity.Type = entityType;
        entity.EntityId = entityId;
        entity.Position = position;

        if(entity is ILiving living && customName != null)
        {
            living.CustomName = customName;
            living.CustomNameVisible = customNameVisible;
        }

        return (world as World).SpawnEntity(entity);
    }
}
