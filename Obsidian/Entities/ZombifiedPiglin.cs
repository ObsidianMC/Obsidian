using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:zombified_piglin")]
public sealed partial class ZombifiedPiglin : Zombie
{
    internal int AngerTicks { get; set; }
    internal Guid AngryAt { get; set; }
    private long nextAlert;
    private long angrySoundTick;
    public ZombifiedPiglin() => Type = EntityType.ZombifiedPiglin;
    internal override bool Hostile => true;
    protected override string? SoundName => "zombified_piglin";
    internal override float MovementSpeed => base.MovementSpeed + (AngerTicks > 0 && !IsBaby ? 0.05f : 0);

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(2, new MeleeAttackGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(8, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, target => AngerTicks > 0 && target.Uuid == AngryAt));
    }

    protected override void FinalizeSpawn()
    {
        CanPickUpLoot = Random.NextSingle() < 0.55f * SpecialDifficulty;
        IsBaby |= Random.NextSingle() < 0.05f;
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(ItemsRegistry.GoldenSword));
    }

    // Vanilla's ZombifiedPiglin.populateDefaultEquipmentSlots, from the level's random: no armour, and a golden
    // spear one time in 20, else a golden sword. A portal's piglin, or a spawner's bare one, is otherwise set up as
    // every zombie is (see Zombie.FinalizeSpawnFromLevel).
    private protected override void PopulateDefaultEquipmentFromLevel(IRandomSource random, float special)
    {
        var weapon = random.NextInt(20) == 0 ? ItemsRegistry.GoldenSpear : ItemsRegistry.GoldenSword;
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(weapon));
    }

    // Zombie.handleAttributes from the mob's own random, where a zombified piglin never calls reinforcements.
    private protected override void HandleSpawnAttributes(float special)
    {
        ReinforcementChance = 0;
        TryUpdateAttribute("minecraft:generic.knockback_resistance", Random.NextSingle() * 0.05f);
        var rangeBonus = Random.NextDouble() * 1.5 * special;
        if (rangeBonus > 1)
            TryUpdateAttribute("minecraft:generic.follow_range", (float)(FollowRange * (1 + rangeBonus)));

        if (Random.NextSingle() < special * 0.05f)
        {
            var health = GetAttributeValue("minecraft:generic.max_health") * (Random.NextSingle() * 3 + 2);
            TryUpdateAttribute("minecraft:generic.max_health", health);
            Health = health;
            CanBreakDoors = true;
        }
    }

    /// <summary>
    /// The ambient sound vanilla's <c>EntityType.spawn</c> plays for a new mob: at its position, with its voice's
    /// pitch.
    /// </summary>
    internal void PlaySpawnAmbientSound(AbstractLevel level)
    {
        var voice = IsBaby ? 1.5f : 1f;
        var pitch = (Random.NextSingle() - Random.NextSingle()) * 0.2f + voice;
        LevelSounds.Play(level, Position, "minecraft:entity.zombified_piglin.ambient",
            SoundCategory.Hostile, pitch: pitch);
    }

    private void BecomeAngry(IEntity source)
    {
        if (!IsValidTarget(source) || source.Type == Type) return;
        if (AngerTicks == 0) angrySoundTick = AiTick + Random.Next(21);
        AngerTicks = Random.Next(400, 781);
        AngryAt = source.Uuid;
        AttackTarget = source;
    }

    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (!ReferenceEquals(source, this)) BecomeAngry(source);
        return default;
    }

    protected override ValueTask TickMobAsync()
    {
        if (AngerTicks <= 0) return default;
        if (AttackTarget != null && (!IsValidTarget(AttackTarget) || !IsInRange(AttackTarget, FollowRange))) AttackTarget = null;
        if (AttackTarget is not IPlayer && --AngerTicks == 0)
        {
            AngryAt = Guid.Empty;
            AttackTarget = null;
            SetAggressive(false);
            return default;
    }
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return default;
        if (AiTick >= angrySoundTick)
        {
            PlayMobSound("angry");
            angrySoundTick = long.MaxValue;
        }
        if (AttackTarget is { } target && AiTick >= nextAlert)
        {
            nextAlert = AiTick + Random.Next(80, 121);
            foreach (var ally in GetEntitiesNear(FollowRange).OfType<ZombifiedPiglin>().Where(ally => ally.Alive && !ally.IsRemoved &&
                ally.AttackTarget == null && Math.Abs(ally.Position.Y - Position.Y) <= 10 && ally.CanSee(target)))
            {
                ally.BecomeAngry(target);
            }
        }
        return default;
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        this.DropVanillaLoot(source);
        foreach (var slot in Enum.GetValues<Obsidian.API.Inventory.EquipmentSlot>())
        {
            var item = this.GetEquipment(slot);
            if (!item.IsAir && this.Random.NextSingle() < this.GetEquipmentDropChance(slot))
                this.DropItem(item);
        }

        return default;
    }
}
