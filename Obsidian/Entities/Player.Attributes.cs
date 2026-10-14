using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

// The player's attribute modifiers from what it holds and wears, and its sprinting modifier, like vanilla's
// LivingEntity.collectEquipmentChanges and setSprinting.
public partial class Player
{
    // Vanilla's sprinting modifier on movement speed, which clients add themselves for their own player.
    private const string SprintingModifier = "minecraft:sprinting";

    // The equipment slots whose items' modifiers apply, with their inventory slots (-1 for the held item's slot).
    private static readonly (string Slot, int Inventory)[] attributeSlots =
    [
        ("mainhand", -1),
        ("offhand", 45),
        ("feet", 8),
        ("legs", 7),
        ("chest", 6),
        ("head", 5)
    ];

    // Per equipment slot: the stack whose modifiers are applied, and the attributes and modifier ids it added.
    private readonly ItemStack?[] equippedStacks = new ItemStack?[attributeSlots.Length];
    private readonly List<(string Attribute, string Id)>[] equippedModifiers =
        [.. attributeSlots.Select(_ => new List<(string, string)>())];

    // Whether the player has been sent all of its attributes, which its client needs once after joining or
    // respawning: other players get them when they start tracking it.
    private bool attributesSentToSelf;

    /// <summary>
    /// The player's maximum health, its <c>minecraft:max_health</c> attribute.
    /// </summary>
    internal float MaxHealth => this.GetAttributeValue("minecraft:max_health");

    /// <summary>
    /// Applies the modifiers of the items in the player's equipment slots that changed since the last time, like
    /// vanilla's <c>collectEquipmentChanges</c>: the old item's modifiers are removed and the new item's added,
    /// replacing modifiers with the same identifiers. Also keeps the sprinting modifier on movement speed.
    /// </summary>
    internal override void UpdateEquipmentAttributes()
    {
        for (var index = 0; index < attributeSlots.Length; index++)
        {
            var (slot, inventorySlot) = attributeSlots[index];
            var stack = inventorySlot < 0 ? this.GetHeldItem() : this.Inventory.GetItem(inventorySlot);
            var current = stack is { Count: > 0 } ? stack : null;

            if (ReferenceEquals(current, this.equippedStacks[index]))
                continue;

            this.equippedStacks[index] = current;

            foreach (var (attribute, id) in this.equippedModifiers[index])
                this.RemoveModifier(attribute, id);

            this.equippedModifiers[index].Clear();

            foreach (var (attribute, modifier) in CombatItems.Modifiers(current, slot))
            {
                this.SetModifier(attribute, modifier);
                this.equippedModifiers[index].Add((attribute, modifier.Id));
            }
        }

        // Vanilla's setSprinting adds 0.3 times the total speed while sprinting.
        if (this.Sprinting != this.HasModifier("minecraft:movement_speed", SprintingModifier))
        {
            if (this.Sprinting)
            {
                var sprinting = new EntityAttributeModifier(SprintingModifier, 0.3, AttributeOperation.MulTotal);
                this.SetModifier("minecraft:movement_speed", sprinting);
            }
            else
            {
                this.RemoveModifier("minecraft:movement_speed", SprintingModifier);
            }
        }

        if (this.attributesSentToSelf)
            return;

        this.attributesSentToSelf = true;

        if (this.CreateAttributesPacket(changedOnly: false) is UpdateAttributesPacket all)
            this.PacketBroadcaster.QueuePacketTo(all, this.EntityId);
    }

    /// <summary>
    /// Sends the player all of its attributes on its next tick, as its client starts over after a respawn.
    /// </summary>
    private void ResetAttributeSync() => this.attributesSentToSelf = false;
}
