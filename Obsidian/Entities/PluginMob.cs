using Obsidian.API.Entities;
using Obsidian.Plugins;

namespace Obsidian.Entities;

/// <summary>
/// A mob of a plugin's entity type: it moves, falls and takes damage like a vanilla mob, runs the goals it's given, and
/// calls its type's behaviour each tick and when used.
/// </summary>
/// <param name="definition">The mob's entity type.</param>
internal sealed class PluginMob(EntityDefinition definition) : PathfinderMob
{
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

    // A mob's default attributes, like vanilla's Mob.createMobAttributes, with the type's health and speed. Set before
    // the base constructors run, which may read them.
    protected override ConcurrentDictionary<string, float> Attributes { get; } = new(new Dictionary<string, float>
    {
        ["minecraft:armor"] = 0,
        ["minecraft:armor_toughness"] = 0,
        ["minecraft:attack_knockback"] = 0,
        ["minecraft:burning_time"] = 1,
        ["minecraft:explosion_knockback_resistance"] = 0,
        ["minecraft:fall_damage_multiplier"] = 1,
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

    // At the type's passenger attachment, less a player's vehicle attachment (0.6 blocks up from its feet), as vanilla
    // clients place riders; passengers of mobs are players.
    protected override VectorD PassengerOffset(int index)
    {
        var seat = definition.Settings.PassengerAttachment(index, this.Yaw.Degrees);

        return new VectorD(seat.X, seat.Y - 0.6, seat.Z);
    }

    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        await this.RunBehaviorAsync(behavior => behavior.TickAsync(this));
    }

    internal override async ValueTask<bool> InteractAsync(IPlayer player, InteractionHand hand)
    {
        var used = false;
        await this.RunBehaviorAsync(async behavior => used = await behavior.InteractAsync(this, player, hand));
        return used;
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
