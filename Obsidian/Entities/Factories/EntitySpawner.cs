using Obsidian.API.Entities;
using Obsidian.API.World;
using Obsidian.WorldData;

namespace Obsidian.Entities.Factories;

internal class EntitySpawner(ILevel level) : IEntitySpawner
{
    private readonly ILevel level = level;

    private EntityType? entityType;

    private VectorD position = VectorD.Zero;
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

    public IEntitySpawner AtPosition(VectorD position)
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

        if (entity is Zombie zombie && isBaby)
            zombie.IsBaby = true;
        if (entity is Zoglin zoglin && isBaby)
            zoglin.IsBaby = true;
        if (entity is Piglin piglin && isBaby)
            piglin.IsBaby = true;

        entity.Burning = burning;
        entity.Glowing = glowing;

        return level.SpawnEntity(entity);
    }

    internal static Entity Create(EntityType type, ILevel level) => CreateMob(level, type) ?? (type switch
    {
        EntityType.Fireball or EntityType.SmallFireball or EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion => new MobProjectile(level, type),
        EntityType.ExperienceOrb => new ExperienceOrb { Level = level, Value = 1 },
        EntityType.Arrow => new Arrow { Level = level },
        EntityType.Trident => new Trident { Level = level },
        EntityType.EvokerFangs => new EvokerFangs { Level = level },
        EntityType.ShulkerBullet => new ShulkerBullet { Level = level },
        EntityType.BreezeWindCharge => new BreezeWindCharge(level),
        EntityType.WitherSkull => new WitherSkull { Level = level },
        EntityType.EndCrystal => new EndCrystal { Level = level },
        EntityType.DragonFireball => new DragonFireball(level),
        EntityType.Horse => new Horse { Level = level },
        EntityType.Llama => new Llama { Level = level },
        EntityType.Donkey => new Donkey { Level = level },
        EntityType.SkeletonHorse => new SkeletonHorse { Level = level },
        EntityType.ZombieHorse => new ZombieHorse { Level = level },
        _ => type.IsNonLiving() ? new Entity { Level = level } : new Living { Level = level }
    });

    internal static Mob? CreateMob(ILevel level, EntityType? type) => type switch
    {
        EntityType.Breeze => new Breeze { Level = level },
        EntityType.CamelHusk => new CamelHusk { Level = level },
        EntityType.Creaking => new Creaking { Level = level },
        EntityType.Giant => new Giant { Level = level },
        EntityType.TraderLlama => new TraderLlama { Level = level },
        EntityType.WanderingTrader => new WanderingTrader { Level = level },
        EntityType.ZombieNautilus => new ZombieNautilus { Level = level },
        EntityType.ZombieVillager => new ZombieVillager { Level = level },
        EntityType.Wither => new Wither { Level = level },
        EntityType.EnderDragon => new EnderDragon { Level = level },
        EntityType.Warden => new Warden { Level = level },
        EntityType.Allay => new Allay { Level = level },
        EntityType.Armadillo => new Armadillo { Level = level },
        EntityType.CopperGolem => new CopperGolem { Level = level },
        EntityType.HappyGhast => new HappyGhast { Level = level },
        EntityType.Nautilus => new Nautilus { Level = level },
        EntityType.Sniffer => new Sniffer { Level = level },
        EntityType.Strider => new Strider { Level = level },
        EntityType.Turtle => new Turtle { Level = level },
        EntityType.Pig => new Pig { Level = level },
        EntityType.Rabbit => new Rabbit { Level = level },
        EntityType.PolarBear => new PolarBear { Level = level },
        EntityType.Cow => new Cow { Level = level },
        EntityType.Mooshroom => new Mooshroom { Level = level },
        EntityType.Chicken => new Chicken { Level = level },
        EntityType.Sheep => new Sheep { Level = level },
        EntityType.Zombie => new Zombie { Level = level },
        EntityType.Husk => new Husk { Level = level },
        EntityType.Skeleton => new Skeleton { Level = level },
        EntityType.Stray => new Stray { Level = level },
        EntityType.Bogged => new Bogged { Level = level },
        EntityType.Parched => new Parched { Level = level },
        EntityType.Creeper => new Creeper { Level = level },
        EntityType.Slime => new Slime { Level = level },
        EntityType.Horse => new Horse { Level = level },
        EntityType.ZombieHorse => new ZombieHorse { Level = level },
        EntityType.Villager => new Villager { Level = level },
        EntityType.IronGolem => new IronGolem { Level = level },
        EntityType.Ocelot => new Ocelot { Level = level },
        EntityType.Cat => new Cat { Level = level },
        EntityType.Camel => new Camel { Level = level },
        EntityType.Blaze => new Blaze { Level = level },
        EntityType.CaveSpider => new CaveSpider { Level = level },
        EntityType.Spider => new Spider { Level = level },
        EntityType.Enderman => new Enderman { Level = level },
        EntityType.Ghast => new Ghast { Level = level },
        EntityType.MagmaCube => new MagmaCube { Level = level },
        EntityType.Silverfish => new Silverfish { Level = level },
        EntityType.SnowGolem => new SnowGolem { Level = level },
        EntityType.Squid => new Squid { Level = level },
        EntityType.Bat => new Bat { Level = level },
        EntityType.Bee => new Bee { Level = level },
        EntityType.Fox => new Fox { Level = level },
        EntityType.Frog => new Frog { Level = level },
        EntityType.Llama => new Llama { Level = level },
        EntityType.SkeletonHorse => new SkeletonHorse { Level = level },
        EntityType.Tadpole => new Tadpole { Level = level },
        EntityType.GlowSquid => new GlowSquid { Level = level },
        EntityType.Dolphin => new Dolphin { Level = level },
        EntityType.Donkey => new Donkey { Level = level },
        EntityType.Axolotl => new Axolotl { Level = level },
        EntityType.Goat => new Goat { Level = level },
        EntityType.Panda => new Panda { Level = level },
        EntityType.Parrot => new Parrot { Level = level },
        EntityType.Wolf => new Wolf { Level = level },
        EntityType.WitherSkeleton => new WitherSkeleton { Level = level },
        EntityType.Endermite => new Endermite { Level = level },
        EntityType.Mule => new Mule { Level = level },
        EntityType.Vindicator => new Vindicator { Level = level },
        EntityType.Cod => new Cod { Level = level },
        EntityType.Salmon => new Salmon { Level = level },
        EntityType.TropicalFish => new TropicalFish { Level = level },
        EntityType.Pufferfish => new Pufferfish { Level = level },
        EntityType.Drowned => new Drowned { Level = level },
        EntityType.ZombifiedPiglin => new ZombifiedPiglin { Level = level },
        EntityType.Guardian => new Guardian { Level = level },
        EntityType.ElderGuardian => new ElderGuardian { Level = level },
        EntityType.Hoglin => new Hoglin { Level = level },
        EntityType.Zoglin => new Zoglin { Level = level },
        EntityType.Pillager => new Pillager { Level = level },
        EntityType.PiglinBrute => new PiglinBrute { Level = level },
        EntityType.Vex => new Vex { Level = level },
        EntityType.Ravager => new Ravager { Level = level },
        EntityType.Evoker => new Evoker { Level = level },
        EntityType.Phantom => new Phantom { Level = level },
        EntityType.Illusioner => new Illusioner { Level = level },
        EntityType.Witch => new Witch { Level = level },
        EntityType.Piglin => new Piglin { Level = level },
        EntityType.Shulker => new Shulker { Level = level },
        _ => null
    };
}
