using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Living
{
    // Vanilla's default swing, for items without an attack_animation component: a whack over 6 ticks.
    private static readonly SwingAnimation defaultSwing = new() { Type = SwingAnimationType.Whack, Duration = 6 };

    // The swing under way, if any, its length in ticks after mining effects and the level time it started at.
    private int swingDuration;
    private long swingStart = -1;

    /// <summary>
    /// Swings the arm in <paramref name="hand"/> for an attack, with the held item's attack animation, and shows the
    /// swing to the players around, but not to this entity's own client, which swung first.
    /// </summary>
    /// <returns>Whether the swing started: a swing past its first half, or none at all, can be cut short.</returns>
    internal bool SwingForAttack(InteractionHand hand)
    {
        var animation = AttackAnimation(this switch
        {
            PlayerBase player => hand == InteractionHand.MainHand ? player.GetHeldItem() : player.GetOffHandItem(),
            Mob mob => mob.GetEquipment(hand == InteractionHand.MainHand ? EquipmentSlot.MainHand : EquipmentSlot.OffHand),
            _ => null
        });

        // Vanilla's SwingState.startIfAble: a new swing replaces one that has run past half its length, or one that
        // started this tick.
        var now = this.Level.LevelData.Time;
        var elapsed = now - this.swingStart;
        if (this.swingStart >= 0 && elapsed > 0 && elapsed <= this.swingDuration / 2)
            return false;

        this.swingStart = now;
        this.swingDuration = this.SwingDuration(animation.Duration);
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new SwingAnimationPacket
        {
            EntityId = this.EntityId,
            Hand = hand,
            Animation = animation
        }, this.EntityId);
        return true;
    }

    private static SwingAnimation AttackAnimation(ItemStack? item)
    {
        var component = CombatItems.Component(item, "attack_animation");
        if (component.ValueKind != System.Text.Json.JsonValueKind.Object)
            return defaultSwing;

        return new SwingAnimation
        {
            Type = component.TryGetProperty("type", out var type) && type.GetString() is { } name
                ? Enum.Parse<SwingAnimationType>(name, ignoreCase: true)
                : defaultSwing.Type,
            Duration = component.TryGetProperty("duration", out var duration) ? duration.GetInt32() : defaultSwing.Duration
        };
    }

    // Vanilla's getModifiedSwingDuration: haste or conduit power shortens a swing, mining fatigue lengthens it.
    private int SwingDuration(int duration)
    {
        var haste = Math.Max(this.EffectAmplifier(PotionEffect.Haste), this.EffectAmplifier(PotionEffect.ConduitPower));
        if (haste >= 0)
            return duration - (1 + haste);

        var fatigue = this.EffectAmplifier(PotionEffect.MiningFatigue);
        return fatigue >= 0 ? duration + (1 + fatigue) * 2 : duration;
    }

    private int EffectAmplifier(PotionEffect effect) =>
        this.ActivePotionEffects.TryGetValue((int)effect - 1, out var active) ? active.EffectData.Amplifier : -1;
}
