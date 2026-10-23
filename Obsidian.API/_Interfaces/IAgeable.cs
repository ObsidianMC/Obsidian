namespace Obsidian.API;

/// <summary>
/// A mob that can be a baby, like vanilla's <c>isBaby</c>. Animals and villagers grow up;
/// zombies, piglins and zoglins stay babies.
/// </summary>
public interface IAgeable : IEntity
{
    /// <summary>Whether the mob is a baby. Mobs that can never be babies ignore the setter.</summary>
    public bool IsBaby { get; set; }
}
