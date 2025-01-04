using Obsidian.Services;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Entities.Factories;
internal class EntityFactory
{
    private EntityType entityType;
    private PacketBroadcaster packetBroadcaster;
    private World world;

    private int entityId = 0;
    private VectorF position = VectorF.Zero;

    public EntityFactory(EntityType type, PacketBroadcaster packetBroadcaster, World world)
    {
        entityType = type;
        this.packetBroadcaster = packetBroadcaster;
        this.world = world;
    }

    public EntityFactory WithPosition(VectorF position)
    {
        this.position = position;
        return this;
    }

    public EntityFactory WithEntityId(int entityId)
    {
        this.entityId = entityId;
        return this;
    }

    public Entity Build()
    {
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

        return entity;
    }
}
