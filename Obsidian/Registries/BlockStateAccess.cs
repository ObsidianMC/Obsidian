using Obsidian.API.World.Features.Trees;

namespace Obsidian.Registries;

/// <summary>
/// Gives the API's tree parts the server's block state properties and fluids (see <see cref="TreeBlockRules"/>).
/// </summary>
internal sealed class BlockStateAccess : IBlockStateAccess
{
    public string? GetProperty(IBlock block, string property) => block.GetProperty(property);

    public IBlock WithProperty(IBlock block, string property, string value) => block.WithProperty(property, value);

    public bool IsWaterSource(IBlock block) => block.GetFluid() == FluidKind.Water;

    public bool IsWater(IBlock block) => block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater;
}
