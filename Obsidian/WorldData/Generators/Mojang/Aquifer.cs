namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// A fluid surface: <see cref="FluidType"/> below <see cref="FluidLevel"/>, air at or above it.
/// </summary>
internal readonly record struct FluidStatus(int FluidLevel, IBlock FluidType)
{
    private readonly int fluidId = FluidType.RegistryId;

    public IBlock At(int y) => y < this.FluidLevel ? this.FluidType : BlocksRegistry.Air;

    /// <summary>
    /// The block id of <see cref="At"/>, without the interface call.
    /// </summary>
    public int BlockIdAt(int y) => y < this.FluidLevel ? this.fluidId : BlockIds.Air;

    // Blocks are boxed structs, whose default equality compares fields through reflection; the fluid types are
    // usually the very same instance.
    public bool Equals(FluidStatus other) => this.FluidLevel == other.FluidLevel
        && (ReferenceEquals(this.FluidType, other.FluidType) || this.FluidType.Equals(other.FluidType));

    public override int GetHashCode() => HashCode.Combine(this.FluidLevel, this.FluidType);
}

/// <summary>
/// Default fluid surface for a position when no local aquifer applies.
/// </summary>
internal delegate FluidStatus FluidPicker(int x, int y, int z);

internal static class Aquifers
{
    /// <summary>
    /// Vanilla's global fluid picker: lava below y = -54 (or sea level, if lower), the default fluid up to sea level.
    /// </summary>
    public static FluidPicker CreateGlobalFluidPicker(int seaLevel, IBlock defaultFluid)
    {
        var lava = new FluidStatus(-54, BlocksRegistry.Lava);
        var sea = new FluidStatus(seaLevel, defaultFluid);
        var lavaCeiling = Math.Min(-54, seaLevel);

        return (_, y, _) => y < lavaCeiling ? lava : sea;
    }
}
