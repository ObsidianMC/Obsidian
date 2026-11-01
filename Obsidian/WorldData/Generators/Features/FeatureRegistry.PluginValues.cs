using Obsidian.WorldData.Generators.Density;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

// The values of 26.3's own features and placement modifiers that a plugin's type can be: int, float and height
// providers, block-state providers, block predicates, rule tests and tree parts.
//
// Vanilla's types compile into delegates as before. Only a type id none of vanilla's readers knows is looked up among
// the types plugins registered, so built-in values never look a type up while generating. A plugin's value is read
// with the public model its type has, like a plugin's feature (see WorldgenReader), while the registry tracks the
// entry being compiled: the plugin becomes an owner of that entry and of every entry using it, which guards their
// placement (failures name the plugin; an unloading plugin's entries place nothing) and is dropped with the registry
// on the next apply.
internal sealed partial class FeatureRegistry
{
    /// <summary>
    /// Reads a value of a type a plugin registered for exactly <typeparamref name="T"/>, where a 26.3 feature takes
    /// one of vanilla's types and <paramref name="type"/> is none of them.
    /// </summary>
    /// <param name="type">The value's <c>type</c> (or <c>predicate_type</c>), as written.</param>
    /// <param name="kind">What the value is, for messages.</param>
    /// <exception cref="InvalidDataException">No plugin registered the type, or the value isn't valid.</exception>
    internal T PluginValue<T>(JsonElement json, string? type, string kind) where T : class
    {
        var identifier = type is null ? null : WorldgenData.Qualify(type);

        if (identifier is null || this.Types.FindRegistered(identifier, typeof(T)) is null)
            throw new InvalidDataException($"Unknown 26.3 {kind} {type}.");

        return this.Reader.Read<T>(json);
    }

    private IntProvider PluginInt(JsonElement json, string? type)
    {
        var provider = this.PluginValue<IIntProvider>(json, type, "int provider");

        return provider.Sample;
    }

    private FloatProvider PluginFloat(JsonElement json, string? type)
    {
        var provider = this.PluginValue<IFloatProvider>(json, type, "float provider");

        return provider.Sample;
    }

    private HeightProvider PluginHeight(JsonElement json, string? type)
    {
        var provider = this.PluginValue<IHeightProvider>(json, type, "height provider");

        return (random, minY, height) => provider.Sample(random, new WorldGenerationContext(minY, height));
    }

    // A plugin's provider always has a state, so it never leaves a rule-based provider's block as it is.
    private StateProvider PluginProvider(JsonElement json, string? type)
    {
        var provider = this.PluginValue<IBlockStateProvider>(json, type, "block state provider");

        return (_, random, position) => provider.GetState(random, position);
    }

    private BlockPredicate PluginPredicate(JsonElement json, string? type)
    {
        var predicate = this.PluginValue<IBlockPredicate>(json, type, "block predicate");

        return predicate.Test;
    }

    // The API's rule tests see the block and the random, not the position 26.3's also see.
    private RuleTest PluginRule(JsonElement json, string? type)
    {
        var rule = this.PluginValue<IRuleTest>(json, type, "rule test");

        return (block, _, random) => rule.Test(block, random);
    }
}
