namespace Obsidian.API;
public interface IIntProvider
{
    public IntProviderType ProviderType { get; }
}

//constant, uniform, biased_to_bottom, clamped, clamped_normal, or weighted_list
public enum IntProviderType
{
    Constant,
    Uniform,
    BiasedToBottom,
    Clamped,
    ClampedNormal,
    WeightedList
}
