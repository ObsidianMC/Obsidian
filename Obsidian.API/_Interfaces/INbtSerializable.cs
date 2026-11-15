using Obsidian.Nbt.Interfaces;

namespace Obsidian.API;
/// <summary>
/// An object that can write itself as NBT.
/// </summary>
public interface INbtSerializable
{
    /// <summary>
    /// Writes the object to <paramref name="writer"/>.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    public void Write(INbtWriter writer);
}
