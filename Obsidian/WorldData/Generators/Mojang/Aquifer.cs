namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Decides which fluid (or air) fills a non-solid position during terrain fill.
/// </summary>
internal interface IAquifer
{
    /// <summary>
    /// Gets the block for a position with the given final density, or <c>null</c> to keep it solid
    /// (which lets ore veins or the default block fill it).
    /// </summary>
    public IBlock? ComputeSubstance(int x, int y, int z, double density);

    /// <summary>
    /// Whether the last <see cref="ComputeSubstance"/> call produced a fluid that needs a tick to settle.
    /// </summary>
    public bool ShouldScheduleFluidUpdate { get; }

    /// <summary>
    /// Clears <see cref="ShouldScheduleFluidUpdate"/>, as if no position had been computed yet.
    /// </summary>
    public void ResetFluidUpdate();

    /// <summary>
    /// Above this Y, <see cref="ComputeSubstance"/> gives non-solid positions the global fluid picker's block and schedules
    /// nothing.
    /// </summary>
    public int GlobalFluidAboveY { get; }
}

/// <summary>
/// A fluid surface: <see cref="FluidType"/> below <see cref="FluidLevel"/>, air at or above it.
/// </summary>
internal readonly record struct FluidStatus(int FluidLevel, IBlock FluidType)
{
    private readonly Material fluidMaterial = FluidType.Material;

    public IBlock At(int y) => y < this.FluidLevel ? this.FluidType : BlocksRegistry.Air;

    /// <summary>
    /// The material of <see cref="At"/>, without the interface call.
    /// </summary>
    public Material MaterialAt(int y) => y < this.FluidLevel ? this.fluidMaterial : Material.Air;

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

    /// <summary>
    /// Creates the aquifer for a chunk, or a sea-level-only aquifer when the settings disable aquifers.
    /// </summary>
    public static IAquifer Create(NoiseChunk noiseChunk, int chunkX, int chunkZ, FluidPicker fluidPicker) =>
        noiseChunk.RandomState.Settings.AquifersEnabled
            ? new NoiseBasedAquifer(noiseChunk, chunkX, chunkZ, fluidPicker)
            : new DisabledAquifer(fluidPicker);

    private sealed class DisabledAquifer : IAquifer
    {
        private readonly FluidPicker fluidPicker;

        public bool ShouldScheduleFluidUpdate => false;

        public int GlobalFluidAboveY => int.MinValue;

        public DisabledAquifer(FluidPicker fluidPicker) => this.fluidPicker = fluidPicker;

        public void ResetFluidUpdate()
        {
        }

        public IBlock? ComputeSubstance(int x, int y, int z, double density) =>
            density > 0.0 ? null : this.fluidPicker(x, y, z).At(y);
    }
}
