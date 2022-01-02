namespace Obsidian.API;

[Flags]
public enum LivingBitMask : byte
{
    None = 0x00,

    HandActive = 0x01,
    ActiveHand = 0x02,
    InRiptideSpinAttack = 0x04
}
