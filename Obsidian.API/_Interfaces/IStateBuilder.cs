namespace Obsidian.API;
/// <summary>
/// Builds a block state from property values set on the builder.
/// </summary>
/// <typeparam name="TState">The block state type built.</typeparam>
public interface IStateBuilder<TState> where TState : IBlockState
{
    /// <summary>
    /// Creates the state with the builder's property values.
    /// </summary>
    public TState Build();
}
