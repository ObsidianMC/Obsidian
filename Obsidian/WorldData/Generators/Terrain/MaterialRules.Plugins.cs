using System.Text.Json;

namespace Obsidian.WorldData.Generators.Terrain;

internal sealed partial class MaterialRules
{
    // A rule of a type beyond vanilla's (see IMaterialTypes), which gives the plugin's code the block's context, or
    // null when the type isn't one of those.
    private Func<MaterialContext, IBlock?>? CompilePluginRule(string type, JsonElement json)
    {
        if (this.types?.ReadRule(type, json, this) is not ICustomSurfaceRule rule)
            return null;

        return context => rule.Apply(context.Surface);
    }

    // A condition of a type beyond vanilla's, like CompilePluginRule.
    private Func<MaterialContext, bool>? CompilePluginCondition(string type, JsonElement json)
    {
        if (this.types?.ReadCondition(type, json, this) is not ICustomSurfaceCondition condition)
            return null;

        return context => condition.Test(context.Surface);
    }

    /// <summary>
    /// A material context as plugins' rules and conditions read it (see <see cref="ISurfaceContext"/>).
    /// </summary>
    /// <remarks>
    /// The rules and conditions a plugin's object holds were compiled for 26.3 as it was read (see
    /// <see cref="CompiledSurfaceRule"/>), and evaluate with this context; one the plugin made itself evaluates
    /// when it's one of the plugin's types.
    /// </remarks>
    private sealed class PluginSurfaceContext(MaterialContext context) : ISurfaceContext
    {
        public int BlockX => context.X;

        public int BlockY => context.Y;

        public int BlockZ => context.Z;

        public string Biome => context.Biome;

        public int SurfaceDepth => context.SurfaceDepth;

        public int StoneDepthAbove => context.Above;

        public int StoneDepthBelow => context.Below;

        public int WaterHeight => context.Water;

        public bool Steep => context.Steep;

        public int MinSurfaceLevel => context.MinSurface;

        public IBlock? Apply(ISurfaceRule rule) => rule switch
        {
            CompiledSurfaceRule compiled => compiled.Rule(context),
            ICustomSurfaceRule custom => custom.Apply(this),
            _ => throw Unsupported(rule)
        };

        public bool Test(ISurfaceCondition condition) => condition switch
        {
            CompiledSurfaceCondition compiled => compiled.Condition(context),
            ICustomSurfaceCondition custom => custom.Test(this),
            _ => throw Unsupported(condition)
        };

        private static NotSupportedException Unsupported(IRegistryResource value) =>
            new($"{value.Type} wasn't read for the 26.3 material rules; only the rules and conditions read in a "
                + "plugin's rule or condition, and plugins' own types, evaluate there.");
    }
}
