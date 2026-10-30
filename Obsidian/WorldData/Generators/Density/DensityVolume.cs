namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// A grid of block positions a density function samples at once: <c>Size</c> points per axis from <c>MinBlock</c>,
/// <c>Step</c> blocks apart. Values are stored y fastest, then x, then z, like vanilla's DensityVolume.
/// </summary>
internal readonly record struct DensityVolume(
    int SizeX,
    int SizeY,
    int SizeZ,
    int MinBlockX,
    int MinBlockY,
    int MinBlockZ,
    int StepX = 1,
    int StepY = 1,
    int StepZ = 1)
{
    public int Size => this.SizeX * this.SizeY * this.SizeZ;

    public int Index(int indexX, int indexY, int indexZ) => indexY + (indexX + indexZ * this.SizeX) * this.SizeY;

    public int BlockX(int indexX) => this.MinBlockX + indexX * this.StepX;

    public int BlockY(int indexY) => this.MinBlockY + indexY * this.StepY;

    public int BlockZ(int indexZ) => this.MinBlockZ + indexZ * this.StepZ;
}
