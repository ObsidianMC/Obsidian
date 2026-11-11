using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:ominous_item_spawner")]
public sealed partial class OminousItemSpawner : Entity
{
    public OminousItemSpawner() => Type = EntityType.OminousItemSpawner;

    public ItemStack? Item { get; set; }

    private long releaseAfter;
    private int age;
    private bool removed;

    /// <summary>Creates the item clients see, released after a level-random delay of 60 to 120 ticks.</summary>
    internal static OminousItemSpawner Create(AbstractLevel level, ItemStack item) => new()
    {
        Level = level,
        EntityId = Server.GetNextEntityId(),
        Item = item,
        releaseAfter = level.GrowthRandom.NextIntBetweenInclusive(60, 120)
    };

    public override ValueTask DamageAsync(IEntity source, float amount = 1) => default;

    public override async ValueTask TickAsync()
    {
        if (this.removed || this.Level is not AbstractLevel level)
            return;

        this.age++;
        if (this.age == this.releaseAfter - 36)
        {
            var position = (Vector)this.Position.Floor();
            level.PacketBroadcaster.BroadcastToLevelInRange(level, this.Position, new SoundPacket
            {
                SoundLocation = "minecraft:block.trial_spawner.about_to_spawn_item",
                SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
                Category = SoundCategory.Neutral,
                Volume = 1,
                Pitch = 1,
                Seed = level.NextSoundSeed()
            });
        }

        if (this.age < this.releaseAfter)
            return;

        if (this.Item is { IsAir: false, Count: > 0 } item)
        {
            var entity = this.Release(level, item);
            level.TrySpawnEntity(entity, hostOwned: true);
            level.BroadcastLevelEvent(3021, (Vector)this.Position.Floor(), 1);
            level.EmitGameEvent(MobGameEvent.EntityPlace, this.Position, entity);
            this.Item = null;
        }

        this.removed = true;
        level.EmitGameEvent(MobGameEvent.EntityDie, this.Position, this);
        await this.RemoveAsync();
    }

    // Projectile spread uses the new projectile's own random, never the level's. A seeded source lets the bounded
    // vanilla probe check those draws independently of the live scenario's deliberately unseeded entity randoms.
    internal static VectorD ReleaseMotion(IRandomSource random, float power, float spread) => new VectorD(
        random.Triangle(0d, 0.0172275 * spread),
        random.Triangle(-1d, 0.0172275 * spread),
        random.Triangle(0d, 0.0172275 * spread)) * power;

    internal static ItemEntity LooseItem(AbstractLevel level, VectorD position, ItemStack item)
    {
        var random = Random.Shared;
        return new ItemEntity
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = position,
            Item = item,
            CanPickup = true,
            Motion = new VectorD(random.NextDouble() * 0.2 - 0.1, 0.2, random.NextDouble() * 0.2 - 0.1)
        };
    }

    private Entity Release(AbstractLevel level, ItemStack stack)
    {
        var id = stack.Holder.Id;
        var random = new LegacyRandomSource(Random.Shared.NextInt64());
        var thrown = id is ItemIds.SplashPotion or ItemIds.LingeringPotion or ItemIds.ExperienceBottle;
        var power = thrown ? 1.1f * 1.25f : 1.1f;
        var spread = thrown ? 3f : 6f;
        if (id is ItemIds.FireCharge or ItemIds.WindCharge)
        {
            // Both factories consume three level-random triangles before Projectile.shoot replaces their motion.
            level.GrowthRandom.Triangle(0d, 0.11485000000000001);
            level.GrowthRandom.Triangle(-1d, 0.11485000000000001);
            level.GrowthRandom.Triangle(0d, 0.11485000000000001);
            power = 1f;
            spread = 6.6666665f;
            level.BroadcastLevelEvent(id == ItemIds.FireCharge ? 1018 : 1051, (Vector)this.Position.Floor(), 0);
        }

        var motion = ReleaseMotion(random, power, spread);
        if (id is ItemIds.Arrow or ItemIds.TippedArrow or ItemIds.SpectralArrow)
        {
            Arrow arrow = id == ItemIds.SpectralArrow
                ? new SpectralArrow { Level = level }
                : new Arrow { Level = level };
            arrow.EntityId = Server.GetNextEntityId();
            arrow.Type = id == ItemIds.SpectralArrow ? EntityType.SpectralArrow : EntityType.Arrow;
            arrow.Position = this.Position;
            arrow.Motion = motion;
            arrow.Owner = this;
            arrow.PickupItem = new ItemStack(stack, stack.Count);
            arrow.Pickup = ArrowPickup.Allowed;
            if (id == ItemIds.SpectralArrow)
            {
                arrow.Effect = (int)PotionEffect.Glowing - 1;
                arrow.EffectDuration = 200;
            }

            return arrow;
        }

        if (id is ItemIds.SplashPotion or ItemIds.LingeringPotion or ItemIds.FireCharge or ItemIds.Snowball)
        {
            var type = id switch
            {
                ItemIds.SplashPotion => EntityType.SplashPotion,
                ItemIds.LingeringPotion => EntityType.LingeringPotion,
                ItemIds.FireCharge => EntityType.SmallFireball,
                _ => EntityType.Snowball
            };
            var contents = stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
            return new MobProjectile(level, type, this.Position, motion)
            {
                Owner = this,
                SplashEffects = [.. ArrowPotionEffects.Get(contents)],
                SplashItem = new ItemStack(stack, stack.Count)
            };
        }

        if (id == ItemIds.WindCharge)
            return BreezeWindCharge.FromOminous(this, motion);

        if (id == ItemIds.ExperienceBottle)
        {
            return new ThrownExperienceBottle
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = this.Position,
                Motion = motion,
                Item = new ItemStack(stack, stack.Count),
                Owner = this
            };
        }

        if (id is ItemIds.Egg or ItemIds.BlueEgg or ItemIds.BrownEgg)
        {
            return new Egg
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = this.Position,
                Motion = motion,
                Item = new ItemStack(stack, stack.Count),
                Owner = this
            };
        }

        if (id == ItemIds.FireworkRocket)
        {
            level.BroadcastLevelEvent(1004, (Vector)this.Position.Floor(), 0);
            return new FireworkRocket
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = this.Position,
                Motion = ReleaseMotion(random, 0.5f, 1f),
                Item = new ItemStack(stack, stack.Count),
                ShotAtAngle = true
            };
        }

        return LooseItem(level, this.Position, stack);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.SetOrRemove("item", this.Item is { IsAir: false, Count: > 0 } item ? item.ToNbt("item") : null);
        tag.Set(new NbtTag<long>("spawn_item_after_ticks", this.releaseAfter));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.Item = tag.TryGetTag<NbtCompound>("item", out var item) ? item.ItemFromNbt() : null;
        this.releaseAfter = tag.TryGetNumber<long>("spawn_item_after_ticks", out var delay) ? delay : 0;
        this.age = 0;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(Item);
    }
}
