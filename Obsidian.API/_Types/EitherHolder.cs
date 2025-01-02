namespace Obsidian.API;
public readonly struct EitherHolder<TValueA, TValueB>
{
    public TValueA? Left { get; init; }

    public TValueB? Right { get; init; }

    public EitherHolder(TValueA? left)
    {
        this.Left = left;
    }

    public EitherHolder(TValueB right)
    {
        this.Right = right;
    }
}
