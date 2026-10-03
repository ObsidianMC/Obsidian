namespace Obsidian.API;

/// <summary>
/// Transforms a density function tree through <see cref="IDensityFunction.MapAll"/>.
/// </summary>
public interface IDensityFunctionVisitor
{
    /// <summary>
    /// Called for every function in the tree after its children have been mapped.
    /// Return the function unchanged to keep it.
    /// </summary>
    public IDensityFunction Apply(IDensityFunction function);

    /// <summary>
    /// Maps a child function. Override to memoize so subtrees shared in the input stay shared in the output.
    /// </summary>
    public IDensityFunction Map(IDensityFunction function) => function.MapAll(this);

    /// <summary>
    /// Called for every noise referenced by a function in the tree.
    /// </summary>
    public INoise VisitNoise(INoise noise) => noise;
}
