using Obsidian.API.Entities;
using Obsidian.API.Loot;
using Obsidian.Entities.AI;
using Obsidian.Plugins;

namespace Obsidian.Entities;

/// <summary>
/// A mob of a plugin's entity type: it moves, falls and takes damage like a vanilla mob, runs the goals it's given, and
/// calls its type's behaviour each tick and when used.
/// </summary>
/// <param name="definition">The mob's entity type.</param>
internal sealed class PluginMob(EntityDefinition definition) : PathfinderMob
{
    // The last damage the mob was about to take and the tick it came, which its death names as its cause.
    private (DamageSource Source, long Tick)? lastDamage;

    // Whether riding paused the mob's goals, which it resumes when its rider leaves.
    private bool riddenPaused;

    public override EntityDefinition Definition => definition;

    protected override bool UsesAi => true;

    // Like an animal, it stays until it's killed: the plugin decides when it goes.
    protected override bool CanDespawn => false;

    // Read from the type, whose size a reload may change.
    public override EntityDimension Dimension
    {
        get => new() { Width = definition.Settings.Width, Height = definition.Settings.Height };
        protected set { }
    }

    // A mob's default attributes, like vanilla's Mob.createMobAttributes, with the type's health and speeds. Set
    // before the base constructors run, which may read them.
    protected override ConcurrentDictionary<string, float> Attributes { get; } = new(new Dictionary<string, float>
    {
        ["minecraft:armor"] = 0,
        ["minecraft:armor_toughness"] = 0,
        ["minecraft:attack_knockback"] = 0,
        ["minecraft:burning_time"] = 1,
        ["minecraft:explosion_knockback_resistance"] = 0,
        ["minecraft:fall_damage_multiplier"] = 1,
        ["minecraft:flying_speed"] = definition.Settings.FlyingSpeed,
        ["minecraft:follow_range"] = 16,
        ["minecraft:gravity"] = 0.08f,
        ["minecraft:jump_strength"] = 0.42f,
        ["minecraft:knockback_resistance"] = 0,
        ["minecraft:max_absorption"] = 0,
        ["minecraft:max_health"] = definition.Settings.MaxHealth,
        ["minecraft:movement_efficiency"] = 0,
        ["minecraft:movement_speed"] = definition.Settings.MovementSpeed,
        ["minecraft:oxygen_bonus"] = 0,
        ["minecraft:safe_fall_distance"] = 3,
        ["minecraft:scale"] = 1,
        ["minecraft:step_height"] = 0.6f,
        ["minecraft:water_movement_efficiency"] = 0
    });

    private bool Flying => definition.Settings.Movement == EntityMovementMode.Flying;

    // A flying mob paths through the air with the flying move control, like vanilla's flying mobs.
    internal override bool FlyingNavigation => this.Flying;

    protected override bool TakesFallDamage => !this.Flying;

    // A seat per passenger attachment, at least one.
    protected override int MaximumPassengers => Math.Max(1, definition.Settings.PassengerAttachments.Length);

    // At the type's passenger attachment, less a player's vehicle attachment (0.6 blocks up from its feet), as vanilla
    // clients place riders; passengers of mobs are players.
    protected override VectorD PassengerOffset(int index)
    {
        var seat = definition.Settings.PassengerAttachment(index, this.Yaw.Degrees);

        return new VectorD(seat.X, seat.Y - 0.6, seat.Z);
    }

    protected override VectorD Travel() => this.Flying ? VolumeMovement.Travel(this, true) : base.Travel();

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        await this.RunBehaviorAsync(behavior => behavior.TickAsync(this));

        if (!this.DataChanged)
            return;

