namespace Obsidian.API;
/// <summary>Whether a damage type's damage scales with the world's difficulty.</summary>
public enum DamageScaling
{
    /// <summary>Damage is the same on every difficulty.</summary>
    Never,
    /// <summary>Damage scales only when caused by a living entity that is not a player.</summary>
    WhenCausedByLivingNonPlayer,
    /// <summary>Damage always scales.</summary>
    Always
}
