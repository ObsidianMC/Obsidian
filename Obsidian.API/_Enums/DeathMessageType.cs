namespace Obsidian.API;
/// <summary>How the death message for a damage type is built.</summary>
public enum DeathMessageType
{
    /// <summary>The damage type's usual death message.</summary>
    Default,
    /// <summary>A message that says where the player fell from, such as off a ladder or out of the water.</summary>
    FallVariants,
    /// <summary>The "intentional game design" message, which links to the bug report about beds exploding.</summary>
    IntentionalGameDesign,
}