        this.DataChanged = false;
        this.SynchronizeMetadata();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        this.WritePluginData(writer);
    }

    internal override async ValueTask<bool> InteractAsync(IPlayer player, InteractionHand hand)
    {
        var used = false;
        var mount = false;

        await this.RunBehaviorAsync(async behavior =>
        {
            used = await behavior.InteractAsync(this, player, hand);
            mount = !used && !player.Sneaking && behavior.CanMount(this, player);
        });

        if (mount && player is Player rider && this.Mount(rider))
            used = true;

        return used;
    }

    // The first passenger steers, like a vanilla mount's controlling passenger: a walking mob like a horse, from
    // the rider's movement keys and jump, and a flying one like a happy ghast, towards where the rider looks, up
    // with jump. Its goals pause meanwhile.
    protected override void TickRidden()
    {
        base.TickRidden();

        if (this.Rider is not Player rider || this.MobBitMask.HasFlag(MobBitmask.NoAi))
        {
            if (this.riddenPaused && this.GoalController is GoalSelector { IsPaused: true } goals)
                goals.Resume();

            this.riddenPaused = false;
            return;
        }

        if (!this.riddenPaused && this.GoalController is GoalSelector { IsPaused: false } controller)
        {
            controller.Pause();
            (this.Navigator as Navigator)?.Stop();
            this.riddenPaused = true;
        }

        var input = rider.Input;
        this.Yaw = rider.Yaw;

        if (this.Flying)
        {
            this.Pitch = rider.Pitch;

            var forward = (input.HasFlag(PlayerInput.Forward) ? 1 : 0)
                - (input.HasFlag(PlayerInput.Backward) ? 0.5 : 0);
            var sideways = (input.HasFlag(PlayerInput.Left) ? 1 : 0)
                - (input.HasFlag(PlayerInput.Right) ? 1 : 0);
            var yaw = this.Yaw.Degrees * Math.PI / 180;
            var lift = input.HasFlag(PlayerInput.Jump) ? 0.5 : 0;
            var direction = (VectorD)rider.GetLookDirection() * forward
                + new VectorD(Math.Cos(yaw) * sideways, lift, Math.Sin(yaw) * sideways);

            if (direction.MagnitudeSquared() > 1)
                direction /= direction.Magnitude;

            this.MoveControl.Acceleration = direction * (this.GetAttributeValue("minecraft:flying_speed") * 0.1);
            return;
        }

        this.Pitch = rider.Pitch.Degrees * 0.5f;

        var ahead = (input.HasFlag(PlayerInput.Forward) ? 1f : 0) - (input.HasFlag(PlayerInput.Backward) ? 1f : 0);
        var side = (input.HasFlag(PlayerInput.Left) ? 0.5f : 0) - (input.HasFlag(PlayerInput.Right) ? 0.5f : 0);
        this.MoveControl.Ride(this.MovementSpeed, ahead < 0 ? ahead * 0.25f : ahead, side);

        if (input.HasFlag(PlayerInput.Jump))
            this.JumpControl.Jump();
    }

    private protected override async ValueTask<float?> FilterIncomingDamageAsync(
        IEntity source,
        float amount,
        CombatDamageKind kind)
    {
        var damage = new EntityDamage
        {
            Source = this.SourceOf(source, kind),
            Amount = amount
        };

        this.lastDamage = (damage.Source, this.AiTick);
        await this.RunBehaviorAsync(behavior => behavior.HurtAsync(this, damage));

        return damage.Cancelled ? null : damage.Amount;
    }

    protected override async ValueTask OnDeathAsync(IEntity source)
    {
        // Killed by damage this tick, or else killed outright, like vanilla's /kill.
        var cause = this.lastDamage is var (damage, tick) && tick == this.AiTick
            ? damage
            : new DamageSource("minecraft:generic_kill", null, null);

        await this.RunBehaviorAsync(behavior => behavior.DiedAsync(this, cause));
        this.DropLoot(cause);
    }

    // Like vanilla's LivingEntity.dropFromLootTable: the type's entities/<path> table in its namespace, with the
    // attacker, the player who hurt it lately (whose luck counts) and the weapon that killed it, which looting reads.
    private void DropLoot(DamageSource cause)
    {
        if (!this.Level.LevelData.GetBooleanRule("mob_drops"))
            return;

        var separator = definition.Identifier.IndexOf(':');
        var identifier = $"{definition.Identifier[..separator]}:entities/{definition.Identifier[(separator + 1)..]}";
        var resolve = PluginLootTables.CaptureResolver();

        if (resolve(identifier) is not LootTable table)
            return;

        var player = this.RecentPlayerAttacker;
        var context = new LootContext
        {
            Random = table.CreateRandom(this.Random.NextInt64()),
            Luck = player?.GetAttributeValue("minecraft:luck") ?? 0,
            Origin = this.Position,
            ThisEntity = this,
            AttackingEntity = cause.Attacker,
            LastDamagePlayer = player,
            AttackerWeapon = this.IncomingWeapon,
            ResolveTable = resolve
        };

        foreach (var item in table.GetRandomItems(context))
            this.DropItem(item);
    }

    // The damage type vanilla would give damage of this kind from this source.
    private DamageSource SourceOf(IEntity source, CombatDamageKind kind)
    {
        var attacker = ReferenceEquals(source, this) ? null : source;
        var direct = this.IncomingDirectSource ?? attacker;
        var type = kind switch
        {
            CombatDamageKind.Melee when attacker is IPlayer => "minecraft:player_attack",
            CombatDamageKind.Melee when attacker is not null => "minecraft:mob_attack",
            CombatDamageKind.Melee => "minecraft:generic",
            CombatDamageKind.Projectile => direct is Arrow ? "minecraft:arrow" : "minecraft:thrown",
            CombatDamageKind.Fall => "minecraft:fall",
            CombatDamageKind.Fire => "minecraft:on_fire",
            CombatDamageKind.FireContact => "minecraft:in_fire",
            CombatDamageKind.Explosion when attacker is IPlayer => "minecraft:player_explosion",
            CombatDamageKind.Explosion => "minecraft:explosion",
            CombatDamageKind.Starvation => "minecraft:starve",
            CombatDamageKind.Void => "minecraft:out_of_world",
            CombatDamageKind.SonicBoom => "minecraft:sonic_boom",
            CombatDamageKind.Suffocation => "minecraft:in_wall",
            CombatDamageKind.Freezing => "minecraft:freeze",
            CombatDamageKind.Thorns => "minecraft:thorns",
            _ => "minecraft:magic"
        };

        return new DamageSource(type, attacker, direct);
    }

    // Runs the type's behaviour as a call into its plugin, so that the plugin can't be released meanwhile.
    private async ValueTask RunBehaviorAsync(Func<IEntityBehavior, ValueTask> run)
    {
        if (definition.Binding is not { Behavior: IEntityBehavior behavior, Owner: PluginContainer plugin })
            return;

        using var call = plugin.TryEnter();
        if (call is null)
            return;

        await run(behavior);
    }
}
