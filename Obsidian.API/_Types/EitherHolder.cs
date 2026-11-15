namespace Obsidian.API;
/// <summary>Holds one of two kinds of value, such as a registry id or an inline definition.</summary>
/// <typeparam name="TValueA">The type of <see cref="Left"/>.</typeparam>
/// <typeparam name="TValueB">The type of <see cref="Right"/>.</typeparam>
public readonly record struct EitherHolder<TValueA, TValueB>
{
    /// <summary>Gets the first kind of value, or the default when the holder holds the second.</summary>
    public TValueA? Left { get; init; }

    /// <summary>Gets the second kind of value, or the default when the holder holds the first.</summary>
    public TValueB? Right { get; init; }

    /// <summary>Creates a holder of the first kind of value.</summary>
    public EitherHolder(TValueA? left)
    {
        this.Left = left;
    }

    /// <summary>Creates a holder of the second kind of value.</summary>
    public EitherHolder(TValueB right)
    {
        this.Right = right;
    }
}
