namespace Obsidian.API;
public interface IStateBuilder<TState> where TState : IBlockState
{
    public TState Build();
}
