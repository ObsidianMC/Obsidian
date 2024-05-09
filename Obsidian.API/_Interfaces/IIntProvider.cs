namespace Obsidian.API;
public interface IIntProvider : IRegistryResource
{
    /// <summary>
    /// Can be constant, uniform, biased_to_bottom, clamped, clamped_normal, or weighted_list
    /// </summary>
    /// <remarks>
    /// See <see cref="IntProviderTypes"/> for the default types.
    /// </remarks>
    public new string Type { get; }

    public int Get();
}

public static class IntProviderTypes
{
    public const string Constant = "minecraft:constant";
    public const string Uniform = "minecraft:uniform";
    public const string BiasedToBottom = "minecraft:biased_to_bottom";
    public const string Clamped = "minecraft:clamped";
    public const string ClampedNormal = "minecraft:clamped_normal";
    public const string WeightedList = "minecraft:weighted_list";
}
