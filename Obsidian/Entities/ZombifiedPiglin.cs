using Obsidian.API.Inventory;
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

    /// <summary>
    /// Vanilla's <c>finalizeSpawn</c> for a zombified piglin a portal spawns, which draws from the level's random
    /// (<paramref name="random"/>) rather than the mob's own: the follow range's spawn bonus, left-handedness,
    /// picking up loot, being a baby (and the chicken jockey chances), breaking doors, the golden spear or sword, the
    /// weapon's enchantment chance and, at Halloween, a pumpkin. The draws and their order are vanilla's, because they're part
    /// of the level's random sequence; the attribute bonuses after them come from the mob's own random, as vanilla's
    /// <c>handleAttributes</c> does.
    /// </summary>
    /// <remarks>
    /// Obsidian has no chicken jockeys and doesn't enchant spawned equipment yet: those draws are made and their
    /// outcome left out.
    /// </remarks>
    internal void FinalizeSpawnFromLevel(IRandomSource random)
    {
        var special = SpecialDifficulty;

        // Mob.finalizeSpawn: random.triangle(0, 0.11485) as a multiplier of the base follow range.
        var followBonus = 0.11485000000000001 * (random.NextDouble() - random.NextDouble());
        TryUpdateAttribute("minecraft:generic.follow_range", (float)(FollowRange * (1 + followBonus)));
        if (random.NextFloat() < 0.05f)
            MobBitMask |= MobBitmask.LeftHanded;

        // Zombie.finalizeSpawn.
        CanPickUpLoot = random.NextFloat() < 0.55f * special;
        if (random.NextFloat() < 0.05f)
        {
            IsBaby = true;

            // An existing chicken to ride (none is looked for here), else a new one: Obsidian makes neither.
            if (random.NextFloat() >= 0.05f)
                _ = random.NextFloat() < 0.05f;
        }

        CanBreakDoors = random.NextFloat() < special * 0.1f;

        var weapon = random.NextInt(20) == 0 ? ItemsRegistry.GoldenSpear : ItemsRegistry.GoldenSword;
        SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(weapon));

        // Mob.populateDefaultEquipmentEnchantments: the weapon's chance; the empty armour slots draw nothing.
        _ = random.NextFloat() < 0.25f * special;

        var bareHead = GetEquipment(EquipmentSlot.Helmet).IsAir;
        if (bareHead && MobSpawner.IsHalloween(DateTime.Now) && random.NextFloat() < 0.25f)
        {
            var head = random.NextFloat() < 0.1f ? ItemsRegistry.JackOLantern : ItemsRegistry.CarvedPumpkin;
            SetEquipment(EquipmentSlot.Helmet, ItemsRegistry.GetSingleItem(head));
        }

        // Zombie.handleAttributes, from the mob's own random; a zombified piglin never calls reinforcements.
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
