using Obsidian.Nbt.Interfaces;

namespace Obsidian.API;

/// <summary>
/// An entry of a registry the server sends to clients during configuration, like a biome or a dimension type.
/// </summary>
public interface ICodec
{
    /// <summary>
    /// Gets the entry's identifier, like <c>minecraft:plains</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the entry's numeric id in its registry.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Writes the entry's data as the fields of an NBT compound, which the caller opens and closes.
    /// </summary>
    /// <param name="writer">The writer to write to.</param>
    public void WriteElement(INbtWriter writer);
}

