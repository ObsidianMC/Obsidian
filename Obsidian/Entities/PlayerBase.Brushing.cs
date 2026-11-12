using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// Brushing with a brush (vanilla's <c>BrushItem</c>): a held use, started on a block, that strokes every ten ticks
/// at the block the player looks at. A stroke on suspicious sand or gravel counts towards uncovering its item.
/// </summary>
public partial class PlayerBase
{
    // Vanilla's BrushItem: how long one use lasts, and the stroke's place in its ten-tick swing.
    internal const int BrushUseTicks = 200;
    private const int BrushStrokeCycle = 10;
    private const int BrushStrokeTick = 5;

    // Vanilla's BrushItem.useOn: the use starts when the player's own view finds a block.
    internal void StartBrushing(InteractionHand hand)
    {
        if (!Alive || Respawning || GameMode == GameMode.Spectator || usingWeapon != null || eatingItem != null)
            return;

        var slot = hand == InteractionHand.OffHand ? 45 : CurrentHeldItemSlot;
        if (Inventory.GetItem(slot) is not { Count: > 0 } item || !item.Is(ItemsRegistry.Brush))
            return;

        if (this.BrushTarget() is null)
            return;

        usingWeapon = item;
        weaponSlot = slot;
        weaponTicks = 0;
        LivingBitMask |= LivingBitMask.HandActive;
        if (hand == InteractionHand.OffHand)
            LivingBitMask |= LivingBitMask.ActiveHand;
        else
            LivingBitMask &= ~LivingBitMask.ActiveHand;

        SyncFoodMetadata();

        // Vanilla's startUsingItem: the brush's use_effects let its use be heard.
        if (Level is AbstractLevel level)
            level.EmitGameEvent(MobGameEvent.ItemInteractStart, Position, this);
    }

    /// <summary>
    /// Vanilla's <c>ProjectileUtil.getHitResultOnViewVector</c> over collision shapes, as the brush aims: the block
    /// face the player's view meets within their block reach, or null. Entities in the way aren't checked.
    /// </summary>
    internal BlockShapeHit? BrushTarget()
    {
        var eye = EyePosition(this);
        var reach = (VectorD)GetLookDirection() * BlockInteractionRange(this);
        return new MobTerrain(Level).RaycastCollisions(eye, eye + reach);
    }

    // A brush that broke on its last stroke leaves its slot empty, and vanilla's updatingUsingItem takes the empty hand
    // for the same (empty) item: the use goes on, doing nothing, until its duration runs out or the slot fills.
    private bool IsSpentBrush(ItemStack item) =>
        item.Count <= 0 && item.Is(ItemsRegistry.Brush) && Inventory.GetItem(weaponSlot) is not { Count: > 0 };

    private ValueTask TickSpentBrushAsync()
    {
        var held = weaponSlot == 45 || weaponSlot == CurrentHeldItemSlot;
        weaponTicks++;
        if (!Alive || Respawning || GameMode == GameMode.Spectator || !held || weaponTicks >= BrushUseTicks)
            CancelWeaponUse();

        return ValueTask.CompletedTask;
    }

    // Vanilla's BrushItem.onUseTick, then the use's countdown: a player who no longer looks at a block lets go, every
    // tenth tick from the fifth strokes, and the use ends after its duration.
    private async ValueTask TickBrushingAsync(ItemStack brush)
    {
        weaponTicks++;
        if (this.BrushTarget() is not BlockShapeHit hit)
        {
            CancelWeaponUse();
            return;
        }

        if (weaponTicks % BrushStrokeCycle == BrushStrokeTick && Level is AbstractLevel level)
            await Brushing.StrokeAsync(level, this, brush, weaponSlot, hit);

        if (weaponTicks >= BrushUseTicks)
            CancelWeaponUse();
    }
}
