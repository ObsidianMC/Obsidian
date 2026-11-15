namespace Obsidian.API;

/// <summary>State flags living entities send in their metadata about the item they are using.</summary>
[Flags]
public enum LivingBitMask : byte
{
    /// <summary>No flags set.</summary>
    None = 0x00,

    /// <summary>The entity is using the item in its hand, such as eating or drawing a bow.</summary>
    HandActive = 0x01,
    /// <summary>The item in use is in the off hand; when clear, it is in the main hand.</summary>
    ActiveHand = 0x02,
    /// <summary>The entity is spinning from a riptide trident.</summary>
    InRiptideSpinAttack = 0x04
}
